using System.Globalization;
using System.Windows.Media;
using TianMu.Core;
using TianMu.Modules.Sense.Views;
using TianMu.Modules.Sense.Watchers;

namespace TianMu.Modules.Sense;

/// <summary>
/// 法器「靈識感應」：把系統的小變化化作天幕上的一瞬提示。
///   · 隨身碟、行動硬碟插入與移除（顯示名稱與剩餘空間）
///   · 已配對的藍牙裝置連線與斷線（耳機、滑鼠…）
///   · 系統音量與靜音、筆電螢幕亮度（帶刻度條，連續調整時同一則提示即時更新）
/// </summary>
public sealed class SenseModule : IIslandModule, IDescribedModule
{
    public const string DrivesOption = "drives";
    public const string BluetoothOption = "bluetooth";
    public const string VolumeOption = "volume";
    public const string BrightnessOption = "brightness";
    public const string PrivacyOption = "privacy";
    public const string UsbOption = "usb";

    // Segoe Fluent Icons / MDL2 Assets
    private const string GlyphUsb = "";
    private const string GlyphBluetooth = "";
    private const string GlyphVolume = "";
    private const string GlyphMute = "";
    private const string GlyphBrightness = "";
    private const string GlyphMic = "\uE720";
    private const string GlyphCamera = "\uE714";

    private static readonly TimeSpan DeviceDuration = TimeSpan.FromSeconds(3.5);
    private static readonly TimeSpan LevelDuration = TimeSpan.FromSeconds(1.6);

    private IIslandHost? _host;
    private DriveWatcher? _drives;
    private BluetoothWatcher? _bluetooth;
    private VolumeWatcher? _volume;
    private BrightnessWatcher? _brightness;
    private PrivacyWatcher? _privacy;
    private UsbDeviceWatcher? _usb;
    private DateTime _lastDriveEvent = DateTime.MinValue;
    private HashSet<string> _micUsers = [];
    private HashSet<string> _camUsers = [];

    // 以下只在 UI 執行緒存取
    private SignalViewModel? _deviceVm;
    private SignalView? _deviceView;
    private SignalViewModel? _levelVm;
    private SignalView? _levelView;

