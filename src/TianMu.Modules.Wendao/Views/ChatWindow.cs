using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TianMu.Modules.Wendao.Ai;
using TianMu.Modules.Wendao.Store;

namespace TianMu.Modules.Wendao.Views;

/// <summary>
/// 問道的對話視窗：左邊是對話紀錄，上面選模型（預設「自動」），下面輸入問題；
/// 可以附圖片（貼上、拖進來或按迴紋針）。Enter 送出，Shift+Enter 換行。
/// </summary>
internal sealed class ChatWindow : Window
{
    private const int MaxContextChars = 60_000;
    private const string SystemPrompt =
        "你是天幕島裡的助理「問道」。預設用繁體中文（台灣用語）回答；如果使用者用其他語言發問，就用那個語言。回答要清楚、直接，必要時用條列或程式碼區塊。";

    private readonly Conductor _conductor;
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
    private readonly Border _attachChip = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    private byte[]? _attachment;
    private string _attachmentMime = "image/png";
    private CancellationTokenSource? _cts;

    public ChatWindow(Conductor conductor, string? dataDir)
    {
        _conductor = conductor;
        _history = new History(dataDir);
        _conversations = _history.LoadAll();
        Title = "問道";
        Width = 1040;
        Height = 720;
        MinWidth = 640;
        MinHeight = 420;
        Background = Look.Lacquer;
        FontFamily = Look.Sans;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // ── 左：對話紀錄 ──
        var left = new DockPanel { Background = Look.Panel };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 16, 16, 10) };
        brand.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(5), Background = Look.Cinnabar, Child = new TextBlock { Text = "問", FontFamily = Look.Serif, FontWeight = FontWeights.Bold, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        brand.Children.Add(new TextBlock { Text = "問道", FontFamily = Look.Serif, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Look.Gold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brand, Dock.Top);
        left.Children.Add(brand);
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
        root.Children.Add(left);

        // ── 右：模型列＋訊息＋輸入 ──
        var right = new DockPanel();
        Grid.SetColumn(right, 1);
        var top = new DockPanel { Margin = new Thickness(20, 12, 20, 10) };
        top.Children.Add(new TextBlock { Text = "模型", Foreground = Look.Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        top.Children.Add(_model);
        top.Children.Add(_route);
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(new Border { Child = top, BorderBrush = Look.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) }.Also(b => DockPanel.SetDock(b, Dock.Top)));

        var composer = new DockPanel { Margin = new Thickness(20, 10, 20, 16) };
        _send = Look.Button("送出", Send, primary: true, tip: "送出（Enter）");
        _stop = Look.Button("停止", () => _cts?.Cancel(), tip: "停止回答");
        _stop.Visibility = Visibility.Collapsed;
        var attach = Look.Button("圖片", PickImage, tip: "附一張圖片（也可以直接貼上或拖進來）");
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 0) };
        actions.Children.Add(attach);
        actions.Children.Add(new Border { Height = 6 });
        actions.Children.Add(_send);
        actions.Children.Add(_stop);
        DockPanel.SetDock(actions, Dock.Right);
        composer.Children.Add(actions);
        var inputStack = new StackPanel();
        inputStack.Children.Add(_attachChip);
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
        right.Children.Add(composer);

        _scroll.Content = _messages;
        right.Children.Add(_scroll);
        root.Children.Add(right);
        Content = root;

        AllowDrop = true;
        Drop += OnDrop;
        Loaded += async (_, _) => { RefreshList(); ShowEmpty(); _input.Focus(); await ReloadModelsAsync(); };
        Closed += (_, _) => _cts?.Cancel();
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
        foreach (var group in _conductor.Models.GroupBy(m => m.ProviderId))
        {
            var p = Provider.Find(group.Key)!;
            _model.Items.Add(new ComboBoxItem { Content = $"── {p.Name} ──", IsEnabled = false, FontWeight = FontWeights.SemiBold });
            foreach (var m in group)
                _model.Items.Add(new ComboBoxItem { Content = "　" + m.Name + (m.Vision ? "　🖼" : ""), Tag = m.Key, ToolTip = m.Id });
        }
        _model.SelectedItem = _model.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == keep) ?? _model.Items[0];
        int count = _conductor.Models.Count;
        _route.Text = count == 0 ? "還沒有可用的模型 — 按左下角「連線設定」貼上免費金鑰" : $"共 {count} 個免費模型可用";
    }

    private string Choice => (_model.SelectedItem as ComboBoxItem)?.Tag as string ?? Conductor.Auto;

    private void OpenConnections()
    {
        new ConnectionWindow(_conductor, this).ShowDialog();
        FillModels();
    }

    // ═══════════════════════ 對話 ═══════════════════════

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
            _messages.Children.Add(new MessageView(m.Role, m.Text, m.Role == Role.Assistant ? m.Model : null, _images.GetValueOrDefault(m), m.HadImage));
        _scroll.ScrollToEnd();
    }

    private void ShowEmpty()
    {
        _messages.Children.Clear();
        var hello = new StackPanel { Margin = new Thickness(0, 60, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        hello.Children.Add(new TextBlock { Text = "有什麼想問的？", FontFamily = Look.Serif, FontSize = 26, Foreground = Look.Gold, HorizontalAlignment = HorizontalAlignment.Center });
        hello.Children.Add(new TextBlock
        {
            Text = "選「自動」時，問道會看問題性質挑模型：\n寫程式、推理分析找大模型；看圖、讀長文件找 Gemini；快問快答找最快的。\n某一家的免費額度用完，就自動換下一家。",
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

    private async void Send()
    {
        string question = _input.Text.Trim();
        if ((question.Length == 0 && _attachment is null) || _cts is not null) return;
        if (question.Length == 0) question = "請描述這張圖片。";

        if (_current.Messages.Count == 0)
        {
            _messages.Children.Clear();
            _current.Title = Conversation.TitleFrom(question);
            _conversations.Insert(0, _current);
        }
        var userMsg = new SavedMessage { Role = Role.User, Text = question, HadImage = _attachment is not null };
        if (_attachment is not null) _images[userMsg] = _attachment;
        _current.Messages.Add(userMsg);
        _messages.Children.Add(new MessageView(Role.User, question, null, _attachment));
        byte[]? image = _attachment;
        string mime = _attachmentMime;
        ClearAttachment();
        _input.Clear();

        var reply = new MessageView(Role.Assistant, "", "思考中…");
        _messages.Children.Add(reply);
        _scroll.ScrollToEnd();

        // 組成上下文：系統提示＋最近的對話（太長就從舊的砍），圖片只附在這一題
        var context = BuildContext(_current.Messages, image, mime);
        int historyChars = context.Sum(m => m.Text.Length) - question.Length;
        var kind = Router.Classify(question, image is not null, historyChars);
        var candidates = _conductor.Candidates(Choice, kind);

        _cts = new CancellationTokenSource();
        _send.Visibility = Visibility.Collapsed;
        _stop.Visibility = Visibility.Visible;
        var answer = new SavedMessage { Role = Role.Assistant };
        try
        {
            var used = await _conductor.AskAsync(context, candidates,
                (m, failed) => Dispatcher.Invoke(() =>
                {
                    string label = $"{ModelCatalog.Pretty(m.Id)} · {Provider.Find(m.ProviderId)?.Name}";
                    reply.SetHeader(failed is null ? label : $"{label}（前一個失敗，已改用這個）", warn: failed is not null);
                    _route.Text = Choice == Conductor.Auto ? $"判斷為「{Router.Label(kind)}」→ {label}" : label;
                }),
                piece => Dispatcher.InvokeAsync(() => { reply.Append(piece); if (IsNearBottom()) _scroll.ScrollToEnd(); }),
                _cts.Token);
            await Dispatcher.InvokeAsync(() => { });   // 等最後一段字畫上去
            answer.Model = $"{ModelCatalog.Pretty(used.Id)} · {Provider.Find(used.ProviderId)?.Name}";
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
            answer = null!;
            _current.Messages.Remove(userMsg);
            _input.Text = question;   // 問題放回輸入框，修好連線再按一次
        }
        catch (Exception e)
        {
            // 網路中斷之類的意外：照實顯示，不讓天幕島整個當掉
            reply.SetHeader("連線中斷", warn: true);
            if (reply.Text.Length == 0) { reply.SetText(e.Message); answer = null!; _current.Messages.Remove(userMsg); _input.Text = question; }
            else { answer.Text = reply.Text + "\n\n（連線中斷）"; answer.Model = "連線中斷"; reply.SetText(answer.Text); }
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _send.Visibility = Visibility.Visible;
            _stop.Visibility = Visibility.Collapsed;
        }

        if (answer is not null && answer.Text.Length > 0) _current.Messages.Add(answer);
        _current.Updated = DateTime.Now;
        if (_current.Messages.Count == 0) _conversations.Remove(_current);
        _history.Save(_current);
        RefreshList();
        _scroll.ScrollToEnd();
        _input.Focus();
    }

    private bool IsNearBottom() => _scroll.VerticalOffset >= _scroll.ScrollableHeight - 80;

    internal static List<ChatMessage> BuildContext(IReadOnlyList<SavedMessage> history, byte[]? image, string mime)
    {
        var list = new List<ChatMessage>();
        int budget = MaxContextChars;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var m = history[i];
            if (m.Text.Length > budget && list.Count > 0) break;
            budget -= m.Text.Length;
            bool last = i == history.Count - 1;
            list.Insert(0, new ChatMessage(m.Role, m.Text, last ? image : null, mime));
        }
        list.Insert(0, new ChatMessage(Role.System, SystemPrompt));
        return list;
    }

    // ═══════════════════════ 圖片 ═══════════════════════

    private void PickImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "圖片|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp", Title = "附一張圖片" };
        if (dlg.ShowDialog(this) == true) AttachFile(dlg.FileName);
    }

    public void AttachFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp")) { _route.Text = "只能附圖片（PNG、JPG、WebP、GIF、BMP）"; return; }
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (ext is ".gif" or ".bmp") { bytes = ToPng(bytes); ext = ".png"; }
            SetAttachment(bytes, ext is ".jpg" or ".jpeg" ? "image/jpeg" : ext == ".webp" ? "image/webp" : "image/png");
        }
        catch (Exception e) { _route.Text = "讀不到這張圖：" + e.Message; }
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is { } img)
        {
            SetAttachment(Encode(img), "image/png");
            e.CancelCommand();
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) AttachFile(files[0]);
    }

    private void SetAttachment(byte[] bytes, string mime)
    {
        // 太大的圖先縮小（免費模型對圖片大小有限制，也省流量）
        if (bytes.Length > 1_500_000) { bytes = Shrink(bytes, 1600); mime = "image/jpeg"; }
        _attachment = bytes;
        _attachmentMime = mime;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var preview = new Image { Height = 48, Margin = new Thickness(0, 0, 8, 0) };
        try { preview.Source = Decode(bytes); } catch (Exception) { }
        row.Children.Add(preview);
        var remove = Look.Button("移除", ClearAttachment);
        remove.Padding = new Thickness(8, 2, 8, 2);
        remove.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(remove);
        _attachChip.Child = row;
        _attachChip.Visibility = Visibility.Visible;
        _input.Focus();
    }

    private void ClearAttachment()
    {
        _attachment = null;
        _attachChip.Child = null;
        _attachChip.Visibility = Visibility.Collapsed;
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

    private static byte[] ToPng(byte[] bytes) => Encode(Decode(bytes));

    private static byte[] Shrink(byte[] bytes, int maxSide)
    {
        try
        {
            var src = Decode(bytes);
            double k = Math.Min(1, maxSide / (double)Math.Max(src.PixelWidth, src.PixelHeight));
            var scaled = new TransformedBitmap(src, new ScaleTransform(k, k));
            return Encode(scaled, jpeg: true);
        }
        catch (Exception) { return bytes; }
    }
}

internal static class Fluent
{
    public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
