using System.Windows;

namespace TianMu.Modules.Ai.Views;

/// <summary>連結 Claude 的說明與 sessionKey 輸入視窗。</summary>
public partial class ConnectWindow : Window
{
    private readonly Action<string> _save;
    private readonly Action _clear;

    public ConnectWindow(bool claudeCodeFound, bool hasSavedKey, Action<string> save, Action clear)
    {
        InitializeComponent();
        _save = save;
        _clear = clear;

        ClaudeCodeStatus.Text = claudeCodeFound ? "✓ 已偵測到 Claude Code 的登入" : "尚未偵測到 Claude Code 的登入";
        KeyStatus.Text = hasSavedKey ? "已存有一組 sessionKey（貼上新的會取代它）" : string.Empty;
        ClearButton.IsEnabled = hasSavedKey;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string key = KeyBox.Password.Trim();
        if (key.StartsWith("sessionKey=", StringComparison.OrdinalIgnoreCase)) key = key["sessionKey=".Length..];

        if (key.Length < 20)
        {
            KeyStatus.Text = "這看起來不像 sessionKey，請確認是否完整複製";
            return;
        }

        _save(key);
        Close();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _clear();
        KeyStatus.Text = "已移除";
        ClearButton.IsEnabled = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
