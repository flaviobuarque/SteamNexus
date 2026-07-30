using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SteamSwitcher.Views.Pages;

public partial class GamesPage : Page,
    Wpf.Ui.Abstractions.Controls.INavigableView<GamesViewModel>
{
    public GamesViewModel ViewModel { get; }

    private bool _initialized;

    public GamesPage(GamesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += Page_Loaded;
        Unloaded += (_, _) => ViewModel.SetPollingActive(false);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetPollingActive(true);

        if (!_initialized)
        {
            await ViewModel.InitializeAsync();
            _initialized = true;
        }

        ViewModel.RefreshStatusBar();
    }
}