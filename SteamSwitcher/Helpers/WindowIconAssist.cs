using System.Windows;
using System.Windows.Media.Imaging;

namespace SteamSwitcher.Helpers;

internal static class WindowIconAssist
{
    private static bool _registered;
    private static BitmapFrame? _icon;

    public static void Register()
    {
        if (_registered) return;
        _icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico"));
        _icon.Freeze();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
        _registered = true;
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window && window.Icon is null)
            window.SetCurrentValue(Window.IconProperty, _icon);
    }
}
