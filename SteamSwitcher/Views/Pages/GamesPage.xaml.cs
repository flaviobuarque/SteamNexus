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
    private readonly Helpers.PageScrollMemory _scrollMemory = new();
    private bool _navigationCaptured;
    public void ResetNavigationPosition() => _scrollMemory.Reset(this);
    public void CaptureNavigationState()
    {
        _scrollMemory.Capture(this);
        _navigationCaptured = true;
    }

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
        _navigationCaptured = false;
        WeakReferenceMessenger.Default.Unregister<GameGridDensityChanged>(this);
        WeakReferenceMessenger.Default.Register<GameGridDensityChanged>(
            this,
            (_, _) => ViewModel.RefreshGameGridDensity());

        await ViewModel.InitializeAsync();

        ViewModel.RefreshStatusBar();
        await _scrollMemory.RestoreAsync(this);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_navigationCaptured) _scrollMemory.Capture(this);
        WeakReferenceMessenger.Default.Unregister<GameGridDensityChanged>(this);
    }

    private void GameSortButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
            return;

        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.Placement = PlacementMode.Bottom;
        element.ContextMenu.IsOpen = true;
    }

    private void GameOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        var current = sender as DependencyObject;
        while (current is not null)
        {
            if (current is Border { ContextMenu: not null } card)
            {
                card.ContextMenu.PlacementTarget = card;
                card.ContextMenu.Placement = PlacementMode.MousePoint;
                card.ContextMenu.IsOpen = true;
                e.Handled = true;
                return;
            }
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
    }
}
