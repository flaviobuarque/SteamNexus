using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SteamSwitcher.Helpers;

/// <summary>Keep interactive cursors consistent across native and themed templates.</summary>
internal static class ClickableCursorAssist
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        foreach (var type in new[] { typeof(ButtonBase), typeof(MenuItem), typeof(TabItem), typeof(Thumb) })
            EventManager.RegisterClassHandler(type, Mouse.QueryCursorEvent,
                new QueryCursorEventHandler(OnQueryCursor), handledEventsToo: true);
    }

    private static void OnQueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (sender is not UIElement control) return;
        e.Cursor = control.IsEnabled ? Cursors.Hand : Cursors.Arrow;
        e.Handled = true;
    }
}
