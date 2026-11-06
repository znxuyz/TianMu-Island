using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TianMu.Core;

namespace TianMu.Island;

/// <summary>
/// 天幕本體的第四部分：翻頁。
/// 展開後可以用左右箭頭（或滑鼠滾輪）在「時辰 → 常駐異象（樂律、專注…）→ 常駐頁面（AI 額度…）」之間切換，
/// 聽音樂時也看得到其他畫面。下方的頁點標示目前在第幾頁。
/// </summary>
public partial class IslandWindow
{
    private const double PagerGutter = 30;      // 左右各留給箭頭的寬度
    private const double PagerDotsHeight = 16;  // 下方頁點的高度
    private const string ClockPageKey = "tianmu.clock";

    private readonly List<IslandActivity> _pinnedPages = [];
    private string _pageKey = ClockPageKey;

    // ═══════════════════════ 法器 API ═══════════════════════

    public void SetPage(IslandActivity page) => Dispatcher.InvokeAsync(() =>
    {
        int index = _pinnedPages.FindIndex(p => p.ModuleId == page.ModuleId);
        if (index >= 0) _pinnedPages[index] = page;
        else _pinnedPages.Add(page);
        Refresh();
    });

    public void RemovePage(string moduleId) => Dispatcher.InvokeAsync(() =>
    {
        _pinnedPages.RemoveAll(p => p.ModuleId == moduleId);
        Refresh();
    });

    // ═══════════════════════ 頁面清單 ═══════════════════════

    /// <summary>目前所有頁面：時辰在最前，接著是有展開畫面的常駐異象，最後是常駐頁面。</summary>
    private List<(string Key, IslandActivity? Activity)> Pages()
    {
        var pages = new List<(string Key, IslandActivity? Activity)> { (ClockPageKey, null) };
        foreach (var a in _ambient)
            if (a.ExpandedView is not null) pages.Add((a.ModuleId, a));
        foreach (var p in _pinnedPages)
            if (p.ExpandedView is not null && pages.All(x => x.Key != p.ModuleId)) pages.Add((p.ModuleId, p));
        return pages;
    }

    /// <summary>目前選中的頁面（時辰頁回傳 null）。選中的頁面消失時回到時辰。</summary>
    private IslandActivity? CurrentPage
    {
        get
        {
            foreach (var (key, activity) in Pages())
                if (key == _pageKey) return activity;
            _pageKey = ClockPageKey;
            return null;
        }
    }

    /// <summary>選中某法器的頁面（例如音樂開始播放時）。</summary>
    private void SelectPage(string key) => _pageKey = key;

    private bool PagerVisible => IsExpanded && _transient is null && Pages().Count > 1;

    // ═══════════════════════ 切換 ═══════════════════════

    private void Navigate(int delta)
    {
        var pages = Pages();
        if (pages.Count < 2) return;

        int current = Math.Max(0, pages.FindIndex(p => p.Key == _pageKey));
        int next = ((current + delta) % pages.Count + pages.Count) % pages.Count;
        _pageKey = pages[next].Key;

        Refresh();
        PlayPageReveal(delta);
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextPage_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void Capsule_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!PagerVisible) return;
        Navigate(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private readonly TranslateTransform _pageSlide = new();

    /// <summary>換頁時新內容從翻頁的方向滑入、以墨暈方式浮現（往下一頁從右邊來，往上一頁從左邊來）。</summary>
    private void PlayPageReveal(int direction)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        if (!ReferenceEquals(PageContent.RenderTransform, _pageSlide)) PageContent.RenderTransform = _pageSlide;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(320);
        PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.1, 1, duration) { EasingFunction = ease });
        _pageSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(Math.Sign(direction) * 28, 0, duration) { EasingFunction = ease });
    }

    // ═══════════════════════ 畫面 ═══════════════════════

    /// <summary>更新箭頭、頁點與內容留白。</summary>
    private void UpdatePager()
    {
        bool visible = PagerVisible;
        PagerLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PageContent.Margin = visible ? new Thickness(PagerGutter, 0, PagerGutter, PagerDotsHeight) : new Thickness(0);
        if (!visible) return;

        var pages = Pages();
        int current = Math.Max(0, pages.FindIndex(p => p.Key == _pageKey));

        PageDots.Children.Clear();
        var active = (Brush)FindResource("YueBaiBrush");
        var idle = (Brush)FindResource("TrackBrush");
        for (int i = 0; i < pages.Count; i++)
        {
            PageDots.Children.Add(new Border
            {
                Width = i == current ? 14 : 5,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Margin = new Thickness(2.5, 0, 2.5, 0),
                Background = i == current ? active : idle,
            });
        }
    }
}
