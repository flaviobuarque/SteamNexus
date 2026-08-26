using SteamSwitcher.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SteamSwitcher.Views.Pages;

public partial class GamesPage : Page,
    Wpf.Ui.Abstractions.Controls.INavigableView<GamesViewModel>
{
    public GamesViewModel ViewModel { get; }

    public GamesPage(GamesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<GameGridDensityChanged>(this);
        WeakReferenceMessenger.Default.Register<GameGridDensityChanged>(
            this,
            (_, _) => ViewModel.RefreshGameGridDensity());

        await ViewModel.InitializeAsync();

        ViewModel.RefreshStatusBar();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
        => WeakReferenceMessenger.Default.Unregister<GameGridDensityChanged>(this);

    private void GameSortButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
            return;

        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.Placement = PlacementMode.Bottom;
        element.ContextMenu.IsOpen = true;
    }
}
