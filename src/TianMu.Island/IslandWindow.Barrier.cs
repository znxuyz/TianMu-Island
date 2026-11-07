using System.Windows;
using TianMu.Island.Shell;
using Forms = System.Windows.Forms;

namespace TianMu.Island;

/// <summary>
/// 天幕本體：專注結界。番茄鐘專注時（<see cref="IsFocusMode"/>），在天幕所在的螢幕四邊亮起呼吸的金光。
/// 天幕隱藏（手動或全螢幕）時結界也一起退下。
/// </summary>
public partial class IslandWindow
{
    private FocusBarrierWindow? _barrier;

    private void UpdateBarrier()
    {
        bool want = _focusMode && !_userHidden && !_fullscreenHidden && _settings.Read(s => s.FocusBarrier);
        if (!want)
        {
            _barrier?.Close();
            _barrier = null;
            return;
        }

        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _activeScreenName) ?? Forms.Screen.PrimaryScreen;
        if (screen is null) return;

        _barrier ??= new FocusBarrierWindow();
        _barrier.Cover(ToDip(screen.Bounds));
        if (!_barrier.IsVisible) _barrier.Show();
        ReassertTopmost(); // 天幕要壓在結界之上
    }
}
