using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TianMu.Modules.Shelf.Views;

/// <summary>
/// 乾坤袋的畫面：按住檔案拖出去即可放到檔案總管、聊天視窗、瀏覽器上傳欄位……；雙擊直接開啟。
/// 拖出時一律「複製」，原始檔案不會被搬走。
/// </summary>
public partial class ShelfView : UserControl
{
    private Point _pressAt;
    private ShelfItem? _pressed;

    public ShelfView()
    {
        InitializeComponent();
    }

    private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShelfItem item }) return;

        if (e.ClickCount == 2)
        {
            Open(item.Path);
        }
        else
        {
            _pressAt = e.GetPosition(this);
            _pressed = item;
        }

        // 不讓天幕把這次按下當成「點擊收合」或「拖曳天幕」
        e.Handled = true;
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _pressed;
        _pressed = null;
        var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Link);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[乾坤袋] 拖出失敗：{ex.Message}");
        }
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[乾坤袋] 無法開啟 {path}：{ex.Message}"); }
    }
}
