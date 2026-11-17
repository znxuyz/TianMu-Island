using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TianMu.Island.Shell;

namespace TianMu.Tests;

public class UprightTextTests
{
    [Fact]
    public void Side_dock_turns_text_upright_and_one_character_per_line_then_restores()
    {
        RunOnSta(() =>
        {
            var title = new TextBlock { Text = "早安台股", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var panel = new Grid();
            panel.Children.Add(new StackPanel { Children = { title } });

            UprightText.Apply(panel, 90);
            Assert.Equal(-90, Assert.IsType<RotateTransform>(title.LayoutTransform).Angle);
            Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
            Assert.True(title.Width < 12 * 1.5);              // 一行只放得下一個字

            title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(title.DesiredSize.Height < title.DesiredSize.Width, "轉回正立後，在轉了 90 度的容器裡應該是橫長的");

            UprightText.Apply(panel, 90);                       // 重複套用不影響還原
            UprightText.Restore(panel);
            Assert.Same(Transform.Identity, title.LayoutTransform);
            Assert.True(double.IsNaN(title.Width));
            Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
        });
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
