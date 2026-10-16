using CommunityToolkit.Mvvm.Messaging;
using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SteamSwitcher.Views.Pages;

public partial class AccountsPage : Page,
    Wpf.Ui.Abstractions.Controls.INavigableView<AccountsViewModel>
{
    public AccountsViewModel ViewModel { get; }
    private readonly Helpers.PageScrollMemory _scrollMemory = new();
    private bool _navigationCaptured;
    public void ResetNavigationPosition() => _scrollMemory.Reset(this);
    public void CaptureNavigationState()
    {
        _scrollMemory.Capture(this);
        _navigationCaptured = true;
    }

    private bool _messengerRegistered;

    public AccountsPage(AccountsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;

        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _navigationCaptured = false;
        if (!_messengerRegistered)
        {
            WeakReferenceMessenger.Default.Register<CacheCleared>(
                this,
                async (_, _) =>
                {
                    await Application.Current.Dispatcher
                        .InvokeAsync(() => ViewModel.InitializeAsync())
                        .Task
                        .Unwrap();
                });

            WeakReferenceMessenger.Default.Register<SteamInstallationChanged>(
                this,
                async (_, _) =>
                {
                    await Application.Current.Dispatcher
                        .InvokeAsync(() => ViewModel.InitializeAsync())
                        .Task
                        .Unwrap();
                });

            WeakReferenceMessenger.Default.Register<AccountGridDensityChanged>(
                this,
                (_, _) => ViewModel.RefreshAccountGridDensity());

            _messengerRegistered = true;
        }

        // Refresh external Steam changes made while this cached page was hidden.
        // The view model updates cards incrementally and retains its filter state.
        await ViewModel.InitializeAsync();

        ViewModel.RefreshStatusBar();
        await _scrollMemory.RestoreAsync(this);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_navigationCaptured) _scrollMemory.Capture(this);
        WeakReferenceMessenger.Default.Unregister<CacheCleared>(this);
        WeakReferenceMessenger.Default.Unregister<SteamInstallationChanged>(this);
        WeakReferenceMessenger.Default.Unregister<AccountGridDensityChanged>(this);
        _messengerRegistered = false;
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
            return;

        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.Placement = PlacementMode.Bottom;
        element.ContextMenu.IsOpen = true;
    }
}
