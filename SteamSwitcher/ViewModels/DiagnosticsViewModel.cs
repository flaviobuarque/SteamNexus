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
    [ObservableProperty] private ObservableCollection<SteamDiagnosticItem> _items = [];
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _installationName = "Nenhuma instalação";
    [ObservableProperty] private string _installationPath = string.Empty;
    [ObservableProperty] private string _lastCheckText = "Ainda não verificado";
    [ObservableProperty] private bool _canDisableChooser;
    [ObservableProperty] private bool _canRepairRegistry;
    private SteamDiagnosticReport? _report;

    public async Task InitializeAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        try
        {
            _report = await diagnosticsService.CheckAsync();
            Items = new ObservableCollection<SteamDiagnosticItem>(_report.Items);
            InstallationName = string.IsNullOrWhiteSpace(_report.InstallationName)
                ? "Nenhuma instalação" : _report.InstallationName;
            InstallationPath = _report.InstallationPath;
            LastCheckText = $"Verificado às {_report.CheckedAt:HH:mm:ss}";
            CanDisableChooser = _report.CanDisableChooser;
            CanRepairRegistry = _report.CanRepairRegistry;
            mainViewModel.UpdateStatusBar(
                _report.HasBlockingIssues ? "Diagnóstico encontrou problemas" : "Diagnóstico concluído",
                $"{Items.Count} verificações");
        }
        catch (Exception ex)
        {
            snackbarService.Show("Falha no diagnóstico", ex.Message,
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task DisableChooserAsync()
    {
        try
        {
            await diagnosticsService.DisableAccountChooserAsync();
            snackbarService.Show("Seletor corrigido", "O seletor obrigatório de contas foi desativado.",
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
    private async Task RepairRegistryAsync()
    {
        try
        {
            await diagnosticsService.RepairRegistryAsync();
            snackbarService.Show("Registro corrigido", "O autologin agora corresponde à conta ativa.",
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
            .AppendLine($"Data: {_report.CheckedAt:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Instalação: {_report.InstallationName}")
            .AppendLine($"Diretório: {_report.InstallationPath}")
            .AppendLine($"Steam em execução: {_report.RunningSteamPath}")
            .AppendLine($"Conta ativa: {_report.ActiveAccountName}");
        foreach (var item in _report.Items)
            text.AppendLine($"[{item.Severity}] {item.Title}: {item.Detail}");
        Clipboard.SetText(text.ToString());
        snackbarService.Show("Diagnóstico copiado", "O relatório foi copiado para a área de transferência.",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
    }
}
