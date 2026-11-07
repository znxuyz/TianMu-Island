using System.Diagnostics;
using System.IO;
using TianMu.Core;
using TianMu.Modules.Convert.Engines;
using TianMu.Modules.Convert.Furnace;

namespace TianMu.Modules.Convert;

/// <summary>
/// 法器「化形爐」：轉檔。把檔案拖到天幕上、停半秒叫出法陣盤，放進「化形爐」，整盤換成可轉的格式；
/// 選一個，成品就在陣眼裡等你拖到想放的地方（沒拖走就放回原檔旁邊）。
///   · 圖片：WEBP、PNG、JPG、AVIF、ICO 互轉，縮小、灰階，幾張合成一份 PDF
///   · PDF：每頁轉圖片、合併、拆頁
///   · 影音：MP4、WEBM、GIF、MP3、M4A、720p 等（第一次使用時下載 FFmpeg）
///   · 字幕 SRT ↔ VTT、文字檔修正亂碼（轉 UTF-8）、ZIP 打包與解壓
/// 全部在這台電腦上完成，不上傳任何檔案；原檔不會被改動。
/// </summary>
public sealed class ConvertModule : IIslandModule, IDescribedModule, IFileFurnace
{
    private static readonly TimeSpan KeepStagingFor = TimeSpan.FromDays(1);

    private IIslandHost? _host;
    private string _staging = string.Empty;
    private MediaEngine? _media;
    private readonly Dictionary<string, string> _sourceOf = new(StringComparer.OrdinalIgnoreCase);

    public string Id => "tianmu.convert";
    public string DisplayName => "化形爐";
    public string Description => "把檔案放進法陣盤的化形爐，轉成想要的格式；圖片、PDF、影音、字幕、壓縮檔都行，全程不上傳。";
    public IReadOnlyList<ModuleOption> Options => [];

    public Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        string data = host.GetDataDirectory(Id);
        _staging = Path.Combine(data, "out");
        _media = new MediaEngine(Path.Combine(data, "ffmpeg"));
        CleanStaging();
        host.SetFurnace(this);
        return Task.CompletedTask;
    }

    // ═══════════════════════ IFileFurnace ═══════════════════════

    public IReadOnlyList<FurnaceOption> OptionsFor(IReadOnlyList<string> files) =>
        FurnacePlanner.OptionsFor(files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList(), _media?.IsReady == true);

    public async Task<FurnaceResult> ConvertAsync(IReadOnlyList<string> files, string optionId, IProgress<double> progress, CancellationToken ct)
    {
        if (_media is null) return new FurnaceResult([], "化形爐尚未準備好");
        string outDir = Path.Combine(_staging, DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(outDir);

        try
        {
            var outputs = await Task.Run(() => RunAsync(files, optionId, outDir, progress, ct), ct);
            return new FurnaceResult(outputs);
        }
        catch (OperationCanceledException)
        {
            return new FurnaceResult([], "已取消");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[化形爐] {optionId} 失敗：{ex}");
            return new FurnaceResult([], ex.Message);
        }
    }

    private async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> files, string optionId, string outDir, IProgress<double> progress, CancellationToken ct)
    {
        string[] parts = optionId.Split(':', 2);
        string family = parts[0], op = parts.Length > 1 ? parts[1] : string.Empty;
        var outputs = new List<string>();

        // 一次轉多個檔案時，進度平均分給每一個
        async Task EachAsync(Func<string, IProgress<double>, Task<IEnumerable<string>>> one)
        {
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                int index = i;
                var part = new Progress<double>(p => progress.Report((index + Math.Clamp(p, 0, 1)) / files.Count));
                foreach (string output in await one(files[i], part))
                {
                    outputs.Add(output);
                    _sourceOf[output] = files[i];
                }
                progress.Report((i + 1.0) / files.Count);
            }
        }

        void Whole(string output)
        {
            outputs.Add(output);
            _sourceOf[output] = files[0];
        }

        switch (family)
        {
            case "img" when op == "pdf":
                Whole(ImageEngine.ToPdf(files, outDir, progress));
                break;
            case "img":
                await EachAsync((f, _) => Task.FromResult<IEnumerable<string>>([ImageEngine.Convert(f, outDir, op)]));
                break;
            case "pdf" when op == "merge":
                Whole(PdfEngine.Merge(files, outDir, progress));
                break;
            case "pdf" when op == "split":
                await EachAsync((f, p) => Task.FromResult<IEnumerable<string>>(PdfEngine.Split(f, outDir, p)));
                break;
            case "pdf":
                await EachAsync(async (f, p) => await PdfEngine.ToImagesAsync(f, outDir, jpg: op == "jpg", p, ct));
                break;
            case "zip" when op == "pack":
                Whole(ArchiveEngine.Pack(files, outDir, progress));
                break;
            case "zip":
                await EachAsync((f, _) => Task.FromResult<IEnumerable<string>>([ArchiveEngine.Extract(f, outDir)]));
                break;
            case "sub":
                await EachAsync((f, _) => Task.FromResult<IEnumerable<string>>([TextEngine.ConvertSubtitle(f, outDir, op)]));
                break;
            case "txt":
                await EachAsync((f, _) => Task.FromResult<IEnumerable<string>>([TextEngine.ToUtf8File(f, outDir)]));
                break;
            case "av" or "au":
                // 沒有 FFmpeg 時先下載：下載佔進度的前一半
                bool needDownload = !_media!.IsReady;
                var convertShare = needDownload ? 0.5 : 0.0;
                if (needDownload)
                    await _media.EnsureAsync(new Progress<double>(p => progress.Report(p * 0.5)), ct);
                for (int i = 0; i < files.Count; i++)
                {
                    int index = i;
                    var part = new Progress<double>(p => progress.Report(convertShare + (1 - convertShare) * (index + p) / files.Count));
                    string output = await _media.ConvertAsync(files[i], outDir, op, part, ct);
                    outputs.Add(output);
                    _sourceOf[output] = files[i];
                }
                break;
            default:
                throw new NotSupportedException($"不認得的轉換：{optionId}");
        }

        progress.Report(1);
        return outputs;
    }

    public IReadOnlyList<string> PlaceBesideSources(IReadOnlyList<string> sources, IReadOnlyList<string> outputs)
    {
        var placed = new List<string>();
        foreach (string output in outputs)
        {
            try
            {
                if (!File.Exists(output) && !Directory.Exists(output)) continue; // 已經被拖走（搬移）了
                string source = _sourceOf.TryGetValue(output, out var s) ? s : sources.FirstOrDefault() ?? output;
                string folder = Path.GetDirectoryName(source) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string target = OutputNames.Unique(folder, Path.GetFileName(output), p => File.Exists(p) || Directory.Exists(p));
                if (Directory.Exists(output)) Directory.Move(output, target);
                else File.Move(output, target);
                placed.Add(target);
            }
            catch (Exception ex)
            {
                // 原檔資料夾不能寫（例如光碟、唯讀的網路磁碟）：留在暫存區，至少不會不見
                Debug.WriteLine($"[化形爐] 無法放回原檔旁：{ex.Message}");
                placed.Add(output);
            }
        }
        return placed;
    }

    /// <summary>清掉一天以前的暫存（使用者拖走時是複製，暫存裡的那份留著沒用）。</summary>
    private void CleanStaging()
    {
        try
        {
            if (!Directory.Exists(_staging)) return;
            foreach (string dir in Directory.EnumerateDirectories(_staging))
                if (DateTime.Now - Directory.GetCreationTime(dir) > KeepStagingFor)
                    Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[化形爐] 清理暫存失敗：{ex.Message}");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
