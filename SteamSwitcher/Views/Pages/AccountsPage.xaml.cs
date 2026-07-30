using CommunityToolkit.Mvvm.Messaging;
using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        SizeChanged += Page_SizeChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AdjustScrollViewerHeight();

        RemoveHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPageMouseWheel));

        AddHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPageMouseWheel),
            handledEventsToo: true);

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
        RemoveHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPageMouseWheel));
        WeakReferenceMessenger.Default.Unregister<CacheCleared>(this);
        _messengerRegistered = false;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        => AdjustScrollViewerHeight();

    private void AdjustScrollViewerHeight()
    {
        var toolbarHeight = Toolbar.ActualHeight;
        var pageHeight = ActualHeight;

        if (pageHeight > 0 && toolbarHeight > 0)
        {
            var maxH = pageHeight - toolbarHeight;
            GridScrollViewer.MaxHeight = maxH;
            ListScrollViewer.MaxHeight = maxH;
        }
    }

    private void OnPageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = ViewModel.IsGridView
            ? GridScrollViewer
            : ListScrollViewer;

        scrollViewer.ScrollToVerticalOffset(
            scrollViewer.VerticalOffset - e.Delta);

        e.Handled = true;
    }
}