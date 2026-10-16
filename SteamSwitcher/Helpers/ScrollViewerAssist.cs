using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SteamSwitcher.Helpers;

public static class ScrollViewerAssist
{
    private static bool _registered;
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        var source = e.OriginalSource as DependencyObject;
        var scrollViewer = FindScrollViewer(source);
        if (scrollViewer == null) return;

        // Logical panels interpret offsets as items, not pixels. Let WPF handle
        // these natively (including the user's wheel-line setting).
        if (scrollViewer.CanContentScroll) return;
        if (e.Delta == 0) return;
        if (SystemParameters.WheelScrollLines == -1)
        {
            if (e.Delta > 0) scrollViewer.PageUp(); else scrollViewer.PageDown();
        }
        else scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset
            - e.Delta / 120d * SystemParameters.WheelScrollLines * 16);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? obj)
    {
        while (obj is not null)
        {
            if (obj is ScrollViewer sv && sv.ScrollableHeight > 0)
                return sv;

            // VisualTreeHelper.GetParent só funciona em Visual/Visual3D
            // ContentElement (ex: Run, TextElement) usa outro método
            obj = obj is Visual or Visual3D
                ? VisualTreeHelper.GetParent(obj)
                : LogicalTreeHelper.GetParent(obj);
        }
        return null;
    }
}
