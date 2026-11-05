# 天幕島 · 設計語言「玄漆描金」

> 以宋代漆器為本：深如夜色的玄漆，只在光落下的地方描一線金。
> 高級感不靠漸層、霓虹與特效堆砌，而是來自克制、材質與節奏。

所有顏色、字體、元件樣式都定義在 [`src/TianMu.Island/Theme/Tokens.xaml`](../src/TianMu.Island/Theme/Tokens.xaml)。
**任何畫面都只能引用那裡的資源，不可自行寫死色碼。**

## 五條鐵律

| | 原則 | 落實方式 |
|---|---|---|
| 壹 | **一個光源** | 高光只來自正上方：膠囊與面板只有頂緣一線描金（`TopHairlineBrush`），兩側淡出 |
| 貳 | **一處重彩** | 朱砂只用於印章、緊急、渡劫；同一畫面最多一處 |
| 參 | **三層為限** | 島內資訊最多三層：主題、說明、操作 |
| 肆 | **墨暈而現** | 文字由模糊暈開到清晰，不滑入、不彈跳；只有膠囊本體有彈簧彈性 |
| 伍 | **器有其色** | 專注是金、飲水是天青、警示是朱砂 |

## 五色

| 名稱 | 色碼 | 資源鍵 | 用途 |
|---|---|---|---|
| 玄漆 | `#0E0C0A` | `XuanQiBrush`、`LacquerBrush`（含受光漸層） | 膠囊與面板本體 |
| 描金 | `#C8A869` | `MiaoJinBrush` | 主強調、專注、修為、開關開啟 |
| 月白 | `#ECE6D8` | `YueBaiBrush` | 文字 |
| 天青 | `#8FB3AD` | `TianQingBrush` | 飲水、健康、平靜 |
| 朱砂 | `#B23A27` | `ZhuShaBrush` | 印章、緊急、渡劫 |

月白的透明層級：`TextSecondaryBrush`（次要文字，約 62%）、`TextTertiaryBrush`（提示，約 42%）、
`HairlineBrush`（分隔線）、`TrackBrush`（進度條底軌、開關關閉）。

## 字

| 用途 | 資源鍵 | 字型 |
|---|---|---|
| 標題、固定文字 | `SerifFont`（搭配 `FontWeight="Bold"`） | 內嵌思源宋體 Bold |
| 時間、數字 | `SerifMediumFont` | 內嵌思源宋體 Medium，等寬數字 `Typography.NumeralAlignment="Tabular"` |
| 歌名、訊息等動態文字 | `SansFont` | 系統微軟正黑體（任何字都顯示得出來） |
| 圖示 | `IconFont` | Segoe Fluent Icons / MDL2 Assets |

- 宋體只內嵌介面用到的字（約 300 字、每個字重約 120 KB）。**新增介面文字後請執行 `python scripts/build-fonts.py`**，CI 會檢查是否缺字。
- WPF 沒有字距屬性；寬字距標題用 `theme:Typo.SpacedText="一般"`（字間插入細空格）。

## 元件

| 樣式鍵 | 說明 |
|---|---|
| `TianMu.Toggle` | 開關：描金軌道 + 月白圓鈕 |
| `TianMu.Button.Primary` | 描金實心按鈕：一個畫面只放一個主要動作 |
| `TianMu.Button.Ghost` | 細框按鈕：次要動作 |
| `TianMu.Button.Icon` | 視窗控制鈕 |
| `TianMu.ContextMenu` / `TianMu.MenuItem` / `TianMu.Separator` | 右鍵與系統匣選單 |

## 東方細節

- **縱書**：時辰、境界等短詞直排於面板側邊（展開時辰面板右側的「未／時」）。
- **印章**：境界徽記用方印、渡劫成就用圓印，取代一般 App 的獎盃圖示。設定面板標頭的「幕」字方印是全畫面唯一的朱砂。
- **農曆**：時辰面板顯示干支年與農曆月日（例如「丙午年 · 九月廿七」）。

## 戒律：絕不出現

霓虹發光、紫藍漸層、滿版毛玻璃、龍鳳剪貼圖、大紅大金年節風、表情符號當圖示、每個元素都加陰影。

## 互動提案

視覺提案（含可點擊原型、修為五境、三種方向比較）：<https://claude.ai/artifact/TKHsBsJUdfyuDBpPkxtGUV>（私人連結）
