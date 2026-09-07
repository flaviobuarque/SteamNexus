using SteamSwitcher.Core.Models;
using SteamSwitcher.ViewModels;
using System.Windows;

namespace SteamSwitcher.Views.Dialogs;

public partial class GameLoginStateDialog : Window
{
    public LoginState? SelectedLoginState { get; private set; }

    public GameLoginStateDialog(string gameName, LoginState? currentState)
    {
        InitializeComponent();
        var options = EditAccountViewModel.LoginStateOptions.Select(item => new LoginStateItem
        {
            Value = item.Value,
            Label = item.Value == -1 ? "Herdar da conta / padrão global" : item.Label,
            Icon = item.Icon,
            Color = item.Color
        }).ToList();
        DataContext = options;
        GameNameText.Text = gameName;
        StateCombo.SelectedItem = options.FirstOrDefault(item =>
            item.Value == (currentState.HasValue ? (int)currentState.Value : -1));
        Loaded += (_, _) =>
        {
            if (Owner is null) return;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var value = (StateCombo.SelectedItem as LoginStateItem)?.Value;
        SelectedLoginState = value is null or -1 ? null : (LoginState)value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
