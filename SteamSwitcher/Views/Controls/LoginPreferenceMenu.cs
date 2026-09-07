using SteamSwitcher.Core.Models;
using SteamSwitcher.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace SteamSwitcher.Views.Controls;

/// <summary>Shared anchored picker for account and game preference badges.</summary>
public static class LoginPreferenceMenu
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(LoginPreferenceMenu), new PropertyMetadata(null, OnCommandChanged));
    public static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(LoginState?), typeof(LoginPreferenceMenu));
    public static readonly DependencyProperty InheritedTextProperty = DependencyProperty.RegisterAttached(
        "InheritedText", typeof(string), typeof(LoginPreferenceMenu), new PropertyMetadata(string.Empty));
    public static ICommand? GetCommand(DependencyObject target) => (ICommand?)target.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject target, ICommand? value) => target.SetValue(CommandProperty, value);
    public static LoginState? GetState(DependencyObject target) => (LoginState?)target.GetValue(StateProperty);
    public static void SetState(DependencyObject target, LoginState? value) => target.SetValue(StateProperty, value);
    public static string GetInheritedText(DependencyObject target) => (string)target.GetValue(InheritedTextProperty);
    public static void SetInheritedText(DependencyObject target, string value) => target.SetValue(InheritedTextProperty, value);

    private static void OnCommandChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Button button) return;
        button.Click -= OpenMenu;
        if (args.NewValue is null) return;
        button.Click += OpenMenu;
        button.SetResourceReference(FrameworkElement.StyleProperty, "LoginPreferenceBadgeStyle");
        var text = new TextBlock { Margin = new Thickness(0, 3, 0, 0) };
        text.SetBinding(TextBlock.TextProperty, new Binding("DataContext.LoginPreferenceTooltip") { Source = button });
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Status ao entrar", FontWeight = FontWeights.SemiBold });
        content.Children.Add(text);
        button.ToolTip = new ToolTip { Content = content };
        ToolTipService.SetInitialShowDelay(button, 450);
        System.Windows.Automation.AutomationProperties.SetName(button, "Alterar status ao entrar");
    }

    private static void OpenMenu(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || GetCommand(button) is not { } command || button.DataContext is null) return;
        args.Handled = true;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
        menu.SetResourceReference(FrameworkElement.StyleProperty, "LoginPreferenceContextMenuStyle");
        foreach (var state in new LoginState?[] { null, LoginState.Online, LoginState.Offline })
        {
            var selection = new LoginPreferenceSelection(button.DataContext, state);
            object header = LoginPreferencePresentation.StateName(state);
            if (!state.HasValue)
            {
                var description = new TextBlock { Text = GetInheritedText(button), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
                description.SetResourceReference(TextBlock.ForegroundProperty, "AppTextMutedBrush");
                var label = new StackPanel();
                label.Children.Add(new TextBlock { Text = "Padrão" });
                label.Children.Add(description);
                header = label;
            }
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = GetState(button) == state,
                Command = command,
                CommandParameter = selection
            };
            item.SetResourceReference(FrameworkElement.StyleProperty, "LoginPreferenceMenuItemStyle");
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => button.Focus();
        menu.IsOpen = true;
    }
}
