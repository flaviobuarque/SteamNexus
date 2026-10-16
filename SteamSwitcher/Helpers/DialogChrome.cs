using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace SteamSwitcher.Helpers;

internal static class DialogChrome
{
    public static void Apply(Window window)
    {
        var content = window.Content;
        window.Content = null;
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 5)
        });
        var root = new DockPanel();
        var header = new Grid { Height = 32 };
        header.SetResourceReference(Panel.BackgroundProperty, "AppChromeBrush");
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = window.Title, Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondaryBrush");
        header.Children.Add(title);
        var close = new Button { Content = "×", Width = 36, Height = 28, MinHeight = 28,
            Padding = new Thickness(0), ToolTip = "Fechar", Cursor = Cursors.Hand };
        close.SetResourceReference(FrameworkElement.StyleProperty, "NexusActionButton");
        close.SetResourceReference(Control.BackgroundProperty, "AppChromeBrush");
        close.SetResourceReference(Control.ForegroundProperty, "AppTextPrimaryBrush");
        System.Windows.Automation.AutomationProperties.SetName(close, "Fechar");
        close.Click += (_, _) => window.Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled || close.IsMouseOver) return;
            if (e.ClickCount == 2 && window.ResizeMode != ResizeMode.NoResize)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ContentPresenter { Content = content });
        var frame = new Border { BorderThickness = new Thickness(1), Child = root };
        frame.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        frame.SetResourceReference(Border.BackgroundProperty, "AppBackgroundBrush");
        window.Content = frame;
        // Constrain to the current monitor's work area; keep the existing resize policy.
        window.SourceInitialized += (_, _) =>
        {
            var work = GetWorkArea(window);
            window.MaxWidth = Math.Max(320, work.Width);
            window.MaxHeight = Math.Max(240, work.Height);
            window.MinWidth = Math.Min(window.MinWidth, window.MaxWidth);
            window.MinHeight = Math.Min(window.MinHeight, window.MaxHeight);
            if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, window.MaxWidth);
            if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height, window.MaxHeight);
        };
    }

    private static Rect GetWorkArea(Window window)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(window.Owner ?? window).Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(0, 0, (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
