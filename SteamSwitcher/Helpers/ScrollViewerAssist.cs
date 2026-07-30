using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SteamSwitcher.Helpers;

public static class ScrollViewerAssist
{
    public static void Register()
    {
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

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
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