    public string Id => "tianmu.sense";
    public string DisplayName => "靈識感應";
    public string Description => "隨身碟插拔、藍牙裝置連線、音量與亮度變化時，天幕彈出一瞬提示。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(DrivesOption, "隨身碟與行動硬碟", "插入或移除時提示，並顯示剩餘空間。", true),
        new(BluetoothOption, "藍牙裝置", "已配對的耳機、滑鼠等連上或斷線時提示。", true),
        new(VolumeOption, "音量", "調整音量或靜音時，天幕顯示音量刻度。", true),
        new(BrightnessOption, "螢幕亮度", "調整亮度時顯示刻度（僅筆電等內建螢幕）。", true),
        new(UsbOption, "其他 USB 裝置", "滑鼠、鍵盤、手機、耳機等 USB 裝置接上或移除時提示。", true),
        new(PrivacyOption, "隱私燈", "有程式使用麥克風或相機時，膠囊下緣亮起朱砂小光點。", true),
    ];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;

        await host.InvokeAsync(() =>
        {
            _deviceVm = new SignalViewModel();
            _deviceView = new SignalView { DataContext = _deviceVm };
            _levelVm = new SignalViewModel();
            _levelView = new SignalView { DataContext = _levelVm };
        });

        _drives = new DriveWatcher();
        _drives.Arrived += d => OnUi(() => ShowDrive(d, arrived: true));
        _drives.Removed += d => OnUi(() => ShowDrive(d, arrived: false));

        _usb = new UsbDeviceWatcher();
        _usb.Changed += (name, plugged) => OnUi(() => ShowUsbLater(name, plugged));
        _usb.Start();

        _bluetooth = new BluetoothWatcher();
        _bluetooth.ConnectionChanged += (name, connected) => OnUi(() => ShowBluetooth(name, connected));
        _bluetooth.Start();

        _volume = new VolumeWatcher();
        _volume.Changed += (level, muted) => OnUi(() => ShowVolume(level, muted));
        _volume.Start();

        _brightness = new BrightnessWatcher();
        _brightness.Changed += level => OnUi(() => ShowBrightness(level));
        _brightness.Start();

        _privacy = new PrivacyWatcher();
        _privacy.Changed += (mic, cam) => OnUi(() => ShowPrivacy(mic, cam));
    }

    // ═══════════════════════ 隱私燈 ═══════════════════════

    /// <summary>
    /// 有程式正在用麥克風或相機時，右上角亮起朱砂小燈（滑鼠停留可看是哪個程式）；
    /// 開始使用的那一刻另外彈出一次提示。
    /// </summary>
    private void ShowPrivacy(IReadOnlyList<CapabilityUser> mic, IReadOnlyList<CapabilityUser> cam)
    {
        if (_host is null) return;
        bool enabled = Enabled(PrivacyOption);

        var micNow = mic.Select(u => u.AppName).ToHashSet();
        var camNow = cam.Select(u => u.AppName).ToHashSet();

        _host.SetIndicator("tianmu.sense.mic", enabled && micNow.Count > 0
            ? new IslandIndicator(GlyphMic, $"麥克風使用中 · {string.Join("、", micNow)}") : null);
        _host.SetIndicator("tianmu.sense.camera", enabled && camNow.Count > 0
            ? new IslandIndicator(GlyphCamera, $"相機使用中 · {string.Join("、", camNow)}") : null);

        if (enabled)
        {
            var newCam = camNow.Except(_camUsers).ToList();
            var newMic = micNow.Except(_micUsers).ToList();
            if (newCam.Count > 0) ShowDevice(GlyphCamera, "相機使用中", string.Join("、", newCam), "ZhuShaBrush");
            else if (newMic.Count > 0) ShowDevice(GlyphMic, "麥克風使用中", string.Join("、", newMic), "ZhuShaBrush");
        }

        _micUsers = micNow;
        _camUsers = camNow;
    }

    private void OnUi(Action action) => _ = _host?.InvokeAsync(action);

    private bool Enabled(string option) => _host?.GetModuleSettings(Id).GetBool(option, true) != false;

    // ═══════════════════════ 裝置 ═══════════════════════

    /// <summary>
    /// USB 裝置稍等一下再顯示：如果其實是隨身碟，磁碟機的提示（含剩餘空間）會隨後出現，就不必再顯示一次。
    /// </summary>
    private void ShowUsbLater(string name, bool plugged)
    {
        if (!Enabled(UsbOption)) return;
        var heardAt = DateTime.Now;
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => OnUi(() =>
        {
            if ((_lastDriveEvent - heardAt).Duration() < TimeSpan.FromSeconds(4)) return;
            ShowDevice(GlyphUsb, plugged ? $"{name} 已接上" : $"{name} 已移除",
                plugged ? "USB 裝置 · 已接上" : "USB 裝置 · 已移除",
                plugged ? "TianQingBrush" : "TextSecondaryBrush");
        }), TaskScheduler.Default);
    }

    private void ShowDrive(DriveSnapshot drive, bool arrived)
    {
        _lastDriveEvent = DateTime.Now;
        if (!Enabled(DrivesOption)) return;

        string name = string.IsNullOrWhiteSpace(drive.Label) ? "隨身碟" : drive.Label;
        ShowDevice(GlyphUsb,
            arrived ? $"{name} 已接上" : $"{name} 已移除",
            arrived
                ? $"{drive.Letter} · 剩餘 {DriveDiff.FormatBytes(drive.FreeBytes)} / {DriveDiff.FormatBytes(drive.TotalBytes)}"
                : drive.Letter,
            "MiaoJinBrush");
    }

    private void ShowBluetooth(string name, bool connected)
    {
        if (!Enabled(BluetoothOption)) return;
        ShowDevice(GlyphBluetooth, name, connected ? "藍牙 · 已連線" : "藍牙 · 已中斷連線",
            connected ? "TianQingBrush" : "TextSecondaryBrush");
    }

    private void ShowDevice(string glyph, string title, string subtitle, string accentKey)
    {
        if (_host is null || _deviceVm is null) return;

        _deviceVm.Glyph = glyph;
        _deviceVm.Title = title;
        _deviceVm.Subtitle = subtitle;
        _deviceVm.HasLevel = false;
        _deviceVm.LevelText = string.Empty;
        _deviceVm.Accent = FindBrush(accentKey);

        _host.Post(new IslandActivity(Id, title, subtitle, ActivityPriority.Normal, DeviceDuration)
        {
            ExpandedView = _deviceView,
            GlowKey = accentKey,
        });
    }

    // ═══════════════════════ 刻度 ═══════════════════════

    private void ShowVolume(float level, bool muted)
    {
        if (!Enabled(VolumeOption)) return;
        int percent = (int)Math.Round(level * 100);
        ShowLevel(muted ? GlyphMute : GlyphVolume, muted ? "靜音" : "音量", muted ? 0 : level,
            muted ? "—" : Percent(percent), "MiaoJinBrush");
    }

    private void ShowBrightness(int level)
    {
        if (!Enabled(BrightnessOption)) return;
        ShowLevel(GlyphBrightness, "亮度", level / 100.0, Percent(level), "YueBaiBrush");
    }

    /// <summary>連續調整時，同一則提示即時更新數值並延長顯示時間，不會一直重新彈出。</summary>
    private void ShowLevel(string glyph, string title, double level, string levelText, string accentKey)
    {
        if (_host is null || _levelVm is null) return;

        _levelVm.Glyph = glyph;
        _levelVm.Title = title;
        _levelVm.Subtitle = string.Empty;
        _levelVm.HasLevel = true;
        _levelVm.Level = Math.Clamp(level, 0, 1);
        _levelVm.LevelText = levelText;
        _levelVm.Accent = FindBrush(accentKey);

        _host.Post(new IslandActivity(Id, title, levelText, ActivityPriority.Normal, LevelDuration)
        {
            ExpandedView = _levelView,
            GlowKey = string.Empty, // 調音量、亮度時一直閃會很煩
        });
    }

    private static string Percent(int value) => value.ToString(CultureInfo.InvariantCulture) + "%";

    private Brush? FindBrush(string key) => _deviceView?.TryFindResource(key) as Brush;

    public ValueTask DisposeAsync()
    {
        _drives?.Dispose();
        _bluetooth?.Dispose();
        _volume?.Dispose();
        _brightness?.Dispose();
        _privacy?.Dispose();
        _usb?.Dispose();
        return ValueTask.CompletedTask;
    }
}
