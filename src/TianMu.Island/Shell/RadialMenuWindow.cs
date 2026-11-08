using File = System.IO.File;
using Directory = System.IO.Directory;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Island.Interop;

namespace TianMu.Island.Shell;

/// <summary>法陣盤上的一個扇區（宿主整理好的）。</summary>
/// <param name="OnFiles">拖著檔案放到這一格時要做的事；null 表示不收檔案。</param>
/// <param name="IsFurnace">化形爐：收下檔案後整盤換成格式。</param>
internal sealed record RadialItem(string Label, string Caption, Action Invoke, Color? Tone,
    Action<IReadOnlyList<string>>? OnFiles = null, bool IsFurnace = false);

/// <summary>法陣盤需要宿主幫忙的事。</summary>
internal sealed record RadialContext(IFileFurnace? Furnace, Action<string, string> Notify, Func<IReadOnlyList<string>?> PickFiles);

/// <summary>
/// 法陣盤：長按天幕（或拖著檔案停在天幕上半秒）後，在游標處展開的圓形功能選單。
///
///   · 功能盤：按住拖到扇區放開即執行；長按後原地放開則改用點選；點陣眼、點盤外或按右鍵關閉
///   · 入爐化形：把檔案放進「化形爐」，化形爐連同檔案縮進陣眼，整盤換成這些檔案能轉的格式
///   · 煉化：選一個格式，陣眼外圍燒成金色進度環
///   · 出爐：成品留在陣眼，拖到任何地方都行；沒拖走就放回原檔旁邊
///
/// 所有展開都用「金線輪轉」：金線從第一格左緣順時針畫一圈，掃到哪裡扇區就長到哪裡（見 <see cref="RadialGeometry"/>）。
/// 透明、置頂、不搶焦點。透明視窗是 CPU 繪製，所以不用模糊特效（陰影、光暈都用漸層畫好），動畫才會順。
/// </summary>
internal sealed class RadialMenuWindow : Window
{
    private enum Stage { Menu, Absorbing, Formats, Converting, Result, Failed }

    private const double Side = 420;
    private const double HoverPush = 7;

    private static readonly Color Gold = Color.FromRgb(0xC8, 0xA8, 0x69);
    private static readonly Color GoldHi = Color.FromRgb(0xF0, 0xDC, 0xA8);
    private static readonly Color Moon = Color.FromRgb(0xEC, 0xE6, 0xD8);
    private static readonly Color Cinnabar = Color.FromRgb(0xE3, 0x9A, 0x8C);
    private static readonly Brush Face = Frozen(Color.FromRgb(0x1D, 0x17, 0x12));
    private static readonly Brush FaceHot = Frozen(Color.FromRgb(0x3A, 0x2C, 0x1A));
    private static readonly Brush FaceTarget = Frozen(Color.FromRgb(0x4A, 0x1F, 0x17));
    private static readonly Brush Edge = Frozen(Color.FromArgb(0x47, 0xC8, 0xA8, 0x69));
    private static readonly Brush EdgeHot = Frozen(Gold);

    private readonly RadialContext _ctx;
    private readonly double _scale;
    private readonly Point _c = new(Side / 2, Side / 2);
    private readonly Canvas _root;
    private readonly Canvas _ringLayer = new() { Width = Side, Height = Side };
    private readonly Path _outline;
    private readonly Ellipse _tip;
    private readonly Grid _hub;
    private readonly Ellipse _hubRing;
    private readonly TextBlock _hubTitle;
    private readonly TextBlock _hubCaption;
    private readonly Path _progress;
    private readonly Grid _fileIcon;
    private readonly TextBlock _fileExt;
    private readonly bool _animate = SystemParameters.ClientAreaAnimation;
    private readonly DispatcherTimer _dragAwayTimer;

    private Stage _stage = Stage.Menu;
    private IReadOnlyList<RadialItem> _items;
    private readonly List<SliceVisual> _slices = [];
    private string _hubIdleTitle = "天幕";
    private string _hubIdleCaption = "移回取消";
    private TimeSpan _animStart = TimeSpan.Zero;
    private Action<double>? _animFrame;
    private double _animLength;
    private Action? _animDone;
    private int _hot = -1;
    private bool _leftHub;
    private bool _clickMode;
    private bool _done;

