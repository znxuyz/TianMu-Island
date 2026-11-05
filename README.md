<div align="center">

# ☯ 天幕島 · TianMu-Island

**懸於螢幕之巔的一方靈台 —— Windows 頂部動態效率法寶**

*「天幕垂落，萬象歸島。」*

![platform](https://img.shields.io/badge/平台-Windows%2010%2F11-0a0a0f?style=flat-square)
![dotnet](https://img.shields.io/badge/.NET-10-E8C872?style=flat-square)
![ui](https://img.shields.io/badge/UI-WPF-0a0a0f?style=flat-square)
![stage](https://img.shields.io/badge/版本-v0.4-C8A869?style=flat-square)

</div>

---

## 📜 法寶緣起

天幕島是一件常駐於 Windows 螢幕頂部正中央的懸浮法器。
它以一枚玄黑的膠囊示人，靜時如墨玉懸空，觸之則如靈液般拉伸、回彈、流轉。

音律、訊息、時辰、專注 —— 世間萬象，皆可收於這方寸天幕之中。

## ✨ 法寶神通（修煉進度）

| 境界 | 神通 | 說明 | 狀態 |
|---|---|---|---|
| 築基 | **天幕成形** | 無邊框、置頂、透明背景、滑鼠穿透的黑色膠囊 | ✅ |
| 築基 | **靈液彈簧** | 阻尼彈簧物理演算法，懸停 / 點擊時果凍般變形 | ✅ |
| 築基 | **法器總管** | 外掛式架構，功能皆為可獨立載入的「法器模組」 | ✅ |
| 築基 | **洞府設施** | 設定面板、開機啟動、系統匣圖示、自動更新（含 SHA-256 校驗） | ✅ |
| 築基 | **玄漆描金** | 專屬設計語言：五色、內嵌思源宋體、描金頂線、農曆與直書時辰 | ✅ |
| 金丹 | **萬象樂律** | 接管系統媒體（含瀏覽器 YouTube）：曲名、歌手、封面、同步歌詞、音訊頻譜、播放控制 | ✅ |
| 金丹 | **傳音入密** | 系統通知從天幕中滑出 | ⏳ |
| 金丹 | **靈識感應** | USB 插拔、藍牙耳機、音量 / 亮度變化提示 | ⏳ |
| 元嬰 | **專注陣法** | 番茄鐘 + 修為五境（練氣→化神）、突破時渡劫雷光、散功不扣修為 | ✅ |
| 元嬰 | **清泉** | 每 45 分鐘提醒喝水、每日 8 盞，專注時與離開座位時不打擾 | ✅ |
| 元嬰 | **靈氣粒子** | 閒置一分鐘後金塵自膠囊兩端飄散，境界越高越多 | ✅ |

## 🧰 煉器材料（技術棧）

- **C# 13 / .NET 10 (LTS)** — 主語言與執行環境
- **WPF** — GPU 加速的向量渲染，圓角、陰影、動畫天生絲滑
- **Win32 P/Invoke** — 置頂、Alt+Tab 隱身、不搶焦點、滑鼠穿透
- **WinRT (Windows SDK 投射)** — 未來的媒體控制、通知監聽
- **單檔自封發佈** — 一顆 `.exe`，免安裝 .NET 即可執行

## 🚀 開爐指南（本地執行）

### 1. 準備爐鼎（只需裝一次）

安裝 **.NET 10 SDK**（約 200MB，5 分鐘內完成）：

```powershell
winget install Microsoft.DotNet.SDK.10
```

> 或到 <https://dotnet.microsoft.com/download/dotnet/10.0> 下載安裝程式。
> 編輯器推薦 **VS Code + C# Dev Kit**，或 **Visual Studio 2026 Community**（二擇一，非必要）。

### 2. 取得法寶原胚

```powershell
git clone https://github.com/znxuyz/TianMu-Island.git
cd TianMu-Island
```

### 3. 催動法寶

```powershell
dotnet run --project src/TianMu.Island
```

螢幕頂部正中央會浮現天幕膠囊：

- 🖱️ **懸停**：膠囊微微脹起
- 👆 **左鍵點擊**：展開成大面板（顯示時辰、干支年），再點一次收回
- 📋 **右鍵膠囊**：開啟設定、暫時隱藏、結束
- 🔔 **系統匣圖示**（右下角）：左鍵開設定、右鍵選單；再次執行 exe 也會直接打開設定
- 🧘 **展開天幕按「凝神」**：開始 25 分鐘專注，完成後累積修為；右鍵膠囊或系統匣也找得到「凝神」「飲水」
- 🎵 **播放音樂**（YouTube / Spotify / 任何播放器）：膠囊變成「封面 + 歌名 + 跳動頻譜」，換歌時自動彈開；
  點開可看同步歌詞、進度條，並可上一首 / 暫停 / 下一首

### 免編譯：直接下載

到 [Releases](https://github.com/znxuyz/TianMu-Island/releases) 下載 `TianMuIsland-win-x64.exe`（ARM 筆電選 `win-arm64`），雙擊即可執行。
之後有新版時，天幕島會自己提醒你，一鍵更新。

> 建議把 exe 放在「文件」或「桌面」等個人資料夾，自動更新才有權限替換檔案。

### 4. 法寶出爐（打包成單一 .exe）

```powershell
.\scripts\publish.ps1
```

產物位於 `dist\win-x64\TianMuIsland.exe`，可直接複製到任何 Windows 10/11 電腦執行。

> 每次推送到 `main`，GitHub Actions 也會自動在雲端煉製一份 exe，可在 **Actions → 最新一次執行 → Artifacts** 下載。

## 🎵 萬象樂律如何運作

| 能力 | 來源 |
|---|---|
| 曲名、歌手、封面、播放控制 | Windows 系統媒體（SMTC）—— 瀏覽器與播放器自己回報給 Windows，**免登入、免金鑰** |
| YouTube 標題清洗 | 內建清洗器：「周杰倫 Jay Chou【晴天】Official MV」→ 歌手「周杰倫 Jay Chou」、歌名「晴天」 |
| 同步歌詞 | [LRCLIB](https://lrclib.net) 開放歌詞庫（只送出歌名與歌手做查詢） |
| 音訊頻譜 | WASAPI 迴路擷取系統輸出聲音 + 自製 FFT，藍牙耳機切換時自動重接 |

> 歌詞庫以西洋與日文歌最齊全，部分華語歌曲可能查無歌詞，後續會再加入其他來源。

## 🎨 設計語言

天幕島採用自訂的「玄漆描金」設計語言：深色漆面、頂緣一線描金、內嵌思源宋體、朱砂印章只用一處。
完整規範見 [docs/DESIGN.md](docs/DESIGN.md)。

## 🏯 洞府佈局（專案結構）

```
TianMu-Island/
├─ src/
│  ├─ TianMu.Core/            # 核心契約：法器介面、異象資料（外掛只需引用它）
│  ├─ TianMu.Island/          # 宿主：視窗、渲染、彈簧物理、法器總管
│  ├─ TianMu.Modules.Music/   # 法器「萬象樂律」：媒體接管、歌詞、頻譜
│  └─ TianMu.Modules.Focus/   # 法器「專注陣法」「清泉」：番茄鐘、修為、飲水
│     ├─ Interop/             # Win32 符咒（P/Invoke）
│     ├─ Physics/             # 阻尼彈簧
│     ├─ Modules/             # 法器總管 + 內建法器
│     ├─ Settings/ Shell/     # 設定、系統匣、開機啟動、設定面板
│     ├─ Update/              # 自動更新
│     ├─ Theme/               # 設計語言資源（Tokens.xaml）
│     ├─ IslandWindow.xaml    # 天幕本體
│     └─ App.xaml             # 開爐入口
├─ tests/TianMu.Tests/        # 試煉（單元測試）
├─ docs/ARCHITECTURE.md       # 陣法圖（架構說明）
├─ docs/DESIGN.md             # 設計語言「玄漆描金」
├─ scripts/build-fonts.py     # 依介面用字裁切內嵌宋體
├─ scripts/publish.ps1        # 一鍵出爐
└─ .github/workflows/         # 雲端煉器（CI）
```

詳見 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## 📿 道規

本法寶以 Vibe Coding 之法煉成：道友執掌心念，護法煉器師執筆成器。

---

<div align="center"><sub>天幕之下，皆為道場。</sub></div>
