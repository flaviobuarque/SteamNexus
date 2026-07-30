using SteamSwitcher.Core;
using SteamSwitcher.ViewModels;
using SteamSwitcher.Views.Dialogs;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Input;

namespace SteamSwitcher.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.Page,
    Wpf.Ui.Abstractions.Controls.INavigableView<SettingsViewModel>
{
    public SettingsViewModel ViewModel { get; }
    private bool _loadingPasswords;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        ApplyFeatureVisibility();
        Loaded += Page_Loaded;

        this.Unloaded += (_, _) => ViewModel.ConfirmNavigateAway();
    }

    private void ApplyFeatureVisibility()
    {
        SteamApiKeyTile.Visibility = FeatureFlags.SteamWebApiKey
            ? Visibility.Visible
            : Visibility.Collapsed;

        SteamGridDbApiKeyTile.Style = (Style)FindResource(
            FeatureFlags.SteamWebApiKey ? "TileLastStyle" : "TileStyle");
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmFunc = (title, message, confirm, cancel) =>
        {
            var dialog = new ConfirmDialog(title, message, confirm, cancel,
                confirm.Contains("Apagar") ? ConfirmDialog.DialogKind.Danger : ConfirmDialog.DialogKind.Question)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
            return dialog.Confirmed;
        };

        ViewModel.Initialize();

        _ = ViewModel.LoadCacheSizeAsync();
        _loadingPasswords = true;
        if (FeatureFlags.SteamWebApiKey)
            SteamApiKeyBox.Password = ViewModel.SteamApiKey;
        SteamGridDbApiKeyBox.Password = ViewModel.SteamGridDbApiKey;
        _loadingPasswords = false;
    }

    private void SteamApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingPasswords) return;
        if (sender is Wpf.Ui.Controls.PasswordBox pb)
            ViewModel.SteamApiKey = pb.Password;
    }

    private void SteamGridDbApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingPasswords) return;
        if (sender is Wpf.Ui.Controls.PasswordBox pb)
            ViewModel.SteamGridDbApiKey = pb.Password;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void BeginHotkeyCapture_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsGlobalHotkeyEnabled)
            return;

        ViewModel.IsCapturingHotkey = true;

        try
        {
            var dialog = new HotkeyCaptureDialog
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true && dialog.CapturedHotkey?.IsValid == true)
            {
                ViewModel.GlobalHotkey = dialog.CapturedHotkey;
                ViewModel.HotkeyDisplayText = dialog.CapturedHotkey.DisplayText;
            }
        }
        finally
        {
            ViewModel.IsCapturingHotkey = false;
        }
    }

    private void GlobalHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.CaptureHotkeyPreviewKeyDown(e))
            e.Handled = true;
    }

    private void GlobalHotkeyBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (ViewModel.CaptureHotkeyPreviewKeyUp(e))
            e.Handled = true;
    }
}