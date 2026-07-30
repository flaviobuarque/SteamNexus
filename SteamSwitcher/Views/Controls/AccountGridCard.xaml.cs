using System.Windows;
using System.Windows.Input;

namespace SteamSwitcher.Views.Controls;

public partial class AccountGridCard : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SwitchCommandProperty =
        DependencyProperty.Register(nameof(SwitchCommand), typeof(ICommand), typeof(AccountGridCard));

    public static readonly DependencyProperty ForgetCommandProperty =
        DependencyProperty.Register(nameof(ForgetCommand), typeof(ICommand), typeof(AccountGridCard));

    public static readonly DependencyProperty CancelForgetCommandProperty =
        DependencyProperty.Register(nameof(CancelForgetCommand), typeof(ICommand), typeof(AccountGridCard));

    public ICommand? SwitchCommand
    {
        get => (ICommand?)GetValue(SwitchCommandProperty);
        set => SetValue(SwitchCommandProperty, value);
    }

    public ICommand? ForgetCommand
    {
        get => (ICommand?)GetValue(ForgetCommandProperty);
        set => SetValue(ForgetCommandProperty, value);
    }

    public ICommand? CancelForgetCommand
    {
        get => (ICommand?)GetValue(CancelForgetCommandProperty);
        set => SetValue(CancelForgetCommandProperty, value);
    }

    public AccountGridCard() => InitializeComponent();

    public static readonly DependencyProperty EditCommandProperty =
    DependencyProperty.Register(nameof(EditCommand), typeof(ICommand), typeof(AccountGridCard));

    public static readonly DependencyProperty CopySteamIdCommandProperty =
        DependencyProperty.Register(nameof(CopySteamIdCommand), typeof(ICommand), typeof(AccountGridCard));

    public ICommand? EditCommand
    {
        get => (ICommand?)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public ICommand? CopySteamIdCommand
    {
        get => (ICommand?)GetValue(CopySteamIdCommandProperty);
        set => SetValue(CopySteamIdCommandProperty, value);
    }
}