using System.Diagnostics;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace TianMu.Modules.Notify.Notifications;

/// <summary>
/// 用 Windows 官方的通知接口（UserNotificationListener）讀取新通知。
/// 好處是可以在天幕顯示後，把系統通知中心裡的那一則刪掉，避免同一則通知出現兩次。
/// 需要使用者同意「通知存取」，而且部分 Windows 版本不開放給未封裝的程式；
/// 拿不到時回傳 null，由呼叫端改用讀取通知資料庫的方式。
/// </summary>
public sealed class NotificationListenerSource : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);

    private readonly UserNotificationListener _listener;
    private readonly HashSet<uint> _known = [];
    private readonly Timer _timer;
    private int _polling;

    public event Action<ToastNotice>? Arrived;

    private NotificationListenerSource(UserNotificationListener listener)
    {
        _listener = listener;
        _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>試著取得官方接口；不支援、被拒絕或逾時都回傳 null。</summary>
    public static async Task<NotificationListenerSource?> TryCreateAsync()
    {
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener")) return null;
            var listener = UserNotificationListener.Current;

            var request = listener.RequestAccessAsync().AsTask();
            if (await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(5))) != request) return null;
            if (await request != UserNotificationListenerAccessStatus.Allowed) return null;

            var source = new NotificationListenerSource(listener);
            // 啟動前就在的通知不算新通知
            foreach (var n in await listener.GetNotificationsAsync(NotificationKinds.Toast)) source._known.Add(n.Id);
            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[傳音入密] 官方通知接口不可用，改讀通知資料庫：{ex.Message}");
            return null;
        }
    }

    public void Start() => _timer.Change(TimeSpan.Zero, Interval);

    /// <summary>從系統通知中心刪掉這一則（天幕已經顯示過了）。</summary>
    public void Remove(long id)
    {
        try { _listener.RemoveNotification((uint)id); }
        catch (Exception ex) { Debug.WriteLine($"[傳音入密] 無法移除系統通知：{ex.Message}"); }
    }

    private async Task PollAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var all = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var present = new HashSet<uint>();
            foreach (var n in all)
            {
                present.Add(n.Id);
                if (!_known.Add(n.Id)) continue;
                if (Convert(n) is { } notice) Arrived?.Invoke(notice);
            }
            _known.IntersectWith(present); // 被使用者清掉的就忘了它
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[傳音入密] 讀取通知失敗：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private static ToastNotice? Convert(UserNotification n)
    {
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text ?? string.Empty).Where(t => t.Length > 0).ToList() ?? [];
            if (texts.Count == 0) return null;

            string appId = n.AppInfo?.AppUserModelId ?? string.Empty;
            string appName = n.AppInfo?.DisplayInfo?.DisplayName is { Length: > 0 } name ? name : AppNames.Resolve(appId);
            return new ToastNotice(n.Id, appId, appName, texts[0], string.Join("\n", texts.Skip(1)), null,
                n.CreationTime.LocalDateTime);
        }
        catch
        {
            return null; // 單一通知讀不到就略過
        }
    }

    public void Dispose() => _timer.Dispose();
}
