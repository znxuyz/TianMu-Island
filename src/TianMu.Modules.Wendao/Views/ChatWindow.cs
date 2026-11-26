using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TianMu.Modules.Wendao.Ai;
using TianMu.Modules.Wendao.Store;
using TianMu.Modules.Wendao.Web;

namespace TianMu.Modules.Wendao.Views;

/// <summary>
/// 問道的對話視窗。
///   · API 模式：用各家免費金鑰，天幕島自己轉接；可附圖片與檔案（PDF、Word、Excel、PowerPoint、文字與程式碼）、
///     快捷指令、語音輸入、朗讀、重新回答、同時比較三家、匯出對話
///   · 網頁模式：直接嵌入各家官網，用你自己的帳號
/// </summary>
internal sealed class ChatWindow : Window
{
    private const int MaxContextChars = 60_000;
    private const string Compare = "compare";
    private const string SystemPrompt =
        "你是天幕島裡的助理「問道」。預設用繁體中文（台灣用語）回答；如果使用者用其他語言發問，就用那個語言。回答要清楚、直接，必要時用標題、條列或程式碼區塊。";

    /// <summary>快捷指令：點一下就把指令放在問題前面。</summary>
    private static readonly (string Label, string Prompt)[] QuickPrompts =
    [
        ("翻成英文", "請把下面的內容翻譯成自然的英文："),
        ("翻成中文", "請把下面的內容翻譯成通順的繁體中文（台灣用語）："),
        ("摘要重點", "請用條列整理下面內容的重點："),
        ("潤稿", "請潤飾下面的文字，讓它更通順、專業，並列出你改了哪些地方："),
        ("寫成 Email", "請把下面的內容寫成一封得體的 Email："),
        ("解釋程式", "請逐段解釋下面這段程式在做什麼，並指出可能的問題："),
        ("找錯字", "請找出下面文字的錯字與文法問題，並給出修正後的版本："),
    ];

    private readonly Conductor _conductor;
    private readonly ImageGenerator _painter;
    private readonly ComboBox _aspect = new() { Width = 120, FontSize = 12.5, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "生成圖片的比例" };
    private readonly History _history;
    private readonly List<Conversation> _conversations;
    private Conversation _current = new();
    private readonly Dictionary<SavedMessage, byte[]> _images = [];

