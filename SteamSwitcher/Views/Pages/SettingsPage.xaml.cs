using SteamSwitcher.Core;
using SteamSwitcher.ViewModels;
using SteamSwitcher.Views.Dialogs;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;

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
        UpdateActiveSettingsSection(AccountNavButton);
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

    private async void CleanupOldAccounts_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsCleaningOldAccounts) return;

        var button = sender as FrameworkElement;
        if (button is not null) button.IsEnabled = false;
        try
        {
            var accounts = await ViewModel.GetAccountsForCleanupAsync();
            var dialog = new CleanupOldAccountsDialog(accounts)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
                await ViewModel.CleanupOldAccountsAsync(dialog.CandidateAccounts);
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button button) return;

        var target = button.Tag?.ToString() switch
        {
            "Appearance" => AppearanceSection,
            "Installations" => InstallationsSection,
            "Steam" => SteamSection,
            "Integrations" => IntegrationsSection,
            "Maintenance" => MaintenanceSection,
            "Updates" => UpdatesSection,
            "Danger" => DangerSection,
            _ => AccountSection
        };

        var offset = target.TranslatePoint(new Point(0, 0), SettingsContent).Y;
        SettingsScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 8));
        UpdateActiveSettingsSection(button);
    }

    public void ShowUpdatesSection()
    {
        if (!IsLoaded)
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) =>
            {
                Loaded -= loaded;
                ShowUpdatesSection();
            };
            Loaded += loaded;
            return;
        }

        var offset = UpdatesSection
            .TranslatePoint(new Point(0, 0), SettingsContent).Y;
        SettingsScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 8));
        UpdateActiveSettingsSection(UpdatesNavButton);
    }

    private void SettingsScrollViewer_ScrollChanged(
        object sender,
        System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (!IsLoaded) return;

        var sections = GetSettingsSections();
        if (SettingsScrollViewer.VerticalOffset >= SettingsScrollViewer.ScrollableHeight - 2)
        {
            UpdateActiveSettingsSection(DangerNavButton);
            return;
        }

        var marker = SettingsScrollViewer.VerticalOffset + 72;
        var active = sections[0].Button;
        foreach (var section in sections)
        {
            var offset = section.Element
                .TranslatePoint(new Point(0, 0), SettingsContent).Y;
            if (offset > marker) break;
            active = section.Button;
        }

        UpdateActiveSettingsSection(active);
    }

    private (Wpf.Ui.Controls.Button Button, FrameworkElement Element)[]
        GetSettingsSections() =>
        [
            (AppearanceNavButton, AppearanceSection),
            (AccountNavButton, AccountSection),
            (InstallationsNavButton, InstallationsSection),
            (SteamNavButton, SteamSection),
            (IntegrationsNavButton, IntegrationsSection),
            (MaintenanceNavButton, MaintenanceSection),
            (UpdatesNavButton, UpdatesSection),
            (DangerNavButton, DangerSection)
        ];

    private SteamSwitcher.Core.Models.CustomThemeSettings ActiveThemeCopy()
    {
        var settings = ViewModel.SettingsSnapshot;
        return !string.IsNullOrEmpty(settings.ActiveThemePresetName)
            ? SteamSwitcher.Services.Themes.CustomThemeManager.CreateBuiltInPreset(settings.ActiveThemePresetName)
            : settings.CustomTheme is { IsEnabled: true } ? settings.CustomTheme.Clone()
            : settings.Theme == SteamSwitcher.Core.Models.AppTheme.Light
                ? SteamSwitcher.Core.Models.CustomThemeSettings.CreateLight() : SteamSwitcher.Core.Models.CustomThemeSettings.CreateDark();
    }

    private async Task ThemeActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            new ConfirmDialog("Não foi possível concluir", ex.Message, "Entendi") { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }

    private async Task EditThemeAsync(SteamSwitcher.Core.Models.CustomThemeSettings draft, string? id = null)
    {
        draft.IsEnabled = true;
        var original = ActiveThemeCopy();
        var baseTheme = ViewModel.SettingsSnapshot.Theme;
        try
        {
            var dialog = new ThemeEditorWindow(draft.BaseTheme, draft, editCustom: true) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.ResultTheme is not null)
                await ViewModel.ApplyCustomThemeAsync(dialog.ResultTheme, id);
            else App.ApplyTheme(baseTheme, original);
        }
        catch { App.ApplyTheme(baseTheme, original); throw; }
    }

    private async void OpenThemeEditor_Click(object sender, RoutedEventArgs e) => await ThemeActionAsync(async () =>
    {
        if (ViewModel.AppliedCustomTheme is { } entry) await EditThemeAsync(entry.Theme.Clone(), entry.Id);
    });

    private async void CreateTheme_Click(object sender, RoutedEventArgs e) => await ThemeActionAsync(async () =>
    {
        var setup = new ThemeSetupDialog(ActiveThemeCopy()) { Owner = Window.GetWindow(this) };
        if (setup.ShowDialog() == true && setup.ResultTheme is not null) await EditThemeAsync(setup.ResultTheme);
    });

    private void ThemeActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu is { } menu) { menu.PlacementTarget = element; menu.IsOpen = true; }
    }
    private async void DuplicateTheme_Click(object sender, RoutedEventArgs e) => await ThemeActionAsync(async () =>
    {
        if (ViewModel.AppliedCustomTheme is not { } entry) return;
        var copy = entry.Theme.Clone(); copy.Name += " — cópia";
        await EditThemeAsync(copy);
    });
    private async void RenameTheme_Click(object sender, RoutedEventArgs e) => await ThemeActionAsync(async () =>
    {
        if (ViewModel.AppliedCustomTheme is not { } entry) return;
        var dialog = new ThemeSetupDialog(entry.Theme, rename: true) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) await ViewModel.RenameCustomThemeAsync(dialog.ThemeName);
    });
    private async void DeleteTheme_Click(object sender, RoutedEventArgs e) => await ThemeActionAsync(async () =>
    {
        if (!ViewModel.HasAppliedCustomTheme) return;
        var dialog = new ConfirmDialog("Excluir tema", "Excluir o tema aplicado? O SteamNexus Dark será ativado. As imagens de fundo serão preservadas.", "Excluir") { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Confirmed) await ViewModel.DeleteCustomThemeAsync();
    });
    private async void ImportTheme_Click(object sender, RoutedEventArgs e) => await ThemeActionAsync(async () =>
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Tema SteamNexus|*.steamnexus-theme;*.json", CheckFileExists = true };
        if (picker.ShowDialog() != true) return;
        var imported = await SteamSwitcher.Services.Themes.CustomThemeManager.ImportAsync(picker.FileName);
        await EditThemeAsync(imported);
    });
    private async void ShareTheme_Click(object sender, RoutedEventArgs e) => await ThemeActionAsync(async () =>
    {
        if (ViewModel.AppliedCustomTheme is not { } entry) return;
        var picker = new Microsoft.Win32.SaveFileDialog { Filter = "Tema SteamNexus|*.steamnexus-theme", FileName = "tema-steamnexus.steamnexus-theme" };
        if (picker.ShowDialog() == true) await SteamSwitcher.Services.Themes.CustomThemeManager.ExportAsync(entry.Theme, picker.FileName);
    });

    private async void ThemeSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await ViewModel.ApplySelectedThemeAsync(ViewModel.SelectedThemeName);
    }

    private void UpdateActiveSettingsSection(Wpf.Ui.Controls.Button activeButton)
    {
        foreach (var section in GetSettingsSections())
        {
            var isActive = ReferenceEquals(section.Button, activeButton);
            section.Button.SetResourceReference(
                System.Windows.Controls.Control.BackgroundProperty,
                isActive ? "AppAccentBrush" : "AppSurfaceAltBrush");
            section.Button.SetResourceReference(
                System.Windows.Controls.Control.ForegroundProperty,
                isActive ? "AppOnAccentTextBrush" : "AppTextPrimaryBrush");
            section.Button.SetResourceReference(
                System.Windows.Controls.Control.BorderBrushProperty,
                isActive ? "AppAccentAltBrush" : "AppBorderBrush");
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
