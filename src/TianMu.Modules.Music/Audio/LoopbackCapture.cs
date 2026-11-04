using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace TianMu.Modules.Music.Audio;

/// <summary>
/// WASAPI 迴路擷取：「聽見」系統正在輸出的聲音（不論來源是 YouTube、Spotify 還是遊戲），
/// 混成單聲道後存進環形緩衝區，供頻譜分析使用。
/// 預設輸出裝置切換（例如藍牙耳機連上）時會自動重新接上。
/// </summary>
internal sealed class LoopbackCapture : IDisposable
{
    private readonly object _gate = new();      // 生命週期（啟動 / 停止）
    private readonly object _ringLock = new();  // 環形緩衝區（擷取執行緒寫、UI 執行緒讀）
    private readonly float[] _ring;
    private int _writePos;
    private long _lastDataTicks;

    private volatile WasapiLoopbackCapture? _capture;
    private MMDeviceEnumerator? _enumerator;
    private AudioDeviceNotifier? _notifier;
    private bool _wanted;
    private bool _disposed;

    public LoopbackCapture(int bufferSize)
    {
        _ring = new float[bufferSize];
    }

    public int SampleRate { get; private set; } = 48000;

    /// <summary>開始或停止擷取（沒在播音樂時停止，省資源）。</summary>
    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _wanted == active) return;
            _wanted = active;
        }

        // 在背景執行緒操作 COM，避免卡住 UI，也避免 NAudio 綁定 UI 的 SynchronizationContext
        _ = Task.Run(() =>
        {
            lock (_gate)
            {
                if (_wanted) StartCore();
                else StopCore();
            }
        });
    }

    /// <summary>
    /// 取出最新的 <paramref name="destination"/>.Length 個樣本。
    /// 若超過 150 毫秒沒有新聲音（WASAPI 在靜音時不送資料），回傳 false。
    /// </summary>
    public bool TryRead(Span<float> destination)
    {
        if (_capture is null) return false;
        lock (_ringLock)
        {
            if (Stopwatch.GetTimestamp() - _lastDataTicks > Stopwatch.Frequency * 0.15) return false;

            int n = Math.Min(destination.Length, _ring.Length);
            int start = (_writePos - n + _ring.Length) % _ring.Length;
            for (int i = 0; i < n; i++)
                destination[i] = _ring[(start + i) % _ring.Length];
            return true;
        }
    }

    // ── 內部 ──────────────────────────────────────────

    private void StartCore()
    {
        if (_capture is not null || _disposed) return;
        try
        {
            if (_enumerator is null)
            {
                _enumerator = new MMDeviceEnumerator();
                _notifier = new AudioDeviceNotifier(Restart);
                _enumerator.RegisterEndpointNotificationCallback(_notifier);
            }

            var capture = new WasapiLoopbackCapture();
            SampleRate = capture.WaveFormat.SampleRate;
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象樂律] 無法啟動音訊擷取：{ex.Message}");
            _capture = null;
        }
    }

    private void StopCore()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null) return;

        capture.DataAvailable -= OnData;
        capture.RecordingStopped -= OnStopped;
        try
        {
            capture.StopRecording();
            capture.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象樂律] 停止音訊擷取時發生錯誤：{ex.Message}");
        }
    }

    /// <summary>重新接上（預設裝置改變或擷取意外中斷時）。</summary>
    private void Restart()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(400); // 等裝置切換穩定
            lock (_gate)
            {
                if (_disposed) return;
                StopCore();
                if (_wanted) StartCore();
            }
        });
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Debug.WriteLine($"[萬象樂律] 音訊擷取中斷：{e.Exception.Message}");
            Restart();
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || e.BytesRecorded == 0) return;

        var format = capture.WaveFormat;
        int channels = Math.Max(1, format.Channels);
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
        int bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample is not (2 or 4)) return;

        int frameBytes = bytesPerSample * channels;
        int frames = e.BytesRecorded / frameBytes;
        var buffer = e.Buffer;

        lock (_ringLock)
        {
            for (int f = 0; f < frames; f++)
            {
                int offset = f * frameBytes;
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int o = offset + c * bytesPerSample;
                    sum += isFloat
                        ? BitConverter.ToSingle(buffer, o)
                        : bytesPerSample == 2
                            ? BitConverter.ToInt16(buffer, o) / 32768f
                            : BitConverter.ToInt32(buffer, o) / 2147483648f;
                }

                _ring[_writePos] = sum / channels;
                _writePos = (_writePos + 1) % _ring.Length;
            }
            _lastDataTicks = Stopwatch.GetTimestamp();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _wanted = false;
            StopCore();
            if (_enumerator is not null && _notifier is not null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_notifier); } catch { /* 收功時忽略 */ }
            }
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }
}

/// <summary>
/// 接收 Windows 音訊裝置變更通知（COM 回呼，必須是 public 類別）。
/// 預設輸出裝置改變時呼叫 <c>onDefaultRenderChanged</c>。
/// </summary>
public sealed class AudioDeviceNotifier(Action onDefaultRenderChanged) : IMMNotificationClient
{
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) onDefaultRenderChanged();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
