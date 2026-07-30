using System.Windows;
using SteamSwitcher.ViewModels;
using SteamSwitcher.Views.Onboarding.Steps;
using Wpf.Ui;

namespace SteamSwitcher.Views.Onboarding;

public partial class OnboardingWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly OnboardingViewModel _viewModel;

    public OnboardingWindow(
        OnboardingViewModel viewModel,
        ISnackbarService snackbarService)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OnboardingViewModel.CurrentStep))
                UpdateStepView();
        };
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await _viewModel.InitializeAsync();
        UpdateStepView();
        _viewModel.UpdateStepItems();
    }

    private void UpdateStepView()
    {
        _viewModel.CurrentStepView = _viewModel.CurrentStep switch
        {
            1 => new Step0Theme { DataContext = _viewModel },
            2 => new Step1Welcome(),
            3 => new Step2Warnings { DataContext = _viewModel },
            4 => new Step3Steam { DataContext = _viewModel },
            5 => new Step5Done { DataContext = _viewModel },
            _ => null
        };

        _viewModel.UpdateStepItems();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_viewModel.OnboardingCompleted)
            System.Windows.Application.Current.Shutdown();
    }
}