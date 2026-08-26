using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamSwitcher.Core.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class DiagnosticsViewModel(
    ISteamDiagnosticsService diagnosticsService,
    ISnackbarService snackbarService,
    MainViewModel mainViewModel) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<DiagnosticInstallationItem> _installations = [];
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _lastCheckText = "Ainda não verificado";
    private SteamDiagnosticReport? _report;

    public async Task InitializeAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        try
        {
            var expandedIds = Installations.Where(item => item.IsExpanded)
                .Select(item => item.Report.InstallationId)
                .ToHashSet(StringComparer.Ordinal);
            _report = await diagnosticsService.CheckAsync();
            Installations = new ObservableCollection<DiagnosticInstallationItem>(
                _report.Installations.Select((report, index) => new DiagnosticInstallationItem(
                    report,
                    expandedIds.Count > 0
                        ? expandedIds.Contains(report.InstallationId)
                        : report.IsSelected || report.IsRunning || index == 0,
                    $"Verificado às {_report.CheckedAt:HH:mm:ss}")));
            LastCheckText = $"Verificado às {_report.CheckedAt:HH:mm:ss}";
            mainViewModel.UpdateStatusBar(
                _report.HasBlockingIssues ? "Diagnóstico encontrou problemas" : "Diagnóstico concluído",
                $"{Installations.Count} instalação(ões)");
        }
        catch (Exception ex)
        {
            snackbarService.Show("Falha no diagnóstico", ex.Message,
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
        }
        finally { IsChecking = false; }
    }

    [RelayCommand]
    private async Task DisableChooserAsync(DiagnosticInstallationItem item)
    {
        try
        {
            await diagnosticsService.DisableAccountChooserAsync(item.Report.InstallationId);
            snackbarService.Show("Seletor corrigido", $"Corrigido em {item.Report.InstallationName}.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            snackbarService.Show("Não foi possível corrigir", ex.Message,
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private async Task RepairRegistryAsync(DiagnosticInstallationItem item)
    {
        try
        {
            await diagnosticsService.RepairRegistryAsync(item.Report.InstallationId);
            snackbarService.Show("Registro corrigido", "O autologin corresponde à conta ativa.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(4));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            snackbarService.Show("Não foi possível corrigir", ex.Message,
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (_report is null) return;
        var text = new StringBuilder()
            .AppendLine("SteamNexus — Diagnóstico da Steam")
            .AppendLine($"Data: {_report.CheckedAt:yyyy-MM-dd HH:mm:ss}");
        foreach (var installation in _report.Installations)
        {
            text.AppendLine()
                .AppendLine($"## {installation.InstallationName}")
                .AppendLine($"Diretório: {installation.InstallationPath}")
                .AppendLine($"Selecionada: {installation.IsSelected}")
                .AppendLine($"Em execução: {installation.RunningSteamPath}")
                .AppendLine($"Conta ativa: {installation.ActiveAccountName}");
            foreach (var diagnostic in installation.Items)
                text.AppendLine($"[{diagnostic.Severity}] {diagnostic.Title}: {diagnostic.Detail}");
        }

        Clipboard.SetText(text.ToString());
        snackbarService.Show("Diagnósticos copiados", "O relatório de todas as instalações foi copiado.",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
    }

    [RelayCommand]
    private void CopyInstallationReport(DiagnosticInstallationItem item)
    {
        var installation = item.Report;
        var text = new StringBuilder()
            .AppendLine("SteamNexus — Diagnóstico da Steam")
            .AppendLine($"Data: {_report?.CheckedAt:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Instalação: {installation.InstallationName}")
            .AppendLine($"Diretório: {installation.InstallationPath}")
            .AppendLine($"Selecionada: {installation.IsSelected}")
            .AppendLine($"Em execução: {installation.RunningSteamPath}")
            .AppendLine($"Conta ativa: {installation.ActiveAccountName}");
        foreach (var diagnostic in installation.Items)
            text.AppendLine($"[{diagnostic.Severity}] {diagnostic.Title}: {diagnostic.Detail}");

        Clipboard.SetText(text.ToString());
        snackbarService.Show("Diagnóstico copiado", $"Relatório de {installation.InstallationName} copiado.",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
    }
}

public partial class DiagnosticInstallationItem(
    SteamInstallationDiagnosticReport report,
    bool isExpanded,
    string checkedAtText) : ObservableObject
{
    public SteamInstallationDiagnosticReport Report { get; } = report;
    public string CheckedAtText { get; } = checkedAtText;
    [ObservableProperty] private bool _isExpanded = isExpanded;
}
