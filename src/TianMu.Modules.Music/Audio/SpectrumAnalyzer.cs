namespace TianMu.Modules.Music.Audio;

/// <summary>
/// 把一段時域音訊轉成數條「頻譜柱」高度（0~1）。純計算，不碰任何系統 API，方便測試。
///
/// 流程：Hann 視窗 → FFT → 依對數頻率切成 N 段 → 取每段最大振幅 → 轉 dB → 映射到 0~1 → 平滑（快升慢降）。
/// </summary>
public sealed class SpectrumAnalyzer
{
    private const double MinFreq = 50;
    private const double MaxFreq = 14000;
    private const double FloorDb = -60;
    private const double CeilDb = -8;
    private const double TiltDbPerOctave = 3;   // 音樂高頻能量天生較弱，稍微補償讓柱子分布均勻
    private const double ReleasePerSecond = 2.2; // 下降速度（每秒最多下降多少）

    private readonly int _size;
    private readonly double[] _window;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly float[] _smoothed;

    public SpectrumAnalyzer(int fftSize, int bandCount)
    {
        if (fftSize < 64 || (fftSize & (fftSize - 1)) != 0)
            throw new ArgumentException("FFT 大小必須是 2 的次方且至少 64。", nameof(fftSize));

        _size = fftSize;
        BandCount = bandCount;
        _window = new double[fftSize];
        for (int i = 0; i < fftSize; i++)
            _window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
        _re = new double[fftSize];
        _im = new double[fftSize];
        _smoothed = new float[bandCount];
    }

    public int FftSize => _size;
    public int BandCount { get; }

    /// <summary>平滑後的頻譜柱高度（0~1）。</summary>
    public ReadOnlySpan<float> Bands => _smoothed;

    /// <summary>
    /// 以最新的 <see cref="FftSize"/> 個單聲道樣本更新頻譜。
    /// <paramref name="samples"/> 為空代表靜音（柱子會慢慢落下）。
    /// </summary>
    public void Update(ReadOnlySpan<float> samples, int sampleRate, double deltaSeconds)
    {
        Span<float> raw = stackalloc float[BandCount];
        if (samples.Length >= _size && sampleRate > 0)
            ComputeRaw(samples[^_size..], sampleRate, raw);
        else
            raw.Clear();

        float maxDrop = (float)(ReleasePerSecond * Math.Max(0, deltaSeconds));
        for (int i = 0; i < BandCount; i++)
        {
            float current = _smoothed[i];
            float target = raw[i];
            _smoothed[i] = target > current
                ? current + (target - current) * 0.65f            // 快速上升
                : Math.Max(target, current - maxDrop);            // 緩慢落下
        }
    }

    /// <summary>重設為全部 0。</summary>
    public void Reset() => Array.Clear(_smoothed);

    /// <summary>計算未平滑的頻譜（測試用也很方便）。</summary>
    public void ComputeRaw(ReadOnlySpan<float> samples, int sampleRate, Span<float> output)
    {
        for (int i = 0; i < _size; i++)
        {
            _re[i] = samples[i] * _window[i];
            _im[i] = 0;
        }

        Fft.Transform(_re, _im);

        // Hann 視窗的振幅增益是 0.5 → 滿刻度正弦波的峰值約為 N/4
        double norm = _size / 4.0;
        double binHz = (double)sampleRate / _size;
        double maxFreq = Math.Min(MaxFreq, sampleRate / 2.0);
        double ratio = Math.Pow(maxFreq / MinFreq, 1.0 / BandCount);

        for (int b = 0; b < BandCount; b++)
        {
            double lowHz = MinFreq * Math.Pow(ratio, b);
            double highHz = lowHz * ratio;
            int lo = Math.Clamp((int)Math.Floor(lowHz / binHz), 1, _size / 2 - 1);
            int hi = Math.Clamp((int)Math.Ceiling(highHz / binHz), lo + 1, _size / 2);

            double peak = 0;
            for (int k = lo; k < hi; k++)
            {
                double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
                if (mag > peak) peak = mag;
            }

            double db = 20 * Math.Log10(peak / norm + 1e-12);
            double centerHz = Math.Sqrt(lowHz * highHz);
            db += TiltDbPerOctave * Math.Log2(centerHz / 1000.0);

            output[b] = (float)Math.Clamp((db - FloorDb) / (CeilDb - FloorDb), 0, 1);
        }
    }
}

/// <summary>就地（in-place）基 2 快速傅立葉轉換。</summary>
public static class Fft
{
    public static void Transform(double[] re, double[] im)
    {
        int n = re.Length;

        // 位元反轉排列
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        // 蝶形運算
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            double wRe = Math.Cos(angle), wIm = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tRe = re[b] * curRe - im[b] * curIm;
                    double tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    double nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
