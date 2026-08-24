using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Helpers;
using System.IO;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class OnboardingViewModel(
    IOnboardingService onboardingService,
    ISteamLocatorService locatorService,
    ISteamAccountService accountService,
    ISnackbarService snackbarService,
    IAppSettingsService settingsService,
    Views.MainWindow mainWindow) : ObservableObject
{
    [ObservableProperty] private int _currentStep = 1;
    [ObservableProperty] private bool _canGoNext = true;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _isLastStep;

    // Step 2 — Avisos
    [ObservableProperty] private bool _warning1Confirmed;
    [ObservableProperty] private bool _warning2Confirmed;
    [ObservableProperty] private bool _warning3Confirmed;

    // Step 3 — Steam
    [ObservableProperty] private bool _steamFound;
    [ObservableProperty] private string _steamPath = string.Empty;
    [ObservableProperty] private bool _isInstallingSteam;
    [ObservableProperty] private int _installProgress;
    [ObservableProperty] private IReadOnlyList<DriveInfo> _availableDrives = [];
    [ObservableProperty] private DriveInfo? _selectedDrive;
    [ObservableProperty] private bool _showDriveSelector;

    [ObservableProperty] private object? _currentStepView;
    [ObservableProperty] private IReadOnlyList<StepItem> _stepItems = [];

    [ObservableProperty] private string _selectedTheme = string.Empty;
    [ObservableProperty] private int _accountsFoundCount;
    [ObservableProperty] private bool _importedFromTcNo;

    public bool OnboardingCompleted { get; private set; }

    private const int TotalSteps = 5;

    partial void OnCurrentStepChanged(int value)
    {
        CanGoBack = value > 1 && value < TotalSteps;
        IsLastStep = value == TotalSteps;
        UpdateCanGoNext();
    }

    partial void OnWarning1ConfirmedChanged(bool value) => UpdateCanGoNext();
    partial void OnWarning2ConfirmedChanged(bool value) => UpdateCanGoNext();
    partial void OnWarning3ConfirmedChanged(bool value) => UpdateCanGoNext();

    private void UpdateCanGoNext()
    {
        CanGoNext = CurrentStep switch
        {
            3 => Warning1Confirmed && Warning2Confirmed && Warning3Confirmed,
            4 => SteamFound,
            _ => true
        };
    }

    [RelayCommand]
    private async Task SelectThemeAsync(string theme)
    {
        SelectedTheme = theme;
        var appTheme = theme switch
        {
            "Light" => Core.Models.AppTheme.Light,
            "Dark" => Core.Models.AppTheme.Dark,
            _ => Core.Models.AppTheme.System
        };
        var current = settingsService.Current;
        current.Theme = appTheme;
        await settingsService.SaveAsync(current);
        App.ApplyTheme(appTheme);
    }

    public async Task InitializeAsync()
    {
        SelectedTheme = settingsService.Current.Theme switch
        {
            Core.Models.AppTheme.Light => "Light",
            Core.Models.AppTheme.Dark => "Dark",
            _ => "System"
        };

        var path = locatorService.FindSteamInstallPath();
        SteamFound = !string.IsNullOrEmpty(path);
        SteamPath = path ?? string.Empty;

        // Contagem para o resumo final
        try
        {
            var accounts = await accountService.GetAccountsAsync();
            AccountsFoundCount = accounts.Count;
        }
        catch { }
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < TotalSteps)
            CurrentStep++;
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
            CurrentStep--;
    }

    [RelayCommand]
    private async Task LoadDrivesAsync()
    {
        AvailableDrives = await onboardingService.GetSuitableDrivesAsync();
        SelectedDrive = AvailableDrives.FirstOrDefault();
        ShowDriveSelector = true;
    }

    public void UpdateStepItems()
    {
        var labels = new[] { "Tema", "Bem-vindo", "Avisos", "Steam", "Concluído" };
        StepItems = Enumerable.Range(1, TotalSteps)
            .Select(i => new StepItem(i, labels[i - 1])
            {
                IsCurrent = i == CurrentStep,
                IsCompleted = i < CurrentStep
            })
            .ToList();
    }

    [RelayCommand]
    private void SkipSteamInstall()
    {
        ShowDriveSelector = false;
        // Permite avançar mesmo sem Steam (banner aparecerá no dashboard)
        CanGoNext = true;
    }

    [RelayCommand]
    private void SelectDrive(DriveInfo drive) => SelectedDrive = drive;

    [RelayCommand]
    private void CancelDriveSelector()
    {
        ShowDriveSelector = false;
        SelectedDrive = null;
    }

    [RelayCommand]
    private async Task InstallSteamAsync(CancellationToken ct)
    {
        if (SelectedDrive is null) return;

        IsInstallingSteam = true;
        InstallProgress = 0;

        var progress = new Progress<int>(p => InstallProgress = p);
        var success = await onboardingService.InstallSteamAsync(
            SelectedDrive.Name, progress, ct);

        IsInstallingSteam = false;

        if (success)
        {
            var path = locatorService.FindSteamInstallPath();
            SteamFound = !string.IsNullOrEmpty(path);
            SteamPath = path ?? string.Empty;
            ShowDriveSelector = false;
            UpdateCanGoNext();

            snackbarService.Show(
                "Steam instalado",
                "Instalação concluída com sucesso.",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(3));
        }
        else
        {
            snackbarService.Show(
                "Erro na instalação",
                "Não foi possível instalar o Steam. Tente manualmente.",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private async Task UninstallSteamAsync()
    {
        await onboardingService.UninstallSteamAsync();
        SteamFound = false;
        SteamPath = string.Empty;
        ShowDriveSelector = false;
        UpdateCanGoNext();
    }

    [RelayCommand]
    private void Complete()
    {
        OnboardingCompleted = true;
        onboardingService.CompleteOnboarding();

        mainWindow.Show();

        foreach (var window in System.Windows.Application.Current.Windows
                     .OfType<System.Windows.Window>()
                     .Where(w => w is not Views.MainWindow)
                     .ToList())
        {
            window.Close();
        }
    }
}