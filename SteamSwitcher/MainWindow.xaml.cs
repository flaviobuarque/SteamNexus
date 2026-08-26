using CommunityToolkit.Mvvm.Messaging;
using SteamSwitcher.Core;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.ViewModels;
using SteamSwitcher.Views.Pages;
using SteamSwitcher.Views.Dialogs;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace SteamSwitcher.Views;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly ISnackbarService _snackbarService;
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly IContentDialogService _contentDialogService;
    private readonly IAppSettingsService _settingsService;

    private const int WmHotkey = 0x0312;
    private const int GlobalHotkeyId = 0x5353;

    private const uint ModAlt = 0x0001;
    private const uint ModCtrl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private HwndSource? _windowSource;
    private bool _globalHotkeyRegistered;

    public MainWindow(
        MainViewModel viewModel,
        ISnackbarService snackbarService,
        INavigationViewPageProvider pageProvider,
        IContentDialogService contentDialogService,
        IAppSettingsService settingsService)
    {
        _viewModel = viewModel;
        _snackbarService = snackbarService;
        _pageProvider = pageProvider;
        _contentDialogService = contentDialogService;
        _settingsService = settingsService;

        InitializeComponent();
        DataContext = _viewModel;
        ApplyFeatureVisibility();

        Loaded += OnLoaded;
        Closing += OnClosing;

        IsVisibleChanged += (_, e) =>
            _viewModel.IsWindowVisible = e.NewValue is bool visible && visible;

        Closed += (_, _) =>
        {
            UnregisterGlobalHotkey();

            if (_windowSource is not null)
                _windowSource.RemoveHook(WindowMessageHook);
        };
    }

    private void ApplyFeatureVisibility()
    {
        NavItemMods.Visibility = FeatureFlags.Mods ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        _contentDialogService.SetDialogHost(RootContentDialogHost);

        NavigateTo(typeof(AccountsPage), "Accounts");
        await _viewModel.InitializeAsync();

        // Navega para Configuracoes quando solicitado (ex.: botao "Configurar API key"
        // no placeholder de capa do jogo).
        if (!WeakReferenceMessenger.Default.IsRegistered<NavigateToSettingsRequested>(this))
        {
            WeakReferenceMessenger.Default.Register<NavigateToSettingsRequested>(this, (_, _) =>
                Dispatcher.Invoke(() => NavigateTo(typeof(SettingsPage), "Settings")));
        }

        ThemeNavItem.AddHandler(
            UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(ThemeNavItem_Click),
            handledEventsToo: true);

        if (TrayIcon.Menu is ContextMenu trayMenu)
            trayMenu.DataContext = _viewModel;

        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);

        _viewModel.GlobalHotkeyChanged += RegisterGlobalHotkey;

        RegisterGlobalHotkey(
            _settingsService.Current.IsGlobalHotkeyEnabled
                ? _settingsService.Current.GlobalHotkey
                : null);
    }

    private bool CanNavigateAway()
    {
        if (MainFrame.Content is SettingsPage settingsPage)
            return settingsPage.ViewModel.ConfirmNavigateAway();
        return true;
    }

    private void NavigateTo(Type pageType, string activeKey)
    {
        if (!CanNavigateAway()) return;
        var page = _pageProvider.GetPage(pageType);
        if (page is null) return;
        MainFrame.Navigate(page);
        _viewModel.ActivePage = activeKey;
        RefreshPageStatus(page, activeKey);
    }

    private void RefreshPageStatus(object page, string activeKey)
    {
        switch (page)
        {
            case AccountsPage accountsPage:
                accountsPage.ViewModel.RefreshStatusBar();
                break;
            case GamesPage gamesPage:
                gamesPage.ViewModel.RefreshStatusBar();
                break;
            case ModsPage modsPage:
                modsPage.ViewModel.RefreshStatusBar();
                break;
            case SettingsPage settingsPage:
                settingsPage.ViewModel.RefreshStatusBar();
                break;
            case DiagnosticsPage:
                _viewModel.UpdateStatusBar("Diagnóstico da Steam", "Verificação local");
                break;
            case AboutPage:
                _viewModel.UpdateStatusBar("Sobre o SteamNexus", "Projeto independente");
                break;
            default:
                _viewModel.UpdateStatusBar(string.Empty);
                break;
        }
    }

    private void NavItem_Accounts_Click(object sender, MouseButtonEventArgs e)
        => NavigateTo(typeof(AccountsPage), "Accounts");

    private void NavItem_Games_Click(object sender, MouseButtonEventArgs e)
        => NavigateTo(typeof(GamesPage), "Games");

    private void NavItem_Mods_Click(object sender, MouseButtonEventArgs e)
    {
        if (!FeatureFlags.Mods) return;
        NavigateTo(typeof(ModsPage), "Mods");
    }

    private void NavItem_Settings_Click(object sender, MouseButtonEventArgs e)
        => NavigateTo(typeof(SettingsPage), "Settings");

    private void NavItem_Diagnostics_Click(object sender, MouseButtonEventArgs e)
        => NavigateTo(typeof(DiagnosticsPage), "Diagnostics");

    private void NavItem_About_Click(object sender, MouseButtonEventArgs e)
        => NavigateTo(typeof(AboutPage), "About");

    private async void UpdateIndicator_Click(object sender, RoutedEventArgs e)
    {
        var updateService = _viewModel.UpdateService;
        if (!updateService.IsUpdateAvailable || updateService.IsDownloading)
            return;

        var dialog = new UpdatePromptDialog(
            updateService.AvailableVersion,
            updateService.IsUpdateReady)
        {
            Owner = this
        };
        dialog.ShowDialog();

        if (dialog.Choice == UpdatePromptDialog.UpdateChoice.Later)
            return;

        if (updateService.IsUpdateReady)
        {
            updateService.ApplyUpdateAndRestart();
            return;
        }

        await updateService.DownloadUpdateAsync();
        if (!updateService.IsUpdateReady)
        {
            _snackbarService.Show(
                "Falha no download",
                string.IsNullOrWhiteSpace(updateService.ErrorText)
                    ? "Não foi possível preparar a atualização."
                    : updateService.ErrorText,
                Wpf.Ui.Controls.ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(6));
            return;
        }

        if (dialog.Choice == UpdatePromptDialog.UpdateChoice.DownloadAndInstall)
        {
            updateService.ApplyUpdateAndRestart();
            return;
        }

        _snackbarService.Show(
            "Atualização pronta",
            $"A versão {updateService.AvailableVersion} foi baixada. Instale quando quiser pelo rodapé.",
            Wpf.Ui.Controls.ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(6));
    }

    private void ThemeNavItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border)
        {
            _viewModel.RefreshThemeSelection();
            border.ContextMenu!.DataContext = _viewModel;
            border.ContextMenu.PlacementTarget = border;
            border.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            border.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindScrollableAncestor(e.OriginalSource as DependencyObject);
        if (scrollViewer is null) return;

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableAncestor(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ScrollViewer sv && sv.ScrollableHeight > 0)
                return sv;

            element = element is Visual or Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        _viewModel.IsWindowVisible = false;
    }

    private void RegisterGlobalHotkey(HotkeyDefinition? hotkey)
    {
        UnregisterGlobalHotkey();

        if (hotkey?.IsValid != true)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        var modifiers = ToNativeModifiers(hotkey.Modifiers);
        var virtualKey = (uint)hotkey.VirtualKey;

        _globalHotkeyRegistered = RegisterHotKey(
            handle,
            GlobalHotkeyId,
            modifiers,
            virtualKey);
    }

    private void UnregisterGlobalHotkey()
    {
        if (!_globalHotkeyRegistered)
            return;

        UnregisterHotKey(new WindowInteropHelper(this).Handle, GlobalHotkeyId);
        _globalHotkeyRegistered = false;
    }

    private void TrayIcon_LeftDoubleClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleWindowCommand.Execute(null);
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == GlobalHotkeyId)
        {
            _viewModel.ToggleWindowCommand.Execute(null);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;

        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) result |= ModCtrl;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= ModShift;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= ModAlt;
        if (modifiers.HasFlag(HotkeyModifiers.Win)) result |= ModWin;

        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