    private readonly ListBox _list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Look.Moon, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _model = new() { MinWidth = 280, FontSize = 13, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _route = new() { Foreground = Look.Tertiary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _messages = new() { Margin = new Thickness(24, 16, 24, 16) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _input = Look.Input(14.5);
    private readonly Button _send, _stop;
    private readonly WrapPanel _attachChips = new() { Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    private readonly List<Attachment> _attachments = [];
    private CancellationTokenSource? _cts;
    private System.Media.SoundPlayer? _player;

    // 兩種模式：API（各家免費金鑰，天幕島自己轉接）／網頁（嵌入各家官網，用自己的帳號）
    private readonly string? _dataDir;
    private readonly DockPanel _apiPane = new();
    private readonly List<UIElement> _apiOnly = [];
    private readonly Button _modeApi, _modeWeb;
    private WebPane? _web;
    private readonly Grid _rightHost = new();

    public ChatWindow(Conductor conductor, ImageGenerator painter, string? dataDir)
    {
        _conductor = conductor;
        _painter = painter;
        _dataDir = dataDir;
        _history = new History(dataDir);
        _conversations = _history.LoadAll();
        Title = "問道";
        Width = 1080;
        Height = 760;
        MinWidth = 680;
        MinHeight = 460;
        Background = Look.Lacquer;
        FontFamily = Look.Sans;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowActivated = true;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // ── 左：模式、對話紀錄 ──
        var left = new DockPanel { Background = Look.Panel };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 16, 16, 10) };
        brand.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(5), Background = Look.Cinnabar, Child = new TextBlock { Text = "問", FontFamily = Look.Serif, FontWeight = FontWeights.Bold, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        brand.Children.Add(new TextBlock { Text = "問道", FontFamily = Look.Serif, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Look.Gold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brand, Dock.Top);
        left.Children.Add(brand);
        var modes = new UniformGrid { Columns = 2, Margin = new Thickness(14, 0, 14, 12) };
        _modeApi = Look.Button("API 模式", () => SetMode(web: false), tip: "用各家的免費金鑰，天幕島自動挑模型、額度用完換下一家");
        _modeWeb = Look.Button("網頁模式", () => SetMode(web: true), tip: "直接開各家 AI 的官方網站，用你自己的帳號登入，不需要金鑰");
        _modeApi.Margin = new Thickness(0, 0, 3, 0);
        _modeWeb.Margin = new Thickness(3, 0, 0, 0);
        modes.Children.Add(_modeApi);
        modes.Children.Add(_modeWeb);
        DockPanel.SetDock(modes, Dock.Top);
        left.Children.Add(modes);
        var newChat = Look.Button("＋ 新的對話", NewConversation, primary: true);
        newChat.Margin = new Thickness(14, 0, 14, 10);
        DockPanel.SetDock(newChat, Dock.Top);
        left.Children.Add(newChat);
        var connect = Look.Button("連線設定", OpenConnections, tip: "設定各家的免費金鑰");
        connect.Margin = new Thickness(14, 10, 14, 14);
        DockPanel.SetDock(connect, Dock.Bottom);
        left.Children.Add(connect);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is ListBoxItem { Tag: Conversation c } && c != _current) Show(c); };
        left.Children.Add(_list);
        _apiOnly.AddRange([newChat, connect, _list]);
        root.Children.Add(left);

        // ── 右：模型列＋訊息＋輸入 ──
        Grid.SetColumn(_rightHost, 1);
        _rightHost.Children.Add(_apiPane);
        var top = new DockPanel { Margin = new Thickness(20, 12, 20, 10) };
        var export = Look.Button("匯出", ExportConversation, tip: "把這段對話存成 Markdown 檔");
        export.Padding = new Thickness(10, 3, 10, 3);
        DockPanel.SetDock(export, Dock.Right);
        top.Children.Add(export);
        foreach (var (label, tag) in new[] { ("圖片 方形 1:1", "1:1"), ("圖片 橫式 16:9", "16:9"), ("圖片 直式 9:16", "9:16"), ("圖片 4:3", "4:3") })
            _aspect.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        _aspect.SelectedIndex = 0;
        _aspect.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(_aspect, Dock.Right);
        top.Children.Add(_aspect);
        top.Children.Add(new TextBlock { Text = "模型", Foreground = Look.Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        top.Children.Add(_model);
        top.Children.Add(_route);
        var topBorder = new Border { Child = top, BorderBrush = Look.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(topBorder, Dock.Top);
        _apiPane.Children.Add(topBorder);

        var composer = new DockPanel { Margin = new Thickness(20, 8, 20, 16) };
        _send = Look.Button("送出", Send, primary: true, tip: "送出（Enter）");
        _stop = Look.Button("停止", () => _cts?.Cancel(), tip: "停止回答");
        _stop.Visibility = Visibility.Collapsed;
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 0) };
        actions.Children.Add(Look.Button("附件", PickFiles, tip: "附圖片或檔案：PDF、Word、Excel、PowerPoint、文字、程式碼（也可以直接拖進來或貼上圖片）"));
        actions.Children.Add(new Border { Height = 6 });
        actions.Children.Add(Look.Button("語音", Dictate, tip: "用說的輸入（Windows 語音輸入，Win+H）"));
        actions.Children.Add(new Border { Height = 6 });
        actions.Children.Add(Look.Button("畫圖", () => _ = DrawAsync(), tip: "把輸入框的描述畫成圖片（免費：Pollinations → Cloudflare → Gemini；比例在右上角選）"));
        actions.Children.Add(new Border { Height = 6 });
        actions.Children.Add(_send);
        actions.Children.Add(_stop);
        DockPanel.SetDock(actions, Dock.Right);
        composer.Children.Add(actions);

        var inputStack = new StackPanel();
        var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (label, prompt) in QuickPrompts)
        {
            var chip = Look.Button(label, () => ApplyQuick(prompt), tip: prompt);
            chip.Padding = new Thickness(9, 2, 9, 2);
            chip.FontSize = 12;
            chip.Margin = new Thickness(0, 0, 6, 4);
            quick.Children.Add(chip);
        }
        inputStack.Children.Add(quick);
        inputStack.Children.Add(_attachChips);
        _input.AcceptsReturn = true;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.MinHeight = 64;
        _input.MaxHeight = 220;
        _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _input.PreviewKeyDown += InputKey;
        DataObject.AddPastingHandler(_input, OnPaste);
        inputStack.Children.Add(_input);
        inputStack.Children.Add(new TextBlock { Text = "Enter 送出 · Shift+Enter 換行 · 免費模型有每日次數限制，用完會自動換下一家", Foreground = Look.Tertiary, FontSize = 11.5, Margin = new Thickness(2, 6, 0, 0) });
        composer.Children.Add(inputStack);
        DockPanel.SetDock(composer, Dock.Bottom);
        _apiPane.Children.Add(composer);

        _scroll.Content = _messages;
        _apiPane.Children.Add(_scroll);
        root.Children.Add(_rightHost);
        Content = root;

        AllowDrop = true;
        Drop += OnDrop;
        Loaded += async (_, _) =>
        {
            RefreshList();
            ShowEmpty();
            SetMode(web: ReadMode() == "web");
            _input.Focus();
            await ReloadModelsAsync();
        };
        Closed += (_, _) => { _cts?.Cancel(); _player?.Stop(); };
    }

    /// <summary>把視窗叫到最前面（從天幕、系統匣開啟時，Windows 常會把新視窗壓在後面只閃工作列）。</summary>
    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;      // 暫時置頂一下，確保真的跳到最前面
        Topmost = false;
        Focus();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { Activate(); _input.Focus(); });
    }

