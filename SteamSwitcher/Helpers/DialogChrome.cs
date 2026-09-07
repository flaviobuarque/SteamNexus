using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

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
        var close = new Wpf.Ui.Controls.Button { Content = "×", Width = 36, Height = 28,
            Padding = new Thickness(0), ToolTip = "Fechar", Cursor = Cursors.Hand };
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
    }
}
