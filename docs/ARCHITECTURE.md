# 天幕島 · 陣法圖（架構說明）

## 總綱

```
┌──────────────── TianMu.Island（宿主 / 天幕）─────────────────┐
│                                                              │
│  IslandWindow ── 透明置頂視窗 + 膠囊 + 彈簧動畫 + IIslandHost │
│       ▲                                                      │
│       │ Post(IslandActivity) / Dismiss(moduleId)             │
│       │                                                      │
│  ModuleManager ── 發現、祭煉、收功所有法器                    │
│       │                                                      │
└───────┼──────────────────────────────────────────────────────┘
        │ 只依賴 TianMu.Core 契約
┌───────┴────────┬──────────────────┬───────────────────┐
│ WelcomeModule  │ MusicModule (待)  │ PomodoroModule (待)│ …
└────────────────┴──────────────────┴───────────────────┘
```

- **宿主只管三件事**：視窗管理、渲染、物理動畫。
- **法器只管一件事**：感知某種「萬象」（音樂、通知、時間…），轉成 `IslandActivity` 交給宿主。
- **契約層 `TianMu.Core`** 不依賴 WPF，未來外部法器可以獨立成專案、編譯成 DLL 丟進 `modules\` 資料夾即可被載入。

## 視窗符咒

| 需求 | 做法 |
|---|---|
| 無邊框 | `WindowStyle=None` |
| 背景透明 | `AllowsTransparency=True` + `Background=Transparent`（WPF 自動設 `WS_EX_LAYERED`） |
| 透明處滑鼠穿透 | 分層視窗的逐像素命中測試，透明像素自動穿透 |
| 整窗穿透（預留） | `WS_EX_TRANSPARENT`，見 `IslandWindow.SetClickThrough` |
| 置頂 | `Topmost=True` + 每 3 秒 `SetWindowPos(HWND_TOPMOST)` 重新宣告 |
| 不進 Alt+Tab / 工作列 | `ShowInTaskbar=False` + `WS_EX_TOOLWINDOW` |
| 點擊不搶焦點 | `WS_EX_NOACTIVATE` + `ShowActivated=False` |
| 高 DPI | `app.manifest` 宣告 PerMonitorV2 |

## 彈簧物理

`Physics/Spring.cs` 是一個阻尼諧振子：

```
a = (-k·(x - target) - c·v) / m
```

- 膠囊的**寬**、**高**各用一組彈簧，頻率略不同 → 展開時「先橫拉、後縱彈」，產生果凍感。
- **內容顯現度**用一組接近臨界阻尼的彈簧（ζ≈0.93），文字淡入不會閃爍。
- 以 `CompositionTarget.Rendering` 逐幀推進，且全部彈簧靜止時自動解除訂閱 → 閒置時 0 CPU。
- 固定 1/480 秒子步長積分，任何更新率下手感一致。

調手感只要改 `IslandWindow.xaml.cs` 頂端的 `stiffness` / `damping`：
- 想更 Q 彈 → 降低 damping
- 想更俐落 → 提高 stiffness

## 撰寫新法器

```csharp
public sealed class MyModule : IIslandModule
{
    public string Id => "me.my-module";
    public string DisplayName => "我的法器";

    public Task InitializeAsync(IIslandHost host, CancellationToken ct)
    {
        // 盡快返回；長時間監聽請自己開背景工作，並尊重 ct
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                host.Post(new IslandActivity(Id, "叮！", "某事發生了",
                    ActivityPriority.Alert, TimeSpan.FromSeconds(3)));
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }, ct);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- 內建法器：放在 `src/TianMu.Island/Modules/`，自動被發現。
- 外部法器：新建類別庫引用 `TianMu.Core`，把 DLL 放進 exe 同層的 `modules\`。

## 後續修煉路線

1. **金丹 · 萬象樂律**：`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` 取曲目與封面；NAudio `WasapiLoopbackCapture` + FFT 取頻譜。
2. **金丹 · 傳音入密**：`Windows.UI.Notifications.Management.UserNotificationListener`。
3. **金丹 · 靈識感應**：`WM_DEVICECHANGE`、Core Audio 音量回呼、WMI 亮度事件。
4. **元嬰 · 專注陣法**：番茄鐘法器 + 可自訂展開面板（`IslandActivity` 擴充為可攜帶自訂 UI）。
