using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TianMu.Modules.Notify.Notifications;

/// <summary>
/// 唯讀讀取 Windows 的通知資料庫（%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db），
/// 每 1.5 秒取一次新的通知。
///
/// 為什麼不用官方的 UserNotificationListener：它只開放給「封裝過」的商店 App，
/// 傳統 exe 需要額外的憑證與安裝步驟。這個資料庫是 Windows 通知中心本身的紀錄，只讀不寫，不會影響系統。
/// 啟動前就存在的舊通知不會重播。
/// </summary>
internal sealed class NotificationDbWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    private Timer? _timer;
    private long _lastId = -1;
    private int _busy;
    private bool _reportedFailure;

    /// <summary>新通知（背景執行緒觸發）。</summary>
    public event Action<ToastNotice>? Arrived;

    /// <summary>讀取失敗（例如 Windows 版本的資料庫格式不同），只會觸發一次。</summary>
    public event Action<string>? Failed;

    static NotificationDbWatcher()
    {
        // 指定使用 Windows 內建的 SQLite（winsqlite3.dll）
        SQLitePCL.Batteries_V2.Init();
    }

    public bool Exists => File.Exists(_path);

    public void Start() => _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, Interval);

    private void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (!File.Exists(_path)) return;

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 2,
            }.ToString());
            connection.Open();

            if (_lastId < 0)
            {
                // 第一次：記下目前最新的編號，不重播舊通知
                using var max = connection.CreateCommand();
                max.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM Notification";
                _lastId = Convert.ToInt64(max.ExecuteScalar());
                return;
            }

            using var query = connection.CreateCommand();
            query.CommandText = """
                SELECT n.Id, n.ArrivalTime, n.Payload, h.PrimaryId
                FROM Notification n
                JOIN NotificationHandler h ON h.RecordId = n.HandlerId
                WHERE n.Type = 'toast' AND n.Id > $last
                ORDER BY n.Id
                LIMIT 20
                """;
            query.Parameters.AddWithValue("$last", _lastId);

            using var reader = query.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                _lastId = Math.Max(_lastId, id);

                var payload = ReadPayload(reader, 2);
                if (payload is null || ToastPayload.Parse(payload) is not { } parsed) continue;

                string aumid = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var arrived = ReadTime(reader, 1);
                Arrived?.Invoke(new ToastNotice(id, aumid, AppNames.Resolve(aumid), parsed.Title, parsed.Body, parsed.Attribution, arrived));
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) // SQLITE_BUSY / SQLITE_LOCKED：下次再讀
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[傳音入密] 讀取通知失敗：{ex}");
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                Failed?.Invoke(ex.Message);
            }
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private static string? ReadPayload(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        object value = reader.GetValue(ordinal);
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        };
    }

    /// <summary>通知時間以 Windows FILETIME 儲存。</summary>
    private static DateTime ReadTime(SqliteDataReader reader, int ordinal)
    {
        try
        {
            if (!reader.IsDBNull(ordinal)) return DateTime.FromFileTimeUtc(reader.GetInt64(ordinal)).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException) { }
        return DateTime.Now;
    }

    public void Dispose() => _timer?.Dispose();
}
