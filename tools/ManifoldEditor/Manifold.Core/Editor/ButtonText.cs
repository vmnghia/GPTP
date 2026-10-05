namespace Manifold.Core.Editor;

/// <summary>stat_txt text as the editor shows it.</summary>
public static class ButtonText
{
    /// <summary>
    /// A button string without its hotkey (the first character) and without the control
    /// codes stat_txt uses for colours and line breaks.
    /// </summary>
    public static string Display(string? buttonString) =>
        string.IsNullOrEmpty(buttonString) ? "" : Clean(buttonString[1..]);

    /// <summary>The string without control codes, trimmed.</summary>
    public static string Clean(string? text) =>
        text is null ? "" : new string(text.Where(c => c >= ' ').ToArray()).Trim();
}
