using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ManifoldEditor.App;

/// <summary>The window's ContentDialogs: one shows at a time, and while one shows the keys are its own.</summary>
static class ContentDialogs
{
    /// <summary>
    /// Whether a ContentDialog shows over <paramref name="root"/>: a second one can't open, and the
    /// window's shortcuts must leave its keys to it (Ctrl+Z in its text boxes, not an undo behind it).
    /// </summary>
    public static bool Open(XamlRoot? root) =>
        root is not null && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(p => p.Child is ContentDialog);
}
