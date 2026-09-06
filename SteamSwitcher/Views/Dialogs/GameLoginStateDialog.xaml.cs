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
        DataContext = EditAccountViewModel.LoginStateOptions;
        GameNameText.Text = gameName;
        StateCombo.SelectedItem = EditAccountViewModel.LoginStateOptions.FirstOrDefault(item =>
            item.Value == (currentState.HasValue ? (int)currentState.Value : -1));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var value = (StateCombo.SelectedItem as LoginStateItem)?.Value;
        SelectedLoginState = value is null or -1 ? null : (LoginState)value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
