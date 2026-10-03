using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using OpenccNetLib;

Console.OutputEncoding = Encoding.UTF8;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("此程式需要 Windows 麥克風。");
    return 1;
}

var key = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine("請先設定 AZURE_SPEECH_KEY（支援 MAI-Transcribe-2 的 Speech 資源金鑰）；無法從重新導向的輸入互動取得金鑰。");
        return 1;
    }

    Console.Write("MAI-Transcribe-2 可用的 Azure Speech 資源金鑰（輸入時不顯示）：");
    key = ReadSecret();
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine("Speech 資源金鑰不可為空白。");
        return 1;
    }
}

var endpoint = Environment.GetEnvironmentVariable("AZURE_SPEECH_ENDPOINT");
if (string.IsNullOrWhiteSpace(endpoint))
{
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine("請先設定 AZURE_SPEECH_ENDPOINT（同一個支援 MAI-Transcribe-2 的 Speech 資源端點）；無法從重新導向的輸入互動取得端點。");
        return 1;
    }

    Console.Write("同一個支援 MAI-Transcribe-2 的 Speech 資源端點（HTTPS URL）：");
    endpoint = Console.ReadLine();
}

if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
{
    Console.Error.WriteLine("AZURE_SPEECH_ENDPOINT 必須是支援 MAI-Transcribe-2 的 Speech 資源 HTTPS URL。");
    return 1;
}

if (WaveInEvent.DeviceCount == 0)
{
    Console.Error.WriteLine("找不到麥克風，請確認裝置與 Windows 的麥克風存取權限。");
    return 1;
}

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var converter = new Opencc(OpenccConfig.S2Twp, isFrozen: true);
var url = new Uri(baseUri.ToString().TrimEnd('/') +
    "/speechtotext/transcriptions:transcribe?api-version=2025-10-15");

while (true)
{
    Console.Write("按 Enter 開始錄音，輸入 q 再按 Enter 結束：");
    var command = Console.ReadLine();
    if (command is null || command.Equals("q", StringComparison.OrdinalIgnoreCase))
        break;

    if (command.Length != 0)
        continue;

    var audio = await RecordAudioAsync();
    Console.WriteLine("轉寫中...");
    var transcript = await TranscribeAsync(httpClient, url, key, audio);
    Console.WriteLine(string.IsNullOrWhiteSpace(transcript) ? "（沒有辨識到語音）" : converter.Convert(transcript));
    Console.WriteLine();
}

return 0;

static string ReadSecret()
{
    var value = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return value.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (value.Length > 0)
                value.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            value.Append(key.KeyChar);
        }
    }
}

static async Task<byte[]> RecordAudioAsync()
{
    using var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
    using var buffer = new MemoryStream();
    using (var writer = new WaveFileWriter(buffer, waveIn.WaveFormat))
    {
        long receivedBytes = 0;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waveIn.DataAvailable += (_, args) =>
        {
            writer.Write(args.Buffer, 0, args.BytesRecorded);
            receivedBytes += args.BytesRecorded;
        };
        waveIn.RecordingStopped += (_, args) =>
        {
            if (args.Exception is { } error)
                stopped.TrySetException(error);
            else
                stopped.TrySetResult();
        };

        waveIn.StartRecording();
        Console.WriteLine("錄音中，按 Enter 停止...");
        Console.ReadLine();
        waveIn.StopRecording();
        await stopped.Task;
        if (receivedBytes == 0)
            throw new InvalidDataException("麥克風未收到音訊，請確認裝置與錄音權限。");
    }

    return buffer.ToArray();
}

static async Task<string> TranscribeAsync(HttpClient client, Uri url, string key, byte[] audio)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, url);
    request.Headers.Add("Ocp-Apim-Subscription-Key", key);

    using var form = new MultipartFormDataContent();
    using var audioContent = new ByteArrayContent(audio);
    audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
    form.Add(audioContent, "audio", "recording.wav");
    form.Add(new StringContent(
        """
        {
          "enhancedMode": {
            "enabled": true,
            "model": "MAI-Transcribe-2",
            "modelOptions": { "transcribeStyle": "clean" }
          },
          "phraseList": {
            "phrases": [
              "程式碼", "資料庫", "前端", "後端", "部署", "套件", "相依性",
              "C#", ".NET", "ASP.NET Core", "Visual Studio", "GitHub", "Git",
              "API", "REST API", "JSON", "SQL", "Docker", "Kubernetes",
              "Azure", "NuGet", "TypeScript", "JavaScript"
            ]
          }
        }
        """,
        Encoding.UTF8), "definition");
    request.Content = form;

    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Azure Speech 回傳 {(int)response.StatusCode} {response.ReasonPhrase}: {error}");
    }

    await using var stream = await response.Content.ReadAsStreamAsync();
    using var result = await JsonDocument.ParseAsync(stream);
    if (!result.RootElement.TryGetProperty("combinedPhrases", out var phrases) ||
        phrases.ValueKind != JsonValueKind.Array)
        throw new InvalidDataException("Azure Speech 回應缺少 combinedPhrases。");

    var texts = new List<string>();
    foreach (var phrase in phrases.EnumerateArray())
    {
        if (!phrase.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Azure Speech 回應的 combinedPhrases 缺少文字。");
        texts.Add(text.GetString()!);
    }

    return string.Join(Environment.NewLine, texts);
}
