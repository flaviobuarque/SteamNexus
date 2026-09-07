using SteamSwitcher.Core.Services;
using System.Windows;

namespace SteamSwitcher.Views.Dialogs;

public partial class RepairRememberPasswordDialog : Window
{
    public sealed class AccountSelection(RememberPasswordAccount account)
    {
        public RememberPasswordAccount Account { get; } = account;
        public bool IsSelected { get; set; }
    }

    private readonly ISteamDiagnosticsService _service;
    private readonly string _installationId;
    private readonly AccountSelection[] _accounts;
    private bool _busy;
    public int RepairedCount { get; private set; }

    public RepairRememberPasswordDialog(ISteamDiagnosticsService service, SteamInstallationDiagnosticReport report)
    {
        InitializeComponent();
        _service = service;
        _installationId = report.InstallationId;
        _accounts = report.AccountsWithoutRememberPassword.Select(account => new AccountSelection(account)).ToArray();
        AccountList.ItemsSource = _accounts;
        Summary.Text = $"{report.InstallationName} · {_accounts.Length} conta(s) com RememberPassword=0.\nSelecione as contas que deseja corrigir ou corrija todas as listadas.";
        Feedback.Text = "Feche a Steam antes de corrigir. Será criado um backup. Alterar para 1 não recupera credenciais expiradas; a Steam ainda pode solicitar login.";
        Closing += (_, e) => e.Cancel = _busy;
        Loaded += (_, _) =>
        {
            if (Owner is null) return;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private async void Selected_Click(object sender, RoutedEventArgs e) => await RepairAsync(false);
    private async void All_Click(object sender, RoutedEventArgs e) => await RepairAsync(true);

    private async Task RepairAsync(bool all)
    {
        if (_busy) return;
        var ids = _accounts.Where(account => all || account.IsSelected).Select(account => account.Account.SteamId64).ToArray();
        if (ids.Length == 0) { Feedback.Text = "Selecione pelo menos uma conta."; return; }
        _busy = true;
        CancelButton.IsEnabled = SelectedButton.IsEnabled = AllButton.IsEnabled = AccountList.IsEnabled = false;
        try
        {
            RepairedCount = await _service.RepairRememberPasswordAsync(_installationId, ids);
            _busy = false;
            DialogResult = true;
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
        finally
        {
            _busy = false;
            CancelButton.IsEnabled = SelectedButton.IsEnabled = AllButton.IsEnabled = AccountList.IsEnabled = true;
        }
    }
}
