using SteamSwitcher.Core.Models;
using SteamSwitcher.ViewModels;
using System.Windows;

namespace SteamSwitcher.Views.Dialogs;

public partial class GameLoginStateDialog : Window
{
    public LoginState? SelectedLoginState { get; private set; }

    public GameLoginStateDialog(string gameName, LoginState? currentState, bool forAccount = false)
    {
        InitializeComponent();
        var options = EditAccountViewModel.LoginStateOptions.Select(item => new LoginStateItem
        {
            Value = item.Value,
            Label = item.Value == -1 ? (forAccount ? "Herdar do padrão global" : "Herdar da conta / padrão global") : item.Label,
            Icon = item.Icon,
            Color = item.Color
        }).ToList();
        DataContext = options;
        GameNameText.Text = gameName;
        PreferenceExplanation.Text = forAccount
            ? "Preferência para entrar nesta conta. Não altera o padrão global nem a sessão atual."
            : "Preferência ao abrir este jogo. A sessão ativa pode ser reutilizada. Não altera o padrão global.";
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
