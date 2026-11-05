using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TianMu.Modules.Todo.Views;

/// <summary>記一筆：輸入待辦與提醒時間。Enter 記下、Esc 關閉。</summary>
public partial class TodoEditorWindow : Window
{
    private readonly Action<string, DateTime> _save;
    private DateTime? _dueAt;

    public TodoEditorWindow(Action<string, DateTime> save)
    {
        InitializeComponent();
        _save = save;
        TimeField.Text = "1小時";
        Loaded += (_, _) =>
        {
            Activate();
            TextField.Focus();
            Keyboard.Focus(TextField);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text })
        {
            TimeField.Text = text;
            TextField.Focus();
        }
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SaveButton is null || Preview is null) return; // 初始化中

        var now = DateTime.Now;
        _dueAt = TodoTime.Parse(TimeField.Text, now);
        Preview.Text = _dueAt is { } at
            ? $"將於 {TodoTime.Describe(at, now)} 提醒"
            : "看不懂這個時間。可以寫「15:30」「下午 3 點」「明天 9 點」「11/20 14:00」「45 分」";
        SaveButton.IsEnabled = _dueAt is not null && !string.IsNullOrWhiteSpace(TextField.Text);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_dueAt is not { } at || string.IsNullOrWhiteSpace(TextField.Text)) return;
        _save(TextField.Text, at);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
