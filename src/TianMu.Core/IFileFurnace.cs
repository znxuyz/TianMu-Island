namespace TianMu.Core;

/// <summary>化形爐能把一批檔案轉成的一種結果（法陣盤上的一格）。</summary>
/// <param name="Id">選項代號，轉換時傳回給 <see cref="IFileFurnace.ConvertAsync"/>。</param>
/// <param name="Label">扇區大字，例如「WEBP」。</param>
/// <param name="Caption">扇區小字，例如「最小」或「首次需下載 92 MB」。</param>
/// <param name="ToneKey">文字顏色（主題色鍵）；null 為月白。</param>
public sealed record FurnaceOption(string Id, string Label, string Caption, string? ToneKey = null);

/// <summary>轉換結果：成品放在暫存資料夾裡，等使用者拖走或放回原檔旁。</summary>
public sealed record FurnaceResult(IReadOnlyList<string> Outputs, string? Error = null)
{
    public bool Succeeded => Error is null && Outputs.Count > 0;
}

/// <summary>
/// 轉檔引擎（由「化形爐」法器提供）。天幕負責畫面：把檔案放進法陣盤的化形爐，
/// 整盤換成 <see cref="OptionsFor"/> 給的格式，選一個就呼叫 <see cref="ConvertAsync"/>，
/// 成品留在陣眼讓使用者拖到想放的地方；沒拖走就交給 <see cref="PlaceBesideSources"/>。
/// </summary>
public interface IFileFurnace
{
    /// <summary>這些檔案能轉成什麼（最多八種，依常用程度排好）；沒有能轉的就回傳空清單。</summary>
    IReadOnlyList<FurnaceOption> OptionsFor(IReadOnlyList<string> files);

    /// <summary>開始轉換（背景執行緒）。進度 0~1。</summary>
    Task<FurnaceResult> ConvertAsync(IReadOnlyList<string> files, string optionId, IProgress<double> progress, CancellationToken cancellationToken);

    /// <summary>使用者沒把成品拖走：搬到原檔旁邊（同名時自動加上編號），回傳實際放好的路徑。</summary>
    IReadOnlyList<string> PlaceBesideSources(IReadOnlyList<string> sources, IReadOnlyList<string> outputs);
}
