using System.IO;
using System.IO.Compression;
using TianMu.Modules.Convert.Furnace;

namespace TianMu.Modules.Convert.Engines;

/// <summary>ZIP 打包與解壓（.NET 內建）。</summary>
internal static class ArchiveEngine
{
    public static string Pack(IReadOnlyList<string> sources, string outDir, IProgress<double> progress)
    {
        string name = sources.Count > 1 ? OutputNames.For(sources[0], "zip", $"等{sources.Count}個") : OutputNames.For(sources[0], "zip");
        string path = OutputNames.Unique(outDir, name, File.Exists);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        for (int i = 0; i < sources.Count; i++)
        {
            string src = sources[i];
            if (Directory.Exists(src))
            {
                string root = Path.GetFileName(src.TrimEnd('\\', '/'));
                foreach (string f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(f, Path.Combine(root, Path.GetRelativePath(src, f)).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            else
            {
                zip.CreateEntryFromFile(src, Path.GetFileName(src), CompressionLevel.Optimal);
            }
            progress.Report((i + 1.0) / sources.Count);
        }
        return path;
    }

    /// <summary>解壓到同名資料夾（.NET 會擋掉指向資料夾外面的路徑）。</summary>
    public static string Extract(string source, string outDir)
    {
        string folder = OutputNames.Unique(outDir, Path.GetFileNameWithoutExtension(source), p => Directory.Exists(p) || File.Exists(p));
        ZipFile.ExtractToDirectory(source, folder);
        return folder;
    }
}
