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
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
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

    // Garante que o wheel role a página mesmo sobre Cards não-ItemsControl
    // (ItemsControl virtualizado já absorve wheel, este handler evita
    // situações onde o evento é interceptado por algum filho).
    private void OnPageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        var scrollViewer = FindScrollViewer(AccountsItemsControl);
        if (scrollViewer is null) return;

        scrollViewer.ScrollToVerticalOffset(
            scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }
}