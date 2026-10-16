using SteamSwitcher.Core.Services;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Controls;

namespace SteamSwitcher.Views.Dialogs;

public partial class RepairRememberPasswordDialog : Window
{
    public sealed class AccountSelection(RememberPasswordAccount account) : ObservableObject
    {
        public RememberPasswordAccount Account { get; } = account;
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    private readonly ISteamDiagnosticsService _service;
    private readonly string _installationId;
    private readonly string _installationName;
    private readonly AccountSelection[] _accounts;
    private bool _busy;
    private CancellationTokenSource? _countdown;
    public int RepairedCount { get; private set; }

    public RepairRememberPasswordDialog(ISteamDiagnosticsService service, SteamInstallationDiagnosticReport report)
    {
        InitializeComponent();
        _service = service;
        _installationId = report.InstallationId;
        _installationName = report.InstallationName;
        _accounts = report.AccountsWithoutRememberPassword.Select(account => new AccountSelection(account)).ToArray();
        foreach (var account in _accounts) account.PropertyChanged += (_, _) => UpdateSelection();
        AccountList.ItemsSource = _accounts;
        Summary.Text = $"Steam {report.InstallationName} · {_accounts.Length} conta(s) sem a opção de lembrar senha.\nEscolha quais deseja corrigir.";
        Summary.ToolTip = "Estas contas têm RememberPassword=0 no loginusers.vdf.";
        Feedback.Text = "A Steam será fechada após 5 segundos e reaberta na última conta após a correção. Você poderá cancelar durante a contagem.";
        UpdateSelection();
        Closing += (_, e) => e.Cancel = _busy;
        Closed += (_, _) => _countdown?.Cancel();
        Loaded += (_, _) =>
        {
            if (Owner is null) return;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            DialogCard.MaxHeight = Math.Max(200, Height - 40);
            Left = Owner.Left;
            Top = Owner.Top;
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void UpdateSelection()
    {
        var count = _accounts.Count(account => account.IsSelected);
        SelectionSummary.Text = $"{count} de {_accounts.Length} selecionada(s)";
        SelectedButton.Content = count > 0 ? $"Corrigir selecionados ({count})" : "Corrigir selecionados";
        SelectedButton.IsEnabled = count > 0 && !_busy && _countdown is null;
    }
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
        RepairProgress.Visibility = Visibility.Visible;
        RepairProgress.IsIndeterminate = false;
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "AppWarningBrush");
        try
        {
            for (var seconds = 5; seconds > 0; seconds--)
            {
                Feedback.Text = $"A Steam será fechada em {seconds}s para corrigir {ids.Length} conta(s). Você pode cancelar.";
                RepairProgress.Value = 6 - seconds;
                await Task.Delay(1000, countdown.Token);
            }
            countdown.Token.ThrowIfCancellationRequested();
            _busy = true;
            CancelButton.Visibility = SelectedButton.Visibility = AllButton.Visibility = Visibility.Collapsed;
            Feedback.Text = "Corrigindo as contas e restaurando a última sessão da Steam…";
            Feedback.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondaryBrush");
            RepairProgress.IsIndeterminate = true;
            RepairedCount = await _service.RepairRememberPasswordAsync(_installationId, ids);
            ActivityJournal.Shared.Record(ActivityKind.PreferenceRepaired, operation: $"Lembrar senha (RememberPassword) — {RepairedCount} conta(s) atualizada(s)",
                installation: _installationName, accounts: _accounts.Where(a => ids.Contains(a.Account.SteamId64)).Select(a => $"{a.Account.DisplayName} (@{a.Account.AccountName})").ToArray());
            _busy = false;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (countdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Feedback.Text = ex.Message;
            ActivityJournal.Shared.Record(ActivityKind.RepairFailed, operation: "Lembrar senha (RememberPassword)",
                installation: _installationName, accounts: _accounts.Where(a => ids.Contains(a.Account.SteamId64)).Select(a => $"{a.Account.DisplayName} (@{a.Account.AccountName})").ToArray());
            Feedback.SetResourceReference(TextBlock.ForegroundProperty, "AppDangerBrush");
        }
        finally
        {
            _busy = false;
            _countdown = null;
            CancelButton.Visibility = SelectedButton.Visibility = AllButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = SelectedButton.IsEnabled = AllButton.IsEnabled = AccountList.IsEnabled = true;
            RepairProgress.Visibility = Visibility.Collapsed;
            UpdateSelection();
        }
    }
}
