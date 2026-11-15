using System.IO;
using System.Windows;
using TianMu.Modules.Wenfang.Sheet;
using TianMu.Modules.Wenfang.Slides;
using TianMu.Modules.Wenfang.Writer;

namespace TianMu.Modules.Wenfang.Shared;

/// <summary>文房的總管：依副檔名決定用哪個編輯器開、開新文件、記住最近檔案。</summary>
internal static class WenfangHub
{
    public enum Kind { Writer, Sheet, Slides }

    public static RecentFiles Recent { get; private set; } = new(null);

    public static void Configure(string? dataDirectory) => Recent = new RecentFiles(dataDirectory);

    public static readonly string[] WriterExtensions = [".docx", ".rtf", ".txt", ".md"];
    public static readonly string[] SheetExtensions = [".xlsx", ".xlsm", ".csv"];
    public static readonly string[] SlidesExtensions = [".pptx"];

    /// <summary>這個檔案該用哪個編輯器（不認得回傳 null）。</summary>
    public static Kind? KindOf(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (WriterExtensions.Contains(ext)) return Kind.Writer;
        if (SheetExtensions.Contains(ext)) return Kind.Sheet;
        if (SlidesExtensions.Contains(ext)) return Kind.Slides;
        return null;
    }

    public static bool CanOpen(string path) => KindOf(path) is not null;

    public static void New(Kind kind) => Create(kind).Show();

    /// <summary>開檔：已經開著就切過去，否則開一個新視窗。</summary>
    public static void Open(string path)
    {
        if (KindOf(path) is not { } kind)
        {
            MessageBox.Show($"文房還不會開這種檔案：{Path.GetExtension(path)}\n\n可以開：Word（.docx .rtf .txt）、Excel（.xlsx .csv）、PowerPoint（.pptx）。", "文房");
            return;
        }
        var existing = Application.Current.Windows.OfType<EditorWindow>()
            .FirstOrDefault(w => string.Equals(w.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var window = Create(kind);
        if (window.TryLoad(path)) window.Show();
        else window.Close();
    }

    private static EditorWindow Create(Kind kind) => kind switch
    {
        Kind.Sheet => new SheetWindow(),
        Kind.Slides => new SlidesWindow(),
        _ => new WriterWindow(),
    };
}
