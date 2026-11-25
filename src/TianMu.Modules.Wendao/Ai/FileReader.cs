using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TianMu.Modules.Wendao.Ai;

public enum AttachmentKind { Image, Text }

/// <summary>附件：圖片直接送給看得懂圖的模型；其他檔案先在你的電腦上轉成文字再交給 AI。</summary>
public sealed record Attachment(string Name, AttachmentKind Kind, string Text = "", byte[]? Bytes = null, string Mime = "image/png", bool Truncated = false);

/// <summary>把各種檔案轉成文字（純邏輯，方便測試）。</summary>
public static class FileReader
{
    /// <summary>每個檔案最多送多少字（免費模型的上下文有限）。</summary>
    public const int MaxChars = 120_000;

    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

    public static readonly string[] TextExtensions =
    [
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".toml", ".srt", ".vtt",
        ".cs", ".py", ".js", ".ts", ".tsx", ".jsx", ".java", ".kt", ".c", ".h", ".cpp", ".hpp", ".go", ".rs", ".rb", ".php",
        ".swift", ".html", ".htm", ".css", ".scss", ".sql", ".ps1", ".sh", ".bat", ".cmd", ".xaml", ".csproj", ".vue", ".lua", ".r",
    ];

    public static readonly string[] DocumentExtensions = [".pdf", ".docx", ".xlsx", ".pptx"];

    public static bool CanRead(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ImageExtensions.Contains(ext) || TextExtensions.Contains(ext) || DocumentExtensions.Contains(ext);
    }

    public static string DialogFilter =>
        "可以附的檔案|" + string.Join(";", ImageExtensions.Concat(DocumentExtensions).Concat(TextExtensions).Select(e => "*" + e)) +
        "|文件（PDF、Word、Excel、PowerPoint）|*.pdf;*.docx;*.xlsx;*.pptx|圖片|" + string.Join(";", ImageExtensions.Select(e => "*" + e)) +
        "|文字與程式碼|" + string.Join(";", TextExtensions.Select(e => "*" + e));

    /// <summary>讀取檔案；不支援的格式丟出 <see cref="NotSupportedException"/>。圖片的縮小與轉檔由呼叫端處理。</summary>
    public static Attachment Read(string path)
    {
        string name = Path.GetFileName(path);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ImageExtensions.Contains(ext))
            return new Attachment(name, AttachmentKind.Image, Bytes: File.ReadAllBytes(path), Mime: ext is ".jpg" or ".jpeg" ? "image/jpeg" : ext == ".webp" ? "image/webp" : "image/png");

        string text = ext switch
        {
            ".pdf" => ReadPdf(path),
            ".docx" => ReadDocx(path),
            ".xlsx" => ReadXlsx(path),
            ".pptx" => ReadPptx(path),
            _ when TextExtensions.Contains(ext) => ReadText(File.ReadAllBytes(path)),
            _ => throw new NotSupportedException($"還不會讀 {ext} 檔。"),
        };
        text = text.Replace("\r\n", "\n").Trim();
        if (text.Length == 0) throw new InvalidDataException(ext == ".pdf" ? "這個 PDF 讀不出文字（可能是掃描的圖片，可以改用截圖附上）。" : "這個檔案是空的。");
        bool cut = text.Length > MaxChars;
        return new Attachment(name, AttachmentKind.Text, cut ? text[..MaxChars] : text, Truncated: cut);
    }

    /// <summary>UTF-8 優先，讀不通就當成 Big5（台灣常見的舊檔）。</summary>
    public static string ReadText(byte[] bytes)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(950).GetString(bytes);
        }
    }

    private static string ReadPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            string t;
            try { t = ContentOrderTextExtractor.GetText(page); }
            catch (Exception) { t = page.Text; }
            sb.Append("【第 ").Append(page.Number).Append(" 頁】\n").Append(t.Trim()).Append("\n\n");
            if (sb.Length > MaxChars) break;
        }
        return sb.ToString();
    }

    private static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";
        var sb = new StringBuilder();
        foreach (var el in body.Elements())
        {
            if (el is W.Paragraph p) sb.Append(p.InnerText).Append('\n');
            else if (el is W.Table table)
                foreach (var row in table.Elements<W.TableRow>())
                    sb.Append(string.Join(" | ", row.Elements<W.TableCell>().Select(c => c.InnerText.Trim()))).Append('\n');
        }
        return sb.ToString();
    }

    private static string ReadXlsx(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var wb = doc.WorkbookPart;
        if (wb?.Workbook?.Sheets is null) return "";
        var shared = wb.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
        var sb = new StringBuilder();
        foreach (var sheet in wb.Workbook.Sheets.Elements<S.Sheet>())
        {
            if (sheet.Id?.Value is not { } id || wb.GetPartById(id) is not WorksheetPart part) continue;
            sb.Append("【工作表：").Append(sheet.Name?.Value).Append("】\n");
            foreach (var row in part.Worksheet.Descendants<S.Row>())
            {
                var cells = row.Elements<S.Cell>().Select(c => CellText(c, shared)).ToList();
                if (cells.All(string.IsNullOrWhiteSpace)) continue;
                sb.Append(string.Join('\t', cells)).Append('\n');
                if (sb.Length > MaxChars) return sb.ToString();
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string CellText(S.Cell c, List<string> shared)
    {
        string v = c.CellValue?.Text ?? c.InnerText;
        if (c.DataType?.Value == S.CellValues.SharedString && int.TryParse(v, out int i) && i >= 0 && i < shared.Count) return shared[i];
        if (c.DataType?.Value == S.CellValues.InlineString) return c.InlineString?.InnerText ?? "";
        return v;
    }

    private static string ReadPptx(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        var pres = doc.PresentationPart;
        var ids = pres?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>() ?? [];
        var sb = new StringBuilder();
        int n = 0;
        foreach (var sid in ids)
        {
            n++;
            if (sid.RelationshipId?.Value is not { } rel || pres!.GetPartById(rel) is not SlidePart slide) continue;
            sb.Append("【投影片 ").Append(n).Append("】\n");
            foreach (var para in slide.Slide.Descendants<A.Paragraph>())
            {
                string line = string.Concat(para.Descendants<A.Text>().Select(t => t.Text));
                if (line.Trim().Length > 0) sb.Append(line).Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>把文字附件接在問題後面，交給模型。</summary>
    public static string Compose(string question, IEnumerable<Attachment> files)
    {
        var sb = new StringBuilder(question);
        foreach (var f in files.Where(f => f.Kind == AttachmentKind.Text))
        {
            sb.Append("\n\n〔附件：").Append(f.Name).Append(f.Truncated ? "（內容太長，只附前面一部分）" : "").Append("〕\n```\n").Append(f.Text).Append("\n```");
        }
        return sb.ToString();
    }
}
