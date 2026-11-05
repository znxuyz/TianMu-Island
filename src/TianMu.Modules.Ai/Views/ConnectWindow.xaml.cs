using System.Windows;

namespace TianMu.Modules.Ai.Views;

/// <summary>連結 Claude：顯示登入狀態，提供登入與登出。</summary>
public partial class ConnectWindow : Window
{
    private readonly Func<Task<bool>> _isLoggedIn;
    private readonly Action _login;
    private readonly Func<Task> _logout;

    public ConnectWindow(bool claudeCodeFound, Func<Task<bool>> isLoggedIn, Action login, Func<Task> logout)
    {
        InitializeComponent();
        _isLoggedIn = isLoggedIn;
        _login = login;
        _logout = logout;

        ClaudeCodeStatus.Text = claudeCodeFound
            ? "另外偵測到 Claude Code 的登入：沒登入 Claude 時會改用它（需要最近開過 Claude Code）。"
            : string.Empty;

        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        bool loggedIn;
        try { loggedIn = await _isLoggedIn(); }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
            return;
        }

        Status.Text = loggedIn ? "✓ 已登入 Claude" : "尚未登入";
        LoginButton.Content = loggedIn ? "重新登入" : "登入 Claude";
        LogoutButton.IsEnabled = loggedIn;
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        _login();
        Close();
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await _logout();
        await RefreshAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
