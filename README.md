# PDF 工具平台(內部使用版)

C# (.NET 8)。四個核心功能:合併、分割、浮水印(含分布與透明度)、PDF轉圖檔。
提供兩種介面,共用同一套 `PdfToolPlatform.Core` 業務邏輯:

- **PdfToolPlatform.UI**:Windows桌面應用(WPF),單機使用
- **PdfToolPlatform.Web**:ASP.NET Core Razor Pages,架在你的主機上,內網其他電腦用瀏覽器連進來使用

## Web版(內網共用)架構重點

- **部署方式**:在你的主機上執行 `dotnet run --project PdfToolPlatform.Web`(或發布後用`dotnet PdfToolPlatform.Web.dll`常駐執行),預設監聽 `http://0.0.0.0:5080`,同事只要在瀏覽器輸入 `http://<你的主機內網IP>:5080` 就能使用,不需要在自己電腦安裝任何東西。
- **檔案不落地保存**:每次上傳/處理都在獨立的暫存資料夾進行(`%TEMP%\PdfToolPlatformWeb\<隨機ID>\`),處理完成回傳結果後立即刪除。另外有一個背景清理服務,每15分鐘掃描一次,自動清除超過1小時的殘留暫存資料夾(防止異常中斷、瀏覽器中途關閉等情況留下檔案)。
- **免登入**:依你的需求,內網信任、直接開放使用,沒有加驗證機制。如果之後想加,`Program.cs`是加`AddAuthentication`/`AddAuthorization`的地方。
- **僅限內網**:`http://0.0.0.0:5080` 會監聽主機所有網卡,如果你的主機同時有對外網卡(例如VPN、雙網卡環境),**務必用Windows防火牆規則把5080 port限制在內網IP網段**,避免意外對外曝露。如果主機只有內網IP,風險較低但仍建議加防火牆規則作為保險。
- **上傳大小限制**:目前設定為500MB,如果PDF檔案(尤其是掃描件合併)會更大,可以在`Program.cs`調整`MaxRequestBodySize`。

### Windows防火牆設定範例(PowerShell,系統管理員權限執行)

```powershell
New-NetFirewallRule -DisplayName "PdfToolPlatform-Internal" `
  -Direction Inbound -LocalPort 5080 -Protocol TCP `
  -RemoteAddress 192.168.1.0/24 -Action Allow
```

把 `192.168.1.0/24` 換成你們公司實際的內網網段,這樣即使主機有對外網卡,也只有內網範圍能連進5080 port。

### 讓服務開機自動啟動(選用)

小規模、偶爾使用的情境下,你也可以只在需要時手動 `dotnet run` 即可。若想常駐,建議用 `dotnet publish` 產出後,搭配Windows工作排程器(Task Scheduler)在開機時啟動,或註冊成Windows服務(需要加`Microsoft.Extensions.Hosting.WindowsServices`套件,目前骨架未加,規模變大再考慮)。

## 資料安全設計原則

因為是公司內部工具、需確保原始資料不外洩,整個專案在架構上就排除了外流的可能性:

1. **零網路呼叫**:所有PDF處理(PDFsharp、Docnet.Core/PDFium)都是本機函式庫運算,程式碼裡沒有任何 `HttpClient`、雲端SDK或API呼叫。你可以直接搜尋整個專案確認沒有`http`字樣出現在業務邏輯中。
2. **關閉.NET遙測**:專案檔中已加入 `TelemetryOptOut`,避免.NET工具鏈本身回傳使用統計。
3. **NuGet套件選擇考量**:PDFsharp(MIT)、Docnet.Core(BSD/Apache)、SixLabors.ImageSharp(MIT)皆為純本機運算函式庫,無已知的資料回傳行為。**建議部署前用公司的內部NuGet鏡像或離線套件包**,避免建置時對外連線到公開NuGet源(這是唯一會在建置階段觸網的地方,執行期完全不會)。
4. **建議加強項目(視公司資安政策決定是否採用)**:
   - 部署後用防火牆規則直接封鎖此應用程式的對外連線(白名單機制),多一層保險
   - 若檔案含高敏感內容,輸出資料夾建議設在公司內部的加密磁碟或受控共用資料夾
   - 若需要留存處理紀錄以供稽核,可以加一個純本機的操作日誌(不含檔案內容,只記錄檔名/時間/操作類型)

## 專案結構

```
PdfToolPlatform/
├── PdfToolPlatform.Core/          # 核心邏輯,不依賴UI,方便單元測試與未來重用
│   ├── Models/
│   │   └── WatermarkOptions.cs    # 浮水印參數(文字/透明度/角度/分布方式)
│   └── Services/
│       ├── PdfMergeService.cs
│       ├── PdfSplitService.cs
│       ├── WatermarkService.cs
│       └── PdfToImageService.cs
├── PdfToolPlatform.UI/             # WPF桌面應用(單機版)
│   └── Views/MainWindow.xaml(.cs)  # 四個功能分頁
├── PdfToolPlatform.Web/            # ASP.NET Core Razor Pages(內網共用版)
│   ├── Pages/                      # 首頁 + 四個功能頁面
│   ├── Services/
│   │   ├── TempWorkspace.cs        # 每次請求的獨立暫存資料夾
│   │   └── TempCleanupService.cs   # 背景清理殘留暫存檔
│   └── appsettings.json            # Kestrel監聽設定
└── PdfToolPlatform.Tests/          # xUnit單元測試(針對Core)
```

## 建置方式

1. 需要 [.NET 8 SDK](https://dotnet.microsoft.com/download) 與 Windows(PDFsharp-GDI依賴System.Drawing,僅支援Windows)
2. 用 Visual Studio 2022 開啟 `PdfToolPlatform.sln`,還原NuGet套件後建置,或用命令列:

```powershell
dotnet restore
dotnet build

# 桌面版(單機使用)
dotnet run --project PdfToolPlatform.UI

# Web版(內網共用,啟動後同事用瀏覽器連 http://<主機IP>:5080)
dotnet run --project PdfToolPlatform.Web
```

3. 執行測試:

```powershell
dotnet test
```

## 部署Web版到正式使用(發布)

```powershell
dotnet publish PdfToolPlatform.Web -c Release -o C:\PdfToolPlatform\publish
cd C:\PdfToolPlatform\publish
dotnet PdfToolPlatform.Web.dll
```

發布後記得依上面的防火牆設定範例限制連線來源,並確認主機的休眠/睡眠設定關閉(否則同事連線時主機睡著會連不上)。

## 功能說明

### 合併
- 採「工作區」模式:可以分好幾次加入PDF檔案,加入後隨時能刪除某一個、或用↑↓調整順序,確認排序後再按「合併並下載」
- 工作區狀態綁定瀏覽器Session(30分鐘無操作會過期),暫存檔案落地在主機的暫存資料夾,合併完成或按「清空重新開始」都會立即刪除;若中途放著不管,既有的背景清理服務(TempCleanupService)一樣會在1小時後自動清掉殘留檔案
- 注意:工作區是綁在瀏覽器的,換一台電腦或清除瀏覽器Cookie會看到空的工作區(這是設計上刻意的,不同使用者/裝置的暫存檔不會互相看到對方的檔案)

### 分割
兩種模式:
- **固定頁數**:例如每5頁切一份
- **頁碼範圍**:自訂多組範圍,例如 `1-3,4-10,11-12`

### 浮水印
- **兩種類型**:文字浮水印,或上傳圖片(Logo)作為浮水印,擇一使用
- **透明度**:0.0(幾乎看不見)~ 1.0(完全不透明),UI用滑桿調整。圖片浮水印的透明度是先烘進圖片本身的alpha色版再嵌入PDF,確保不同PDFsharp版本都能穩定呈現半透明效果
- **分布方式**:
  - 置中單一
  - 斜角單一(常見的"機密"字樣大字斜貼)
  - 滿版重複貼(平舖),適合防拍照外流的場景,間距可調
- **起始位置偏移**:以頁面正中央為基準點,可調整水平/垂直偏移量(pt),讓浮水印不一定要貼在正中央,例如偏移到接近右下角。滿版平舖模式下,偏移量會讓整個貼圖網格一起平移,可用來微調對齊位置
- 可調整旋轉角度;圖片浮水印可調整寬度(高度依原圖比例自動計算)
- **預覽功能(Web版)**:上傳檔案後可先按「預覽第一頁」,伺服器只處理第一頁並轉成圖片顯示,不用每次調參數都下載整份PDF確認效果。原始檔與浮水印圖片都會暫存在該瀏覽器Session的資料夾,調整參數重複預覽不需要重新上傳,滿意後再按「套用並下載」處理全部頁面

**字型解析(重要)**:本專案使用的PDFsharp套件是跨平台核心版,不會自動讀取作業系統安裝的字型,需要自訂`IFontResolver`(見`PdfToolPlatform.Core/Fonts/WindowsFontResolver.cs`)在應用程式啟動時指定 `GlobalFontSettings.FontResolver`,直接從Windows系統字型資料夾(`C:\Windows\Fonts`)讀取字型檔案。Web版在`Program.cs`、桌面版在`App.xaml.cs`各自註冊一次。目前對照表涵蓋微軟正黑體、微軟雅黑、新細明體、SimSun、Arial、Times New Roman;若要用其他字型,在`WindowsFontResolver.cs`的`FamilyMap`裡加一筆對照即可(字型檔名可以在`C:\Windows\Fonts`資料夾裡確認,或在檔案總管的字型設定頁面查看實際檔名)。

### PDF轉圖檔
用PDFium引擎(與Chrome同款渲染核心)本機渲染,可調DPI(150適合預覽、300適合列印品質),支援PNG/JPEG輸出。

## 已知限制與後續可擴充方向

- 目前UI採code-behind直接呼叫Service,規模變大後建議改成MVVM(ViewModel + ICommand),已預留`ViewModels`資料夾
- 若檔案量大、需要批次處理(例如整個資料夾的PDF批次轉圖檔),可以在Core層加一個`BatchProcessor`,UI再加進度條顯示
