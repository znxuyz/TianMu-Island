namespace TianMu.Core;

/// <summary>天幕上的一盞小燈。</summary>
/// <param name="Glyph">Segoe Fluent Icons 字元，例如麥克風 ""、相機 ""。</param>
/// <param name="Label">滑鼠停留時的說明，例如「麥克風使用中 · Zoom」。</param>
/// <param name="ToneKey">顏色資源鍵（設計語言 Tokens.xaml 中的鍵），例如 "ZhuShaBrush"。</param>
public sealed record IslandIndicator(string Glyph, string Label, string ToneKey = "ZhuShaBrush");
