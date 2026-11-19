# 絲音 Silk

一個 Windows 原生（WPF / .NET 9）的音樂播放器介面實驗：只留封面、歌詞和聲音，視窗顏色跟著正在播放的封面變化，背後有緩慢流動的絲綢光效。

> **非官方項目。** 本項目與騰訊、QQ 音樂沒有任何關係，僅供個人學習與使用。它透過第三方套件 [`@yakult-green-tea/qq-music-api`](https://www.npmjs.com/package/@yakult-green-tea/qq-music-api) 讀取帳號可正常取得的內容，不繞過付費限制，也不解密受保護的檔案或歌詞。

## 功能

- 首頁、音樂庫、喜歡、歌手頁、專輯頁，以及歌曲、專輯、歌單搜尋與搜尋紀錄
- 播放器：大封面、逐行彈性捲動的歌詞（遠處模糊、間奏圓點）、真實響度波形進度條（波紋／條形／普通）
- 播放清單：右鍵選單、下一首播放、拖動排序、移除與清空
- 背景播放與系統匣、媒體鍵、工作列縮圖按鈕與進度、全屏模式、記住視窗大小與位置
- 播放器與介面的多項自訂選項

## 執行需求

- Windows 10 或 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/)（音樂服務以本機子程序執行，只監聽 127.0.0.1）

## 建置與執行

```bash
cd bridge
npm install
cd ../src/Silk
dotnet run
```

不登入、不連網也能看介面：示範模式使用程式即時合成的虛構歌曲與自繪封面。

```bash
dotnet run -- --demo
```

登入資料以 Windows DPAPI 加密，存放在 `%LOCALAPPDATA%\Silk`，不會寫進項目資料夾。

## 安卓版

`android` 是用 Kotlin 與 Jetpack Compose 寫的安卓版，介面照手繪草圖：首頁一張卡片放「猜你喜歡」、「每日 30 首」、推薦與歌單，底部是懸浮的迷你播放條；播放器分兩頁，左右滑動切換封面頁與歌詞頁。播放用 Media3，可在背景播放，並支援通知欄與耳機按鍵。

手機上沒有 Node.js，所以 App 內建 [nodejs-mobile](https://github.com/nodejs-mobile/nodejs-mobile)（Node 18），直接在手機上執行和桌面版相同的 `bridge`。`bridge/mobile.cjs` 補上 Node 18 缺少的幾個全域功能。

建置需要：

- JDK 17 或 21、Android SDK（compileSdk 35）、NDK 27.2 與 CMake 3.22.1
- 解壓好的 nodejs-mobile Android 版本，路徑寫在 `android/local.properties`：

```properties
sdk.dir=D\:/DevTools/Android/Sdk
nodejs.mobile.dir=D\:/DevTools/nodejs-mobile
```

- 先在 `bridge` 執行過 `npm install`（建置時會把 `bridge` 連同 `node_modules` 打包進 App）

```bash
cd android
./gradlew assembleDebug
```

不登入也能看介面：用示範模式啟動。

```bash
adb shell am start -n app.silk/.ui.MainActivity --ez demo true
```

## 項目結構

- `src/Silk`：WPF 應用程式
- `android`：安卓版
- `bridge`：本機音樂服務（Node.js）
- `assets/icon`：圖標與產生圖標的腳本