    // ═══════════════════════ 模式 ═══════════════════════

    private void SetMode(bool web)
    {
        if (web && _web is null)
        {
            _web = new WebPane(_dataDir);
            _rightHost.Children.Add(_web);
        }
        if (web && _web is not null && _input.Text.Trim().Length > 0 && _web.Draft.Trim().Length == 0) _web.Draft = _input.Text;   // 打到一半的問題帶過去
        _apiPane.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        if (_web is not null) _web.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
        foreach (var e in _apiOnly) e.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        _modeApi.Background = web ? Brushes.Transparent : Look.Gold;
        _modeApi.Foreground = web ? Look.Moon : Look.Lacquer;
        _modeWeb.Background = web ? Look.Gold : Brushes.Transparent;
        _modeWeb.Foreground = web ? Look.Lacquer : Look.Moon;
        SaveMode(web ? "web" : "api");
    }

    private string ModeFile => Path.Combine(_dataDir ?? Path.GetTempPath(), "mode.txt");

    private string ReadMode()
    {
        try { return _dataDir is not null && File.Exists(ModeFile) ? File.ReadAllText(ModeFile).Trim() : "api"; }
        catch (IOException) { return "api"; }
    }

    private void SaveMode(string mode)
    {
        if (_dataDir is null) return;
        try { Directory.CreateDirectory(_dataDir); File.WriteAllText(ModeFile, mode); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ═══════════════════════ 模型 ═══════════════════════

    private async Task ReloadModelsAsync()
    {
        _route.Text = "正在取得模型清單…";
        try { await _conductor.RefreshAsync(CancellationToken.None); } catch (Exception) { }
        FillModels();
    }

    private void FillModels()
    {
        string? keep = (_model.SelectedItem as ComboBoxItem)?.Tag as string;
        _model.Items.Clear();
        _model.Items.Add(new ComboBoxItem { Content = "自動（依問題挑選，額度用完自動換）", Tag = Conductor.Auto });
        _model.Items.Add(new ComboBoxItem { Content = "同時比較三家（各家一起回答）", Tag = Compare });
        foreach (var group in _conductor.Models.GroupBy(m => m.ProviderId))
        {
            var p = Provider.Find(group.Key)!;
            _model.Items.Add(new ComboBoxItem { Content = $"── {p.Name} ──", IsEnabled = false, FontWeight = FontWeights.SemiBold });
            foreach (var m in group)
                _model.Items.Add(new ComboBoxItem { Content = "　" + m.Name + (m.Vision ? "　🖼" : ""), Tag = m.Key, ToolTip = m.Id });
        }
        _model.SelectedItem = _model.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == keep) ?? _model.Items[0];
        // 分組標題不能選：滾輪或方向鍵停到標題時，改選它下面第一個模型
        _model.SelectionChanged -= SkipHeader;
        _model.SelectionChanged += SkipHeader;
        int count = _conductor.Models.Count;
        _route.Text = count == 0 ? "還沒有可用的模型 — 按左下角「連線設定」貼上免費金鑰，或改用「網頁模式」" : $"共 {count} 個免費模型可用";
    }

    private void SkipHeader(object sender, SelectionChangedEventArgs e)
    {
        if (_model.SelectedItem is ComboBoxItem { Tag: null })
        {
            int i = _model.SelectedIndex;
            _model.SelectedIndex = i + 1 < _model.Items.Count ? i + 1 : 0;
        }
    }

    private string Choice => (_model.SelectedItem as ComboBoxItem)?.Tag as string ?? Conductor.Auto;

    private void OpenConnections()
    {
        new ConnectionWindow(_conductor, this).ShowDialog();
        FillModels();
    }

    // ═══════════════════════ 對話紀錄 ═══════════════════════

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var c in _conversations)
        {
            var item = new ListBoxItem
            {
                Tag = c, Padding = new Thickness(14, 8, 14, 8), Foreground = Look.Moon, Cursor = Cursors.Hand,
                Content = new StackPanel { Children =
                {
                    new TextBlock { Text = c.Title, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = c.Updated.ToString("M/d HH:mm"), FontSize = 11, Foreground = Look.Tertiary },
                } },
            };
            var menu = new ContextMenu();
            var del = new MenuItem { Header = "刪除這段對話" };
            var captured = c;
            del.Click += (_, _) => DeleteConversation(captured);
            menu.Items.Add(del);
            item.ContextMenu = menu;
            _list.Items.Add(item);
            if (c == _current) _list.SelectedItem = item;
        }
    }

    private void NewConversation()
    {
        _cts?.Cancel();
        _current = new Conversation();
        _list.SelectedItem = null;
        ShowEmpty();
        _input.Focus();
    }

    private void DeleteConversation(Conversation c)
    {
        _history.Delete(c);
        _conversations.Remove(c);
        if (c == _current) NewConversation();
        RefreshList();
    }

    private void Show(Conversation c)
    {
        _cts?.Cancel();
        _current = c;
        _messages.Children.Clear();
        foreach (var m in c.Messages)
        {
            byte[]? picture = _images.GetValueOrDefault(m);
            if (picture is null && m.ImagePath is { } path && File.Exists(path)) { try { picture = File.ReadAllBytes(path); } catch (IOException) { } }
            var view = new MessageView(m.Role, m.Text, m.Role == Role.Assistant ? m.Model : null, picture, m.HadImage, m.Files, largeImage: m.ImagePath is not null);
            if (m.Role == Role.Assistant && m.ImagePath is not null && picture is not null) AttachImageActions(view, m, picture);
            else if (m.Role == Role.Assistant && !m.ImageOnly) AttachActions(view, m);
            _messages.Children.Add(view);
        }
        _scroll.ScrollToEnd();
    }

    private void ShowEmpty()
    {
        _messages.Children.Clear();
        var hello = new StackPanel { Margin = new Thickness(0, 60, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        hello.Children.Add(new TextBlock { Text = "有什麼想問的？", FontFamily = Look.Serif, FontSize = 26, Foreground = Look.Gold, HorizontalAlignment = HorizontalAlignment.Center });
        hello.Children.Add(new TextBlock
        {
            Text = "選「自動」時，問道會看問題性質挑模型：\n寫程式、推理分析找大模型；看圖、讀長文件找 Gemini；快問快答找最快的。\n" +
                   "可以附 PDF、Word、Excel、PowerPoint、圖片；選「同時比較三家」一次看三個答案。",
            Foreground = Look.Secondary, FontSize = 13.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 0), LineHeight = 22,
        });
        _messages.Children.Add(hello);
    }

    // ═══════════════════════ 送出 ═══════════════════════

    private void InputKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            Send();
        }
    }

    private void ApplyQuick(string prompt)
    {
        string body = _input.Text.Trim();
        _input.Text = prompt + "\n" + body;
        _input.CaretIndex = _input.Text.Length;
        _input.Focus();
    }

    private void Send()
    {
        string question = _input.Text.Trim();
        if ((question.Length == 0 && _attachments.Count == 0) || _cts is not null) return;
        // 「幫我畫…」「生成一張…的圖」：直接畫圖，不當成一般問答
        if (_attachments.Count == 0 && Router.WantsImage(question)) { _ = DrawAsync(); return; }
        var image = _attachments.FirstOrDefault(a => a.Kind == AttachmentKind.Image);
        var files = _attachments.ToList();
        if (question.Length == 0) question = image is not null ? "請描述這張圖片。" : "請整理這份檔案的重點。";

        if (_current.Messages.Count == 0)
        {
            _messages.Children.Clear();
            _current.Title = Conversation.TitleFrom(question);
            _conversations.Insert(0, _current);
        }
        string fileText = FileReader.Compose("", files);
        var userMsg = new SavedMessage
        {
            Role = Role.User, Text = question, HadImage = image is not null,
            Files = files.Count > 0 ? files.Select(f => f.Name).ToList() : null,
            FileText = fileText.Length > 0 ? fileText : null,
        };
        if (image?.Bytes is { } bytes) _images[userMsg] = bytes;
        _current.Messages.Add(userMsg);
        _messages.Children.Add(new MessageView(Role.User, question, null, image?.Bytes, false, userMsg.Files));
        ClearAttachments();
        _input.Clear();
        _ = AnswerAsync(image?.Bytes, image?.Mime ?? "image/png", exclude: null);
    }

    /// <summary>針對最後一個問題產生回答（送出、重新回答都走這裡）。</summary>
    private async Task AnswerAsync(byte[]? image, string mime, string? exclude)
    {
        var last = _current.Messages.LastOrDefault(m => m.Role == Role.User);
        if (last is null) return;
        var context = BuildContext(_current.Messages, image, mime);
        int historyChars = context.Sum(m => m.Text.Length) - last.Prompt.Length;
        var kind = Router.Classify(last.Prompt, image is not null, historyChars);
        bool compare = Choice == Compare;
        var all = _conductor.Candidates(compare ? Conductor.Auto : Choice, kind).Where(m => m.Key != exclude).ToList();

        _cts = new CancellationTokenSource();
        _send.Visibility = Visibility.Collapsed;
        _stop.Visibility = Visibility.Visible;
        try
        {
            if (compare)
            {
                // 每家挑一個，最多三家同時回答；每一家失敗時在自己的候選裡換
                var lanes = all.GroupBy(m => m.ProviderId).Take(3).Select(g => g.ToList()).ToList();
                if (lanes.Count == 0) lanes.Add([]);
                _route.Text = $"判斷為「{Router.Label(kind)}」→ 同時問 {lanes.Count} 家";
                var tasks = lanes.Select((lane, i) => AnswerOneAsync(context, lane, kind, alternate: i > 0, _cts.Token)).ToList();
                await Task.WhenAll(tasks);
            }
            else await AnswerOneAsync(context, all, kind, alternate: false, _cts.Token);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _send.Visibility = Visibility.Visible;
            _stop.Visibility = Visibility.Collapsed;
        }

        _current.Updated = DateTime.Now;
        if (_current.Messages.Count == 0) _conversations.Remove(_current);
        _history.Save(_current);
        RefreshList();
        _scroll.ScrollToEnd();
        _input.Focus();
    }

    private async Task AnswerOneAsync(List<ChatMessage> context, IReadOnlyList<ModelInfo> candidates, TaskKind kind, bool alternate, CancellationToken ct)
    {
        var reply = new MessageView(Role.Assistant, "", "思考中…");
        _messages.Children.Add(reply);
        _scroll.ScrollToEnd();
        var answer = new SavedMessage { Role = Role.Assistant, Alternate = alternate };
        bool keep = true;
        try
        {
            var used = await _conductor.AskAsync(context, candidates,
                (m, failed) => Dispatcher.Invoke(() =>
                {
                    string label = Label(m);
                    reply.SetHeader(failed is null ? label : $"{label}（前一個失敗，已改用這個）", warn: failed is not null);
                    if (Choice != Compare) _route.Text = Choice == Conductor.Auto ? $"判斷為「{Router.Label(kind)}」→ {label}" : label;
                }),
                piece => Dispatcher.InvokeAsync(() => { reply.Append(piece); if (IsNearBottom()) _scroll.ScrollToEnd(); }),
                ct);
            await Dispatcher.InvokeAsync(() => { });   // 等最後一段字畫上去
            answer.Model = Label(used);
            answer.Text = reply.Text;
            reply.SetText(reply.Text);
        }
        catch (OperationCanceledException)
        {
            answer.Text = reply.Text + (reply.Text.Length > 0 ? "\n\n（已停止）" : "（已停止）");
            answer.Model = "已停止";
            reply.SetText(answer.Text);
        }
        catch (ProviderException e)
        {
            reply.SetHeader("沒有回答", warn: true);
            reply.SetText(e.Message);
            keep = false;
        }
        catch (Exception e)
        {
            // 網路中斷之類的意外：照實顯示，不讓天幕島整個當掉
            reply.SetHeader("連線中斷", warn: true);
            if (reply.Text.Length == 0) { reply.SetText(e.Message); keep = false; }
            else { answer.Text = reply.Text + "\n\n（連線中斷）"; answer.Model = "連線中斷"; reply.SetText(answer.Text); }
        }

        if (keep && answer.Text.Length > 0)
        {
            _current.Messages.Add(answer);
            AttachActions(reply, answer);
        }
        else if (!alternate && Choice != Compare && _current.Messages.Count > 0 && _current.Messages[^1].Role == Role.User && _input.Text.Length == 0)
        {
            // 完全沒有回答：問題放回輸入框，修好連線再按一次
            _input.Text = _current.Messages[^1].Text;
            _current.Messages.RemoveAt(_current.Messages.Count - 1);
        }
    }

    private static string Label(ModelInfo m) => $"{ModelCatalog.Pretty(m.Id)} · {Provider.Find(m.ProviderId)?.Name}";

    /// <summary>回答下方的按鈕：複製、朗讀、重新回答（只有最後一則）。</summary>
    private void AttachActions(MessageView view, SavedMessage message)
    {
        view.SetActions(
            ("複製", "複製這則回答", () => { try { Clipboard.SetText(message.Text); } catch (Exception) { } }),
            ("朗讀", "用 Windows 的語音唸出來（再按一次停止）", () => _ = SpeakAsync(message.Text)),
            ("重新回答", "換一個模型重新回答最後一個問題", () => Regenerate(message)));
    }

    private void Regenerate(SavedMessage message)
    {
        if (_cts is not null) return;
        int lastUser = _current.Messages.FindLastIndex(m => m.Role == Role.User);
        if (_current.Messages.IndexOf(message) < lastUser) { _route.Text = "只能重新回答最後一個問題"; return; }
        // 拿掉最後一個問題之後的所有回答，換一個模型再答一次
        string? exclude = _conductor.Models.FirstOrDefault(m => Label(m) == message.Model)?.Key;
        _current.Messages.RemoveRange(lastUser + 1, _current.Messages.Count - lastUser - 1);
        Show(_current);
        var user = _current.Messages[lastUser];
        _ = AnswerAsync(_images.GetValueOrDefault(user), "image/png", exclude);
    }

    private bool IsNearBottom() => _scroll.VerticalOffset >= _scroll.ScrollableHeight - 80;

    internal static List<ChatMessage> BuildContext(IReadOnlyList<SavedMessage> history, byte[]? image, string mime)
    {
        var list = new List<ChatMessage>();
        int budget = MaxContextChars;
        int lastUser = -1;
        for (int i = history.Count - 1; i >= 0; i--) if (history[i].Role == Role.User) { lastUser = i; break; }
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var m = history[i];
            if (m.Alternate || m.ImageOnly) continue;          // 比較時的其他答案、畫圖不放進上下文
            string text = m.Prompt;
            if (text.Length > budget && list.Count > 0) break;
            budget -= text.Length;
            list.Insert(0, new ChatMessage(m.Role, text, i == lastUser ? image : null, mime));
        }
        list.Insert(0, new ChatMessage(Role.System, SystemPrompt));
        return list;
    }

    // ═══════════════════════ 畫圖 ═══════════════════════

    private const string PromptWriter =
        "你是 AI 繪圖提示詞專家。把使用者的畫面描述翻譯並擴寫成一段適合 FLUX／Stable Diffusion 的英文提示詞：" +
        "主體、場景、構圖、光線、風格、細節，用逗號分隔。只輸出提示詞本身，不要任何說明或引號。";

    /// <summary>把輸入框的描述畫成圖：能用聊天模型時先改寫成英文提示詞（畫得比較準），再交給生圖服務。</summary>
    private async Task DrawAsync()
    {
        string idea = _input.Text.Trim();
        if (idea.Length == 0) { _route.Text = "先在輸入框描述想畫什麼，再按「畫圖」"; _input.Focus(); return; }
        if (_cts is not null) return;
        string aspect = (_aspect.SelectedItem as ComboBoxItem)?.Tag as string ?? "1:1";
        var (w, h) = ImageGenerator.SizeOf(aspect);

        if (_current.Messages.Count == 0)
        {
            _messages.Children.Clear();
            _current.Title = Conversation.TitleFrom("畫：" + idea);
            _conversations.Insert(0, _current);
        }
        var ask = new SavedMessage { Role = Role.User, Text = "畫：" + idea, ImageOnly = true };
        _current.Messages.Add(ask);
        _messages.Children.Add(new MessageView(Role.User, ask.Text, null));
        _input.Clear();
        var reply = new MessageView(Role.Assistant, "", "準備中…");
        _messages.Children.Add(reply);
        _scroll.ScrollToEnd();

        _cts = new CancellationTokenSource();
        _send.Visibility = Visibility.Collapsed;
        _stop.Visibility = Visibility.Visible;
        try
        {
            string prompt = await WritePromptAsync(idea, reply, _cts.Token);
            var image = await _painter.GenerateAsync(prompt, w, h, s => Dispatcher.Invoke(() => reply.SetHeader(s)), _cts.Token);
            string? path = _history.SaveImage(image.Bytes, image.Mime);
            var answer = new SavedMessage { Role = Role.Assistant, ImageOnly = true, ImagePath = path, Model = image.Source, Text = "提示詞：" + prompt };
            _current.Messages.Add(answer);
            int index = _messages.Children.IndexOf(reply);
            var view = new MessageView(Role.Assistant, answer.Text, $"{image.Source} · {aspect}", image.Bytes, largeImage: true);
            _messages.Children[index] = view;
            AttachImageActions(view, answer, image.Bytes);
        }
        catch (OperationCanceledException) { reply.SetHeader("已停止", warn: true); }
        catch (Exception e)
        {
            reply.SetHeader("沒有畫出來", warn: true);
            reply.SetText(e.Message);
            if (_input.Text.Length == 0) _input.Text = idea;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _send.Visibility = Visibility.Visible;
            _stop.Visibility = Visibility.Collapsed;
        }
        _current.Updated = DateTime.Now;
        _history.Save(_current);
        RefreshList();
        _scroll.ScrollToEnd();
    }

    /// <summary>請最快的聊天模型把描述改寫成英文提示詞；沒有可用模型或失敗時就用原文。</summary>
    private async Task<string> WritePromptAsync(string idea, MessageView reply, CancellationToken ct)
    {
        var candidates = _conductor.Candidates(Conductor.Auto, TaskKind.Quick).Take(3).ToList();
        if (candidates.Count == 0) return idea;
        reply.SetHeader("把描述整理成繪圖提示詞…");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var sb = new StringBuilder();
        try
        {
            await _conductor.AskAsync([new ChatMessage(Role.System, PromptWriter), new ChatMessage(Role.User, idea)], candidates,
                (_, _) => { }, piece => { lock (sb) sb.Append(piece); }, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return idea; }
        catch (ProviderException) { return idea; }
        string prompt = sb.ToString().Trim().Trim('"', '「', '」');
        return prompt.Length is > 0 and < 1500 ? prompt : idea;
    }

    /// <summary>生成圖片下方的按鈕。</summary>
    private void AttachImageActions(MessageView view, SavedMessage message, byte[] bytes)
    {
        view.SetActions(
            ("存檔", "另存這張圖", () => SaveImageAs(bytes, message)),
            ("複製圖片", "複製到剪貼簿，可以直接貼到其他程式", () => { try { Clipboard.SetImage(Decode(bytes)); _route.Text = "已複製圖片"; } catch (Exception) { } }),
            ("拿去問 AI", "把這張圖附到輸入框，請 AI 描述或給修改建議", () =>
            {
                _attachments.RemoveAll(x => x.Kind == AttachmentKind.Image);
                _attachments.Add(new Attachment("生成的圖片", AttachmentKind.Image, Bytes: bytes, Mime: bytes.Length > 3 && bytes[0] == 0x89 ? "image/png" : "image/jpeg"));
                RefreshChips();
            }),
            ("再畫一次", "用同樣的描述再畫一張", () =>
            {
                int i = _current.Messages.IndexOf(message);
                if (i > 0 && _current.Messages[i - 1].Text.StartsWith("畫：", StringComparison.Ordinal)) _input.Text = _current.Messages[i - 1].Text[2..];
                _ = DrawAsync();
            }));
    }

    private void SaveImageAs(byte[] bytes, SavedMessage message)
    {
        bool png = bytes.Length > 3 && bytes[0] == 0x89;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = png ? "PNG 圖片|*.png" : "JPEG 圖片|*.jpg", DefaultExt = png ? ".png" : ".jpg", AddExtension = true, Title = "儲存圖片",
            FileName = message.ImagePath is { } p ? Path.GetFileNameWithoutExtension(p) : "問道生成",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dlg.FileName, bytes); _route.Text = "已儲存：" + Path.GetFileName(dlg.FileName); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _route.Text = "存不起來：" + e.Message; }
    }

    // ═══════════════════════ 語音 ═══════════════════════

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>叫出 Windows 內建的語音輸入（Win+H），說的話會直接打進輸入框。</summary>
    private void Dictate()
    {
        _input.Focus();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            const byte win = 0x5B, h = 0x48;
            const uint up = 0x0002;
            keybd_event(win, 0, 0, UIntPtr.Zero);
            keybd_event(h, 0, 0, UIntPtr.Zero);
            keybd_event(h, 0, up, UIntPtr.Zero);
            keybd_event(win, 0, up, UIntPtr.Zero);
        });
    }

    /// <summary>朗讀回答（Windows 內建語音，優先用中文聲音）；正在唸時再按就停止。</summary>
    private async Task SpeakAsync(string text)
    {
        if (_player is not null) { _player.Stop(); _player = null; return; }
        try
        {
            string plain = Markdown.Plain(string.Join("\n", MessageView.Split(text).Where(p => !p.IsCode).Select(p => p.Text)));
            if (plain.Length > 4000) plain = plain[..4000];
            using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
            var voices = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices;
            bool chinese = plain.Any(c => c >= 0x4E00 && c <= 0x9FFF);
            var voice = chinese
                ? voices.FirstOrDefault(v => v.Language.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)) ?? voices.FirstOrDefault(v => v.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                : voices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (voice is not null) synth.Voice = voice;
            using var stream = await synth.SynthesizeTextToStreamAsync(plain);
            var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            ms.Position = 0;
            var player = new System.Media.SoundPlayer(ms);
            _player = player;
            _ = Task.Run(() =>
            {
                try { player.PlaySync(); } catch (Exception) { }
                Dispatcher.InvokeAsync(() => { if (_player == player) _player = null; });   // 唸完了，下次按就是重新唸
            });
            _route.Text = chinese && voice is null ? "這台電腦沒有中文語音（設定 → 時間與語言 → 語音 可以新增）" : "朗讀中（再按一次「朗讀」停止）";
        }
        catch (Exception e) { _route.Text = "無法朗讀：" + e.Message; _player = null; }
    }

    // ═══════════════════════ 匯出 ═══════════════════════

    private void ExportConversation()
    {
        if (_current.Messages.Count == 0) { _route.Text = "這段對話還是空的"; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Markdown|*.md|純文字|*.txt", DefaultExt = ".md", AddExtension = true, Title = "匯出對話",
            FileName = string.Concat(_current.Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).TrimEnd('…'),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, ToMarkdown(_current), new UTF8Encoding(false));
            _route.Text = "已匯出：" + Path.GetFileName(dlg.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _route.Text = "匯出失敗：" + e.Message; }
    }

    internal static string ToMarkdown(Conversation c)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(c.Title).Append("\n\n");
        foreach (var m in c.Messages)
        {
            if (m.Role == Role.User)
            {
                sb.Append("## 問\n\n").Append(m.Text).Append("\n\n");
                if (m.Files is { Count: > 0 }) sb.Append("附件：").Append(string.Join("、", m.Files)).Append("\n\n");
            }
            else
            {
                sb.Append("## 答（").Append(m.Model ?? "AI").Append("）\n\n");
                if (m.ImagePath is { } path) sb.Append("![生成的圖片](").Append(new Uri(path).AbsoluteUri).Append(")\n\n");
                sb.Append(m.Text).Append("\n\n");
            }
        }
        return sb.ToString();
    }

    // ═══════════════════════ 附件 ═══════════════════════

    private void PickFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = FileReader.DialogFilter, Title = "附加檔案", Multiselect = true };
        if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FileNames) AttachFile(f);
    }

    public void AttachFile(string path)
    {
        if (!FileReader.CanRead(path)) { _route.Text = $"還不會讀 {Path.GetExtension(path)} 檔（可以附 PDF、Word、Excel、PowerPoint、圖片、文字與程式碼）"; return; }
        try
        {
            var a = FileReader.Read(path);
            if (a.Kind == AttachmentKind.Image)
            {
                if (_attachments.Any(x => x.Kind == AttachmentKind.Image)) _attachments.RemoveAll(x => x.Kind == AttachmentKind.Image);   // 一次一張圖
                var (bytes, mime) = PrepareImage(a.Bytes!, a.Mime, Path.GetExtension(path).ToLowerInvariant());
                a = a with { Bytes = bytes, Mime = mime };
            }
            _attachments.Add(a);
            RefreshChips();
            if (a.Truncated) _route.Text = $"「{a.Name}」很長，只會附上前面 {FileReader.MaxChars / 10000} 萬字";
        }
        catch (Exception e) { _route.Text = $"讀不到「{Path.GetFileName(path)}」：{e.Message}"; }
    }

    private void RefreshChips()
    {
        _attachChips.Children.Clear();
        foreach (var a in _attachments)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (a.Kind == AttachmentKind.Image && a.Bytes is { } b)
            {
                var img = new Image { Height = 40, Margin = new Thickness(0, 0, 6, 0) };
                try { img.Source = Decode(b); } catch (Exception) { }
                row.Children.Add(img);
            }
            else row.Children.Add(new TextBlock { Text = "📎 " + a.Name + (a.Truncated ? "（太長，已截斷）" : ""), Foreground = Look.Moon, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var captured = a;
            var remove = Look.Button("✕", () => { _attachments.Remove(captured); RefreshChips(); }, tip: "移除");
            remove.Padding = new Thickness(6, 0, 6, 0);
            remove.FontSize = 11;
            row.Children.Add(remove);
            _attachChips.Children.Add(new Border { Child = row, Background = Look.Field, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 4, 4, 4), Margin = new Thickness(0, 0, 6, 4) });
        }
        _attachChips.Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _input.Focus();
    }

    private void ClearAttachments()
    {
        _attachments.Clear();
        RefreshChips();
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is { } img)
        {
            _attachments.RemoveAll(x => x.Kind == AttachmentKind.Image);
            var (bytes, mime) = PrepareImage(Encode(img), "image/png", ".png");
            _attachments.Add(new Attachment("貼上的圖片.png", AttachmentKind.Image, Bytes: bytes, Mime: mime));
            RefreshChips();
            e.CancelCommand();
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var f in files) AttachFile(f);
    }

    /// <summary>GIF、BMP 轉成 PNG；太大的圖縮小（免費模型對圖片大小有限制，也省流量）。</summary>
    private static (byte[] Bytes, string Mime) PrepareImage(byte[] bytes, string mime, string ext)
    {
        try
        {
            if (ext is ".gif" or ".bmp") { bytes = Encode(Decode(bytes)); mime = "image/png"; }
            if (bytes.Length > 1_500_000)
            {
                var src = Decode(bytes);
                double k = Math.Min(1, 1600.0 / Math.Max(src.PixelWidth, src.PixelHeight));
                bytes = Encode(new TransformedBitmap(src, new ScaleTransform(k, k)), jpeg: true);
                mime = "image/jpeg";
            }
        }
        catch (Exception) { }
        return (bytes, mime);
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.StreamSource = new MemoryStream(bytes);
        b.EndInit();
        b.Freeze();
        return b;
    }

    private static byte[] Encode(BitmapSource src, bool jpeg = false)
    {
        BitmapEncoder enc = jpeg ? new JpegBitmapEncoder { QualityLevel = 85 } : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
