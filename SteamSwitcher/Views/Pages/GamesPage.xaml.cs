using SteamSwitcher.Core;
using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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

        RemoveHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnScrollChanged));
        AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnScrollChanged),
            handledEventsToo: true);

        if (!_initialized)
        {
            await ViewModel.InitializeAsync();
            _initialized = true;
        }

        ViewModel.RefreshStatusBar();
        UpdateVisibleCards();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        => UpdateVisibleCards();

    private void UpdateVisibleCards()
    {
        if (GamesItemsControl.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
            return;

        var scrollViewer = FindScrollViewer(GamesItemsControl);
        if (scrollViewer is null) return;

        var viewport = new Rect(0, 0,
            scrollViewer.ViewportWidth,
            scrollViewer.ViewportHeight);

        foreach (var card in ViewModel.Games)
        {
            if (card.IsVisibleInViewport) continue;

            var container = GamesItemsControl.ItemContainerGenerator
                .ContainerFromItem(card) as FrameworkElement;
            if (container is null) continue;

            try
            {
                var transform = container.TransformToAncestor(scrollViewer);
                var bounds = transform.TransformBounds(
                    new Rect(0, 0, container.ActualWidth, container.ActualHeight));

                if (viewport.IntersectsWith(bounds))
                    card.IsVisibleInViewport = true;
            }
            catch { }
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }
}