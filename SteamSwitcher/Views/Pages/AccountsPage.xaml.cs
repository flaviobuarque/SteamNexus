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

    private bool _initialized;
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

            _messengerRegistered = true;
        }

        if (!_initialized)
        {
            await ViewModel.InitializeAsync();
            _initialized = true;
        }

        ViewModel.RefreshStatusBar();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<CacheCleared>(this);
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