    // 入爐與煉化
    private IReadOnlyList<string> _sources = [];
    private IReadOnlyList<FurnaceOption> _options = [];
    private IReadOnlyList<string> _outputs = [];
    private bool _outputsTaken;
    private CancellationTokenSource? _cts;
    private Point _fileFrom;

    private sealed record SliceVisual(Canvas Group, Path Shape, StackPanel Labels, TextBlock Label, double A0, double A1, double Mid,
        ScaleTransform Scale, TranslateTransform Move, Brush LabelBrush);

    private static readonly Brush GoldHiBrush = Frozen(GoldHi);
    private static readonly Brush CinnabarBrush = Frozen(Cinnabar);
    private static readonly Brush HubRingBrush = Frozen(Color.FromArgb(0x8C, 0xC8, 0xA8, 0x69));
    private static readonly Duration HoverTime = new(TimeSpan.FromMilliseconds(130));
    private static readonly IEasingFunction HoverEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <param name="scale">盤面大小（1 = 標準；設定 → 法陣盤 → 大小）。</param>
    public RadialMenuWindow(IReadOnlyList<RadialItem> items, RadialContext ctx, double scale = 1)
    {
        _items = items;
        _ctx = ctx;
        _scale = Math.Clamp(scale, 0.8, 2);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = Side * _scale;
        AllowDrop = true;
        Title = "天幕島 · 法陣盤";

        _root = new Canvas { Width = Side, Height = Side, LayoutTransform = new ScaleTransform(_scale, _scale) };

        // 底下一圈柔和的影子（漸層畫好，不用 DropShadowEffect）
        double shadowR = RadialGeometry.OuterRadius + 46;
        var shadow = new Ellipse
        {
            Width = shadowR * 2, Height = shadowR * 2, IsHitTestVisible = false,
            Fill = Frozen(new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x8C, 0, 0, 0), 0.62),
                    new GradientStop(Color.FromArgb(0x40, 0, 0, 0), 0.8),
                    new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1),
                },
            }),
        };
        Canvas.SetLeft(shadow, _c.X - shadowR);
        Canvas.SetTop(shadow, _c.Y - shadowR);
        _root.Children.Add(shadow);

        // 幾乎透明的底盤：扇區之間的縫也接得到滑鼠與拖放
        var catcher = new Ellipse { Width = (RadialGeometry.OuterRadius + 34) * 2, Height = (RadialGeometry.OuterRadius + 34) * 2, Fill = Frozen(Color.FromArgb(1, 0, 0, 0)) };
        Canvas.SetLeft(catcher, _c.X - catcher.Width / 2);
        Canvas.SetTop(catcher, _c.Y - catcher.Height / 2);
        _root.Children.Add(catcher);

        _outline = new Path
        {
            Stroke = new SolidColorBrush(Gold),
            StrokeThickness = 1.6,
            StrokeDashArray = new DoubleCollection { 0, 9999 },
            IsHitTestVisible = false,
        };
        // 筆尖：亮點＋光暈（漸層）
        _tip = new Ellipse { Width = 18, Height = 18, IsHitTestVisible = false, Opacity = 0,
            Fill = Frozen(new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(GoldHi, 0.18),
                    new GradientStop(Color.FromArgb(0x80, GoldHi.R, GoldHi.G, GoldHi.B), 0.3),
                    new GradientStop(Color.FromArgb(0x00, GoldHi.R, GoldHi.G, GoldHi.B), 1),
                },
            }) };
        _root.Children.Add(_outline);
        _root.Children.Add(_ringLayer);
        _root.Children.Add(_tip);

        // 陣眼
        _hubTitle = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("SerifFont"),
            FontWeight = FontWeights.Black,
            FontSize = 16,
            Foreground = new SolidColorBrush(Gold),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = RadialGeometry.HubRadius * 1.7,
        };
        _hubCaption = new TextBlock
        {
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, Moon.R, Moon.G, Moon.B)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = RadialGeometry.HubRadius * 1.6,
        };
        _fileExt = new TextBlock { FontSize = 7.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xEE, 0xE2)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _fileIcon = FileIcon(_fileExt);
        _fileIcon.Visibility = Visibility.Collapsed;
        var hubText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hubText.Children.Add(_fileIcon);
        hubText.Children.Add(_hubTitle);
        hubText.Children.Add(_hubCaption);
        var hubFill = new RadialGradientBrush(Color.FromRgb(0x2B, 0x21, 0x17), Color.FromRgb(0x0E, 0x0C, 0x0A)) { GradientOrigin = new Point(0.5, 0.4) };
        _hubRing = new Ellipse { Margin = new Thickness(4), Stroke = new SolidColorBrush(Color.FromArgb(0x8C, Gold.R, Gold.G, Gold.B)), StrokeThickness = 1 };
        _hub = new Grid { Width = RadialGeometry.HubRadius * 2, Height = RadialGeometry.HubRadius * 2 };
        _hub.Children.Add(new Ellipse { Fill = hubFill });
        _hub.Children.Add(_hubRing);
        _hub.Children.Add(hubText);
        Canvas.SetLeft(_hub, _c.X - RadialGeometry.HubRadius);
        Canvas.SetTop(_hub, _c.Y - RadialGeometry.HubRadius);
        _root.Children.Add(_hub);

        _progress = new Path { Stroke = new SolidColorBrush(GoldHi), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false };
        _root.Children.Add(_progress);

        Content = _root;

        BuildRing(items.Select(i => (i.Label, i.Caption, i.Tone)).ToList());
        SetHubIdle("天幕", "移回中間取消");

        _dragAwayTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Input, (_, _) =>
        {
            _dragAwayTimer!.Stop();
            if (_stage == Stage.Menu) Finish(null); // 拖著檔案離開了法陣盤
        }, Dispatcher);
        _dragAwayTimer.Stop();

        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW, true);
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE, true);
        };
        Loaded += (_, _) => PlayWeave(null);
        Closed += (_, _) => { CompositionTarget.Rendering -= OnRendering; _dragAwayTimer.Stop(); };

        MouseMove += (_, e) => { if (_clickMode) Hover(e.GetPosition(_root)); };
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += (_, e) => { if (_clickMode) Click(e.GetPosition(_root)); };
        MouseRightButtonUp += (_, _) => Finish(null);
        LostMouseCapture += (_, _) =>
        {
            // 別的程式搶走了滑鼠（例如切換視窗）：當作取消；拖曳成品時會主動放開，不算
            if (_clickMode && !_done && !_suspendCapture) Finish(null);
        };

        DragEnter += (_, e) => { _dragAwayTimer.Stop(); _dragAwayTimer.Interval = TimeSpan.FromMilliseconds(450); HandleDragOver(e); };
        DragOver += (_, e) => HandleDragOver(e);
        DragLeave += (_, _) => { if (_stage == Stage.Menu) _dragAwayTimer.Start(); };
        Drop += OnDrop;
    }

    /// <summary>在游標附近顯示（螢幕 DIP）：整個圓留在工作區內，並避開天幕本體。</summary>
    public void ShowAt(Point cursorDip, Rect workAreaDip, Rect avoidDip)
    {
        double side = Side * _scale, r = (RadialGeometry.OuterRadius + 8) * _scale;
        var c = RadialGeometry.PlaceCenter(cursorDip, r, avoidDip, workAreaDip);
        Left = c.X - side / 2;
        Top = c.Y - side / 2;
        Show();
        if (PresentationSource.FromVisual(this) is HwndSource source) NativeMethods.ReassertTopmost(source.Handle);
    }

    // ═══════════════════════ 按住拖曳（天幕轉送滑鼠） ═══════════════════════

    /// <summary>
    /// 拖著檔案展開時：盤不在游標下，若檔案一直沒拖進盤裡（放到別處或取消），稍後自動收起。
    /// </summary>
    public void ExpectDrag()
    {
        _dragAwayTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _dragAwayTimer.Start();
    }

    /// <summary>天幕仍抓著滑鼠時，轉送游標位置（實際像素）。</summary>
    public void Track(Point screenPx) => Hover(FromScreen(screenPx));

    /// <summary>螢幕像素 → 盤面座標（已扣掉縮放）。</summary>
    private Point FromScreen(Point screenPx) => TranslatePoint(PointFromScreen(screenPx), _root);

    /// <summary>在天幕上放開滑鼠：落在扇區上就執行；剛展開還沒離開陣眼就改用點選；其餘取消。</summary>
    public void Release(Point screenPx)
    {
        var p = FromScreen(screenPx);
        int hit = RadialGeometry.HitTest(p, _c, _items.Count);
        if (hit >= 0)
        {
            Activate(hit);
            return;
        }
        // 還沒經過任何一格就放開（盤可能為了避開天幕而不在游標下）：改用點選
        if (!_leftHub)
        {
            EnterClickMode();
            return;
        }
        Finish(null);
    }

    // ═══════════════════════ 滑鼠 ═══════════════════════

    private bool _suspendCapture;

    private void EnterClickMode()
    {
        _clickMode = true;
        if (_stage == Stage.Menu) _hubCaption.Text = _hubIdleCaption = "點一格執行\n點這裡關閉";
        Mouse.Capture(this, CaptureMode.SubTree);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 出爐：在陣眼按下就開始拖成品
        if (_stage == Stage.Result && RadialGeometry.HitTest(e.GetPosition(_root), _c, Math.Max(1, _items.Count)) == -1)
        {
            e.Handled = true;
            DragOutputs();
        }
    }

    private void Click(Point p)
    {
        int hit = RadialGeometry.HitTest(p, _c, _items.Count, slack: 0);
        switch (_stage)
        {
            case Stage.Menu:
            case Stage.Formats:
                if (hit >= 0) Activate(hit);
                else Finish(null); // 點陣眼或盤外：關閉
                break;
            case Stage.Result:
            case Stage.Failed:
                if (hit != -1) Finish(null); // 陣眼以外的地方：收起（成品放回原檔旁）
                break;
        }
    }

    private void Hover(Point p)
    {
        if (_stage is not (Stage.Menu or Stage.Formats)) return;
        int hit = RadialGeometry.HitTest(p, _c, _items.Count);
        if (hit >= 0) _leftHub = true;
        SetHot(hit, dragTarget: false);
    }

    private void SetHot(int hit, bool dragTarget)
    {
        if (hit == _hot && !dragTarget) return;
        _hot = hit;
        for (int i = 0; i < _slices.Count; i++)
        {
            var s = _slices[i];
            bool hot = i == hit;
            bool target = hot && dragTarget;
            s.Shape.Fill = target ? FaceTarget : hot ? FaceHot : Face;
            s.Shape.Stroke = target ? CinnabarBrush : hot ? EdgeHot : Edge;
            // 選中的格子滑出去一點（短動畫，不是瞬間跳）
            Slide(s.Move, TranslateTransform.XProperty, hot ? Math.Cos(s.Mid) * HoverPush : 0);
            Slide(s.Move, TranslateTransform.YProperty, hot ? Math.Sin(s.Mid) * HoverPush : 0);
            s.Label.Foreground = hot ? GoldHiBrush : s.LabelBrush;
        }
        if (hit >= 0)
        {
            _hubTitle.Text = LabelOf(hit);
            _hubCaption.Text = CaptionOf(hit);
        }
        else
        {
            _hubTitle.Text = _hubIdleTitle;
            _hubCaption.Text = _hubIdleCaption;
        }
    }

    // ═══════════════════════ 拖著檔案 ═══════════════════════

    private void HandleDragOver(DragEventArgs e)
    {
        e.Handled = true;
        if (_stage != Stage.Menu || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        int hit = RadialGeometry.HitTest(e.GetPosition(_root), _c, _items.Count);
        bool accepts = hit >= 0 && (_items[hit].IsFurnace || _items[hit].OnFiles is not null);
        e.Effects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        SetHot(hit, dragTarget: accepts);
        if (hit >= 0 && !accepts) _hubCaption.Text = "這一格不收檔案";
        else if (hit >= 0 && _items[hit].IsFurnace) _hubCaption.Text = "放開即入爐";
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_stage != Stage.Menu || e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        int hit = RadialGeometry.HitTest(e.GetPosition(_root), _c, _items.Count);
        if (hit < 0) { Finish(null); return; } // 放在陣眼或盤外：取消
        var item = _items[hit];
        if (item.IsFurnace)
        {
            e.Effects = DragDropEffects.Copy; // 永遠回報「複製」，檔案總管就不會刪掉原檔
            Absorb(files, e.GetPosition(_root));
        }
        else if (item.OnFiles is { } onFiles)
        {
            e.Effects = DragDropEffects.Copy;
            Finish(null);
            onFiles(files);
        }
        else
        {
            Finish(null); // 這一格不收檔案
        }
    }

    // ═══════════════════════ 選中 ═══════════════════════

    private void Activate(int index)
    {
        if (_stage == Stage.Formats)
        {
            Convert(_options[index]);
            return;
        }

        var item = _items[index];
        if (item.IsFurnace)
        {
            // 沒拖檔案就點化形爐：先選檔案
            _suspendCapture = true;
            if (Mouse.Captured == this) ReleaseMouseCapture();
            var files = _ctx.PickFiles();
            _suspendCapture = false;
            if (files is { Count: > 0 }) Absorb(files, _c with { Y = _c.Y + RadialGeometry.OuterRadius + 20 });
            else Finish(null);
            return;
        }
        Finish(item);
    }

    // ═══════════════════════ 入爐化形 ═══════════════════════

    private void Absorb(IReadOnlyList<string> files, Point from)
    {
        if (_ctx.Furnace is not { } furnace) { Finish(null); return; }
        _sources = files;
        _options = furnace.OptionsFor(files);
        if (_options.Count == 0)
        {
            _ctx.Notify("化形爐認不得這些檔案", "目前能轉圖片、PDF、影音、字幕、文字檔與 ZIP");
            Finish(null);
            return;
        }

        _stage = Stage.Absorbing;
        _fileFrom = from;
        int furnaceIndex = _items.ToList().FindIndex(i => i.IsFurnace);
        SetHot(-1, dragTarget: false);
        foreach (var s in _slices) StopSlide(s.Move);   // 入爐由每一幀直接擺位置
        ShowFile(files[0]);
        _hubTitle.Text = "化形爐";
        _hubCaption.Text = "煉化中";

        Animate(RadialGeometry.AbsorbMs + RadialGeometry.AbsorbPauseMs, ms =>
        {
            double p = RadialGeometry.AbsorbProgress(ms);
            for (int i = 0; i < _slices.Count; i++)
            {
                var s = _slices[i];
                if (i == furnaceIndex)
                {
                    var (scale, offset) = RadialGeometry.FurnaceAbsorb(p);
                    s.Scale.ScaleX = s.Scale.ScaleY = scale;
                    s.Move.X = Math.Cos(s.Mid) * offset;
                    s.Move.Y = Math.Sin(s.Mid) * offset;
                    s.Group.Opacity = 1 - p * 0.6;
                }
                else
                {
                    s.Scale.ScaleX = s.Scale.ScaleY = 1 - 0.6 * p;
                    s.Move.X = s.Move.Y = 0;
                    s.Group.Opacity = 1 - p;
                }
            }
            _outline.Opacity = 0.9 * (1 - p);
            _hubRing.Stroke = p > 0.5 ? CinnabarBrush : HubRingBrush;
            // 檔案圖示從放下的位置飛進陣眼
            var t = new TranslateTransform((_fileFrom.X - _c.X) * (1 - p), (_fileFrom.Y - _c.Y) * (1 - p) - 28 * (1 - p));
            _fileIcon.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.3 - 0.3 * p, 1.3 - 0.3 * p), t } };
        }, () =>
        {
            _fileIcon.RenderTransform = Transform.Identity;
            ShowFormats();
        });
    }

    private void ShowFormats()
    {
        _stage = Stage.Formats;
        _items = _options.Select(o => new RadialItem(o.Label, o.Caption, () => { }, ToneFromKey(o.ToneKey))).ToList();
        BuildRing(_options.Select(o => (o.Label, o.Caption, ToneFromKey(o.ToneKey))).ToList());
        string name = IOPath.GetFileName(_sources[0]);
        SetHubIdle(name, _sources.Count > 1 ? $"等 {_sources.Count} 個 · 選擇格式" : "選擇格式");
        _hubRing.Stroke = HubRingBrush;
        if (!_clickMode) EnterClickMode();
        PlayWeave(null);
    }

    // ═══════════════════════ 煉化與出爐 ═══════════════════════

    private async void Convert(FurnaceOption option)
    {
        if (_ctx.Furnace is not { } furnace || _stage != Stage.Formats) return;
        _stage = Stage.Converting;
        SetHot(-1, dragTarget: false);
        _hubTitle.Text = "0%";
        _hubCaption.Text = option.Label;
        _cts = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            DrawProgress(p);
            _hubTitle.Text = $"{(int)Math.Round(p * 100)}%";
        });

        var result = await furnace.ConvertAsync(_sources, option.Id, progress, _cts.Token);
        if (_done) return;

        if (!result.Succeeded)
        {
            _stage = Stage.Failed;
            _progress.Data = null;
            _hubTitle.Text = "煉化失敗";
            _hubCaption.Text = "點一下關閉";
            _ctx.Notify("化形爐煉化失敗", result.Error ?? "沒有產生檔案");
            return;
        }

        _outputs = result.Outputs;
        _stage = Stage.Result;
        DrawProgress(1);
        ShowFile(_outputs[0]);
        _hubTitle.Text = _outputs.Count > 1 ? $"{_outputs.Count} 個檔案" : "已煉成";
        _hubCaption.Text = "拖到想放的地方";
        foreach (var s in _slices) s.Group.Opacity = 0.35; // 格式退到背景，焦點留在成品
        Cursor = Cursors.Hand;
    }

    /// <summary>把成品拖出去（桌面、資料夾、聊天視窗都行）。</summary>
    private void DragOutputs()
    {
        var existing = _outputs.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length == 0) return;

        _suspendCapture = true;
        if (Mouse.Captured == this) ReleaseMouseCapture();
        var data = new DataObject(DataFormats.FileDrop, existing);
        var effect = DragDrop.DoDragDrop(_hub, data, DragDropEffects.Copy | DragDropEffects.Move);
        _suspendCapture = false;

        if (effect != DragDropEffects.None)
        {
            _outputsTaken = true;
            Finish(null);
        }
        else if (!_done)
        {
            Mouse.Capture(this, CaptureMode.SubTree);
        }
    }

    // ═══════════════════════ 結束 ═══════════════════════

    private void Finish(RadialItem? chosen)
    {
        if (_done) return;
        _done = true;
        _cts?.Cancel();
        if (Mouse.Captured == this) ReleaseMouseCapture();

        // 成品沒被拖走：放回原檔旁邊，不會不見
        if (_stage == Stage.Result && !_outputsTaken && _ctx.Furnace is { } furnace)
        {
            var placed = furnace.PlaceBesideSources(_sources, _outputs);
            if (placed.Count > 0)
                _ctx.Notify("成品已放在原檔旁", placed.Count > 1 ? $"{IOPath.GetFileName(placed[0])} 等 {placed.Count} 個" : IOPath.GetFileName(placed[0]));
        }

        Close();
        if (chosen is null) return;
        try { chosen.Invoke(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[法陣盤] {chosen.Label} 執行失敗：{ex.Message}"); }
    }

    // ═══════════════════════ 盤面 ═══════════════════════

    private void BuildRing(IReadOnlyList<(string Label, string Caption, Color? Tone)> entries)
    {
        _ringLayer.Children.Clear();
        _slices.Clear();
        _hot = -1;
        int n = entries.Count;
        for (int i = 0; i < n; i++)
        {
            var (a0, a1, mid) = RadialGeometry.SliceAngles(i, n);
            var shape = new Path { Fill = Face, Stroke = Edge, StrokeThickness = 1 };
            bool latin = entries[i].Label.All(ch => ch < 0x2E80);
            var label = new TextBlock
            {
                Text = entries[i].Label,
                FontFamily = latin ? (FontFamily)Application.Current.FindResource("SansFont") : (FontFamily)Application.Current.FindResource("SerifFont"),
                FontWeight = latin ? FontWeights.SemiBold : FontWeights.Bold,
                FontSize = latin ? 14 : 15,
                Foreground = Frozen(entries[i].Tone ?? Moon),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var caption = new TextBlock
            {
                Text = entries[i].Caption,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0x61, Moon.R, Moon.G, Moon.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 74,
            };
            var labels = new StackPanel { IsHitTestVisible = false };
            labels.Children.Add(label);
            labels.Children.Add(caption);
            labels.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lr = (RadialGeometry.HubRadius + RadialGeometry.OuterRadius) / 2 + 4;
            Canvas.SetLeft(labels, _c.X + lr * Math.Cos(mid) - labels.DesiredSize.Width / 2);
            Canvas.SetTop(labels, _c.Y + lr * Math.Sin(mid) - labels.DesiredSize.Height / 2);

            var scale = new ScaleTransform(1, 1, _c.X, _c.Y);
            var move = new TranslateTransform();
            var group = new Canvas { Width = Side, Height = Side, RenderTransform = new TransformGroup { Children = { scale, move } } };
            group.Children.Add(shape);
            group.Children.Add(labels);
            _ringLayer.Children.Add(group);
            _slices.Add(new SliceVisual(group, shape, labels, label, a0, a1, mid, scale, move, label.Foreground));
        }
        _outline.Data = CircleFrom(RadialGeometry.StartAngle(n), RadialGeometry.OuterRadius + 6);
        _outline.Opacity = 0.9;
    }

    private void PlayWeave(Action? done) =>
        Animate(RadialGeometry.OpenDuration.TotalMilliseconds, WeaveFrame, done);

    /// <summary>金線輪轉的一幀：扇區只畫到金線已經掃過的角度，並從陣眼往外長出。</summary>
    private void WeaveFrame(double ms)
    {
        int n = _slices.Count;
        double ringR = RadialGeometry.OuterRadius + 6, len = 2 * Math.PI * ringR;
        double f = RadialGeometry.OutlineProgress(ms);
        _outline.StrokeDashArray = new DoubleCollection { len * f / 1.2, len };
        _outline.Opacity = ms > RadialGeometry.SweepMs + 300 ? Math.Max(0.3, 0.9 - (ms - RadialGeometry.SweepMs - 300) / 700) : 0.9;
        double sweep = RadialGeometry.SweepAngle(ms, n);
        Canvas.SetLeft(_tip, _c.X + ringR * Math.Cos(sweep) - _tip.Width / 2);
        Canvas.SetTop(_tip, _c.Y + ringR * Math.Sin(sweep) - _tip.Height / 2);
        _tip.Opacity = f is > 0 and < 1 ? 1 : 0;
        _hub.Opacity = RadialGeometry.HubOpacity(ms);

        double r1 = RadialGeometry.HubRadius + 6;
        for (int i = 0; i < n; i++)
        {
            var s = _slices[i];
            var (edge, grow, text) = RadialGeometry.Slice(ms, i, n);
            double r2 = r1 + (RadialGeometry.OuterRadius - r1) * Math.Min(grow, 1.06);
            s.Shape.Data = edge > s.A0 + 0.0005 ? RadialGeometry.SlicePath(_c, s.A0, edge, r1, r2) : null;
            s.Labels.Opacity = text;
            s.Group.Opacity = 1;
            s.Scale.ScaleX = s.Scale.ScaleY = 1;
        }
    }

    private void Animate(double lengthMs, Action<double> frame, Action? done)
    {
        CompositionTarget.Rendering -= OnRendering;
        if (!_animate)
        {
            frame(double.MaxValue / 4);
            done?.Invoke();
            return;
        }
        _animFrame = frame;
        _animLength = lengthMs;
        _animDone = done;
        _animStart = TimeSpan.Zero;
        frame(0);
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_animStart == TimeSpan.Zero) _animStart = now;
        double ms = (now - _animStart).TotalMilliseconds;
        _animFrame?.Invoke(Math.Min(ms, _animLength));
        if (ms < _animLength) return;
        CompositionTarget.Rendering -= OnRendering;
        var done = _animDone;
        _animDone = null;
        done?.Invoke();
    }

    private void DrawProgress(double p)
    {
        double r = RadialGeometry.HubRadius + 2;
        p = Math.Clamp(p, 0, 1);
        if (p <= 0) { _progress.Data = null; return; }
        if (p >= 0.9999) { _progress.Data = new EllipseGeometry(_c, r, r); return; }
        double a0 = -Math.PI / 2, a1 = a0 + p * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(_c.X + r * Math.Cos(a0), _c.Y + r * Math.Sin(a0)), false, false);
            ctx.ArcTo(new Point(_c.X + r * Math.Cos(a1), _c.Y + r * Math.Sin(a1)), new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        _progress.Data = g;
    }

    private void SetHubIdle(string title, string caption)
    {
        _hubIdleTitle = title;
        _hubIdleCaption = caption;
        _hubTitle.Text = title;
        _hubCaption.Text = caption;
    }

    private void ShowFile(string path)
    {
        string ext = IOPath.GetExtension(path).TrimStart('.').ToUpperInvariant();
        _fileExt.Text = ext.Length is > 0 and <= 4 ? ext : Directory.Exists(path) ? "夾" : "檔";
        _fileIcon.Visibility = Visibility.Visible;
    }

    private string LabelOf(int i) => _stage == Stage.Formats ? _options[i].Label : _items[i].Label;
    private string CaptionOf(int i) => _stage == Stage.Formats ? _options[i].Caption : _items[i].Caption;
    private Color? ToneOf(int i) => _items[i].Tone;

    private static Color? ToneFromKey(string? key) => key switch
    {
        "ZhuShaBrush" => Cinnabar,
        "TianQingBrush" => Color.FromRgb(0xBF, 0xD8, 0xD3),
        _ => null,
    };

    /// <summary>一張摺角的紙，下方標著副檔名（入爐與出爐時顯示在陣眼）。</summary>
    private static Grid FileIcon(TextBlock ext)
    {
        var paper = new Path
        {
            Data = Geometry.Parse("M0,0 H14 L20,6 V26 H0 Z M14,0 V6 H20"),
            Fill = new SolidColorBrush(Moon),
            Stroke = new SolidColorBrush(Gold),
            StrokeThickness = 1,
        };
        var tag = new Border { Width = 16, Height = 9, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x41, 0x2F)),
            Margin = new Thickness(2, 13, 2, 4), Child = ext, HorizontalAlignment = HorizontalAlignment.Center };
        var grid = new Grid { Width = 20, Height = 26, Margin = new Thickness(0, 0, 0, 3), HorizontalAlignment = HorizontalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
        grid.Children.Add(paper);
        grid.Children.Add(tag);
        return grid;
    }

    /// <summary>從某個角度開始順時針的一整圈（讓金線從第一格的左緣開始畫）。</summary>
    /// <summary>位移的短動畫：從目前位置滑到目標，結束後停在目標值（基底值已先設好）。</summary>
    private static void Slide(TranslateTransform t, DependencyProperty p, double to)
    {
        double from = (double)t.GetValue(p);
        if (Math.Abs(from - to) < 0.01) return;
        t.SetValue(p, to);
        t.BeginAnimation(p, new DoubleAnimation(from, to, HoverTime) { EasingFunction = HoverEase, FillBehavior = FillBehavior.Stop });
    }

    private static void StopSlide(TranslateTransform t)
    {
        t.BeginAnimation(TranslateTransform.XProperty, null);
        t.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private Geometry CircleFrom(double angle, double r)
    {
        var start = new Point(_c.X + r * Math.Cos(angle), _c.Y + r * Math.Sin(angle));
        var half = new Point(_c.X - r * Math.Cos(angle), _c.Y - r * Math.Sin(angle));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(half, new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.ArcTo(start, new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
