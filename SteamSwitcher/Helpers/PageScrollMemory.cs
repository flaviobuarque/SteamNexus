using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SteamSwitcher.Helpers;

/// <summary>Session-only viewport state. Filters remain owned by the existing view models.</summary>
internal sealed class PageScrollMemory
{
    private readonly Dictionary<ScrollViewer, double> _offsets = [];
    public void Reset(DependencyObject root)
    {
        _offsets.Clear();
        foreach (var viewer in Find(root)) viewer.ScrollToTop();
    }
    public void Capture(DependencyObject root)
    {
        _offsets.Clear();
        foreach (var viewer in Find(root))
            if (viewer.Visibility == Visibility.Visible) _offsets[viewer] = viewer.VerticalOffset;
    }

    public async Task RestoreAsync(FrameworkElement page)
    {
        await page.Dispatcher.InvokeAsync(() =>
        {
            if (!page.IsLoaded) return;
            foreach (var viewer in Find(page))
                if (_offsets.TryGetValue(viewer, out var offset)) viewer.ScrollToVerticalOffset(offset);
        }, DispatcherPriority.Loaded);
    }

    private static IEnumerable<ScrollViewer> Find(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) yield return viewer;
            foreach (var nested in Find(child)) yield return nested;
        }
    }
}
