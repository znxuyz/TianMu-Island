using TianMu.Modules.Music.Audio;
using TianMu.Modules.Music.Lyrics;

namespace TianMu.Tests;

public class SpectrumAnalyzerTests
{
    private const int SampleRate = 48000;
    private const int Size = 2048;

    private static float[] Sine(double freq, double amplitude = 0.5)
    {
        var buffer = new float[Size];
        for (int i = 0; i < Size; i++)
            buffer[i] = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / SampleRate));
        return buffer;
    }

    [Fact]
    public void Fft_finds_the_right_bin()
    {
        var re = new double[64];
        var im = new double[64];
        for (int i = 0; i < 64; i++) re[i] = Math.Cos(2 * Math.PI * 5 * i / 64);

        Fft.Transform(re, im);

        int peak = Enumerable.Range(0, 32).MaxBy(k => re[k] * re[k] + im[k] * im[k]);
        Assert.Equal(5, peak);
    }

    [Fact]
    public void Silence_produces_zero_bands()
    {
        var analyzer = new SpectrumAnalyzer(Size, 24);
        var output = new float[24];

        analyzer.ComputeRaw(new float[Size], SampleRate, output);

        Assert.All(output, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Low_tone_lights_low_bands_more_than_high_bands()
    {
        var analyzer = new SpectrumAnalyzer(Size, 24);
        var output = new float[24];

        analyzer.ComputeRaw(Sine(120), SampleRate, output);

        Assert.True(output.Take(8).Max() > 0.5f, "低頻柱應該明顯亮起");
        Assert.True(output.Skip(16).Max() < output.Take(8).Max(), "高頻柱應該比低頻柱低");
    }

    [Fact]
    public void Bands_fall_slowly_after_sound_stops()
    {
        var analyzer = new SpectrumAnalyzer(Size, 24);
        analyzer.Update(Sine(440), SampleRate, 1.0 / 60);
        analyzer.Update(Sine(440), SampleRate, 1.0 / 60);
        float peak = analyzer.Bands.ToArray().Max();

        analyzer.Update(ReadOnlySpan<float>.Empty, 0, 1.0 / 60);
        float after = analyzer.Bands.ToArray().Max();

        Assert.True(after > 0 && after < peak, "柱子應該緩慢下降，而不是瞬間歸零");
    }

    [Fact]
    public void Lyrics_pick_prefers_synced_and_closest_duration()
    {
        var records = new List<LrclibRecord>
        {
            new(1, "a", "x", null, 200, false, "plain", null),
            new(2, "a", "x", null, 260, false, "plain", "[00:01.00]far"),
            new(3, "a", "x", null, 201, false, "plain", "[00:01.00]close"),
        };

        var best = LyricsService.PickBest(records, TimeSpan.FromSeconds(200));

        Assert.NotNull(best);
        Assert.Equal(3L, best!.Id);
    }
}
