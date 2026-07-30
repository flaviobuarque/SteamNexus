using SteamSwitcher.Core;
using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

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
        SizeChanged += Page_SizeChanged;
        GamesScrollViewer.ScrollChanged += OnScrollChanged;
        Unloaded += (_, _) => ViewModel.SetPollingActive(false);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetPollingActive(true);
        AdjustScrollViewerHeight();
        if (!_initialized)
        {
            await ViewModel.InitializeAsync();
            _initialized = true;
        }

        ViewModel.RefreshStatusBar();
        UpdateVisibleCards();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        => AdjustScrollViewerHeight();

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        => UpdateVisibleCards();

    private void UpdateVisibleCards()
    {
        if (GamesItemsControl.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
            return;

        var viewport = new Rect(0, 0,
            GamesScrollViewer.ViewportWidth,
            GamesScrollViewer.ViewportHeight);

        foreach (var card in ViewModel.Games)
        {
            if (card.IsVisibleInViewport) continue;

            var container = GamesItemsControl.ItemContainerGenerator
                .ContainerFromItem(card) as FrameworkElement;
            if (container is null) continue;

            try
            {
                var transform = container.TransformToAncestor(GamesScrollViewer);
                var bounds = transform.TransformBounds(
                    new Rect(0, 0, container.ActualWidth, container.ActualHeight));

                if (viewport.IntersectsWith(bounds))
                    card.IsVisibleInViewport = true;
            }
            catch { }
        }
    }

    private void AdjustScrollViewerHeight()
    {
        var toolbarHeight = Toolbar.ActualHeight;
        var pageHeight = ActualHeight;
        if (pageHeight > 0 && toolbarHeight > 0)
            GamesScrollViewer.MaxHeight = pageHeight - toolbarHeight;
    }
}