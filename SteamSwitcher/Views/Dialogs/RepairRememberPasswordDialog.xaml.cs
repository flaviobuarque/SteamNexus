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
    private CancellationTokenSource? _countdown;
    public int RepairedCount { get; private set; }

    public RepairRememberPasswordDialog(ISteamDiagnosticsService service, SteamInstallationDiagnosticReport report)
    {
        InitializeComponent();
        _service = service;
        _installationId = report.InstallationId;
        _accounts = report.AccountsWithoutRememberPassword.Select(account => new AccountSelection(account)).ToArray();
        AccountList.ItemsSource = _accounts;
        Summary.Text = $"{report.InstallationName} · {_accounts.Length} conta(s) com RememberPassword=0.\nSelecione as contas que deseja corrigir ou corrija todas as listadas.";
        Feedback.Text = "A Steam será fechada após uma contagem de 5 segundos, que você poderá cancelar. Será criado um backup. Credenciais expiradas ainda podem exigir login.";
        Closing += (_, e) => e.Cancel = _busy;
        Closed += (_, _) => _countdown?.Cancel();
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
        if (_busy || _countdown is not null) return;
        var ids = _accounts.Where(account => all || account.IsSelected).Select(account => account.Account.SteamId64).ToArray();
        if (ids.Length == 0) { Feedback.Text = "Selecione pelo menos uma conta."; return; }
        using var countdown = new CancellationTokenSource();
        _countdown = countdown;
        SelectedButton.IsEnabled = AllButton.IsEnabled = AccountList.IsEnabled = false;
        try
        {
            for (var seconds = 5; seconds > 0; seconds--)
            {
                Feedback.Text = $"A Steam será fechada em {seconds}s para corrigir {ids.Length} conta(s). Você pode cancelar.";
                await Task.Delay(1000, countdown.Token);
            }
            countdown.Token.ThrowIfCancellationRequested();
            _busy = true;
            CancelButton.Visibility = SelectedButton.Visibility = AllButton.Visibility = Visibility.Collapsed;
            Feedback.Text = "Verificando jogos, fechando a Steam e corrigindo as contas…";
            RepairedCount = await _service.RepairRememberPasswordAsync(_installationId, ids);
            _busy = false;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (countdown.IsCancellationRequested) { }
        catch (Exception ex) { Feedback.Text = ex.Message; }
        finally
        {
            _busy = false;
            _countdown = null;
            CancelButton.Visibility = SelectedButton.Visibility = AllButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = SelectedButton.IsEnabled = AllButton.IsEnabled = AccountList.IsEnabled = true;
        }
    }
}
