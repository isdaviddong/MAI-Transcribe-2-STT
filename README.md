# MAI-Transcribe-2 麥克風聽寫

Windows 上的 .NET 8 主控台程式，使用 **[MAI-Transcribe-2（Microsoft 官方文件）](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/mai-transcribe)** 辨識語音。按 Enter 開始錄製一小段麥克風音訊，再按 Enter 停止；程式上傳 WAV 至 Azure Speech 的 Fast Transcription API，轉成臺灣繁體後印出文字。可重複錄音；輸入 `q` 結束。這是**錄完後轉寫**，不是即時串流辨識。

## 需求與設定

需要 .NET 8 SDK、Windows 麥克風與網路連線。Azure 資源請依[官方 MAI-Transcribe-2 前置需求與支援區域](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/mai-transcribe#prerequisites)，使用**支援 MAI-Transcribe-2 的 Microsoft Foundry Speech 資源**；一般 Azure Speech 資源或其他模型的設定不能假設適用。請從**同一個資源**取得金鑰及 HTTPS 端點（例如 `https://your-resource.cognitiveservices.azure.com/`），不要輸入 Speech SDK 的即時語音端點、其他資源的金鑰，或模型部署 URL。程式會在請求中指定 `MAI-Transcribe-2`，**沒有另外一組模型專用的金鑰或端點**。程式不內建任何金鑰或端點。

在 PowerShell 設定目前視窗的環境變數，再啟動程式：

```powershell
$env:AZURE_SPEECH_KEY = Read-Host "MAI-Transcribe-2 Speech resource key"
$env:AZURE_SPEECH_ENDPOINT = Read-Host "Same Speech resource endpoint (https://...)"
dotnet run
```

也可以直接執行 `dotnet run`：如果未設定 `AZURE_SPEECH_KEY` 或 `AZURE_SPEECH_ENDPOINT`，啟動時會逐一提示輸入缺少的值。程式提示輸入金鑰時不顯示字元；**PowerShell 的 `Read-Host` 指令則會顯示輸入的金鑰**。直接在程式中輸入的值僅供本次執行，不會寫入環境變數或檔案；在 PowerShell 設定的變數則保留於目前視窗。若使用重新導向輸入、無法互動，請先設定兩個環境變數。

## 聽寫操作

程式使用預設麥克風、16 kHz／16-bit／單聲道 WAV；請先確認 Windows 已開放麥克風權限。

1. 在「按 Enter 開始錄音」提示時按 Enter，對麥克風說話。
2. 再按 Enter 停止錄音，等待辨識與繁體轉換結果顯示。
3. 重複上述操作可繼續聽寫；輸入 `q` 並按 Enter 結束。

未指定語言時由模型自動辨識。

## 中英混合的術語提示

MAI-Transcribe-2 的轉寫 API 不支援自由格式的提示詞；程式改用 `phraseList.phrases` 提示繁體中文軟體開發用語和英文術語，例如「程式碼」、`.NET`、`GitHub`、`Docker`。可直接編輯 `Program.cs` 中的清單，加入專案專有名詞。程式不指定 `locales`，以保留中英夾雜語音的自動語言辨識。

MAI-Transcribe-2 的語言表將 `zh` 列為簡體中文，因此程式在收到結果後以 [OpenCC](https://github.com/BYVoid/OpenCC) 的 `s2twp` 規則將簡體轉為臺灣正體（含常用詞彙），再輸出。英文技術術語維持原樣；這是**文字後處理**，不能修正語音辨識錯誤。詞彙或多義字仍可能需要人工校對。

請勿將金鑰放進程式碼。若金鑰曾公開分享，請在 Azure 入口網站重新產生並更新環境變數。

## API 參考

- [MAI-Transcribe-2 用法與支援區域](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/mai-transcribe)
- [Transcriptions - Transcribe (2025-10-15)](https://learn.microsoft.com/en-us/rest/api/speechtotext/transcriptions/transcribe?view=rest-speechtotext-2025-10-15)

程式以 `multipart/form-data` POST `audio` 與 JSON `definition`，指定 `enhancedMode.enabled=true`、`enhancedMode.model=MAI-Transcribe-2` 和乾淨的轉寫格式（`clean`）；若要保留口頭贅字可改成 `verbatim`。
