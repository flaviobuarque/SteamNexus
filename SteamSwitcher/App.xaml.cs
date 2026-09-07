using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamSwitcher.Core;
using SteamSwitcher.Core.Extensions;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Services.Updates;
using SteamSwitcher.Services.Notifications;
using SteamSwitcher.Services.Themes;
using SteamSwitcher.ViewModels;
using SteamSwitcher.Views;
using SteamSwitcher.Views.Onboarding;
using SteamSwitcher.Views.Pages;
using System.Windows;
using System.Windows.Input;
using Velopack;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace SteamSwitcher;

public partial class App : Application
{
    private IHost? _host;
    private readonly CancellationTokenSource _updateMonitorCancellation = new();
    private readonly CancellationTokenSource _steamStateMonitorCancellation = new();
    private string _lastNotifiedUpdateVersion = string.Empty;
    private bool _windowReady;
    private bool _activationPending;

    [STAThread]
    private static void Main(string[] args)
    {
        // Precisa executar antes do Host e de qualquer janela para que os hooks
        // rápidos de instalação e atualização possam terminar sem carregar o WPF.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        SteamSwitcher.Helpers.ScrollViewerAssist.Register();
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();

        await _host.StartAsync();

        var systemService = _host.Services.GetRequiredService<ISystemService>();

        systemService.ExistingInstanceActivated += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                _activationPending = true;
                if (_windowReady) RestoreExistingWindow();
            });

        // Single instance — se já existe outra, encerra
        if (!systemService.IsSingleInstance(out _))
        {
            Shutdown();
            return;
        }


        // Carrega settings
        var settingsService = _host.Services.GetRequiredService<IAppSettingsService>();
        await settingsService.LoadAsync();

        var installationService = _host.Services.GetRequiredService<ISteamInstallationService>();
        await installationService.DiscoverAsync();

        // Aplica tema salvo
        var safeThemeStartup = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var customTheme = safeThemeStartup
            ? null
            : !string.IsNullOrWhiteSpace(settingsService.Current.ActiveThemePresetName)
                ? CustomThemeManager.CreateBuiltInPreset(settingsService.Current.ActiveThemePresetName!)
                : settingsService.Current.CustomTheme;
        if (customTheme is not null) customTheme.IsEnabled = true;
        try
        {
            ApplyTheme(settingsService.Current.Theme, customTheme);
        }
        catch
        {
            if (settingsService.Current.CustomTheme is not null)
                settingsService.Current.CustomTheme.IsEnabled = false;
            settingsService.Current.ActiveThemePresetName = null;
            await settingsService.SaveAsync(settingsService.Current);
            ApplyTheme(settingsService.Current.Theme);
        }

        // Watchdog — verifica se houve crash durante troca
        var watchdog = _host.Services.GetRequiredService<IWatchdogService>();
        if (watchdog.HasInterruptedSwitch(out var interruptedId))
        {
            // ViewModel principal vai mostrar o aviso
        }

        // Onboarding ou janela principal
        try
        {
            var onboarding = _host.Services.GetRequiredService<IOnboardingService>();
            if (onboarding.IsFirstRun || onboarding.HasCorruptedInstallFlag)
            {
                var onboardingWindow = _host.Services.GetRequiredService<OnboardingWindow>();
                onboardingWindow.Show();
            }
            else
            {
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var current = ex;
            int depth = 0;
            while (current != null)
            {
                sb.AppendLine($"=== Exception depth {depth} ===");
                sb.AppendLine($"Type: {current.GetType().FullName}");
                sb.AppendLine($"Message: {current.Message}");
                sb.AppendLine($"StackTrace: {current.StackTrace}");
                sb.AppendLine();
                current = current.InnerException;
                depth++;
            }
            System.IO.File.WriteAllText(@"C:\crash.txt", sb.ToString());
            MessageBox.Show(sb.ToString(), "Crash detail");
            Shutdown();
        }

        // Não bloqueia a abertura e continua verificando enquanto o app estiver aberto.
        _ = MonitorUpdatesAsync(_updateMonitorCancellation.Token);
        _ = MonitorSteamStateAsync(_steamStateMonitorCancellation.Token);

        // Arg --minimized (startup com Windows)
        if (e.Args.Contains("--minimized"))
            Application.Current.MainWindow?.Hide();

        _windowReady = true;
        if (_activationPending) RestoreExistingWindow();

        // Arg --switch <steamid>
        var switchArg = GetArgValue(e.Args, "--switch");
        if (!string.IsNullOrEmpty(switchArg))
            await HandleCliSwitchAsync(switchArg, GetArgValue(e.Args, "--state"));
    }

    private void RestoreExistingWindow()
    {
        _activationPending = false;
        _host?.Services.GetRequiredService<MainViewModel>().ShowWindowFromTray();
    }

    private async Task MonitorUpdatesAsync(CancellationToken ct)
    {
        if (_host is null)
            return;

        var updateService = _host.Services.GetRequiredService<IUpdateService>();
        var settingsService = _host.Services.GetRequiredService<IAppSettingsService>();
        if (!updateService.IsConfigured || !updateService.IsInstalled)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            while (!ct.IsCancellationRequested)
            {
                if (!settingsService.Current.CheckForUpdatesAutomatically)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                    continue;
                }

                if (!updateService.IsUpdateAvailable && updateService.CanCheckForUpdates)
                    await updateService.CheckForUpdatesAsync(ct);

                var shouldNotify = updateService.IsUpdateAvailable
                    && !string.IsNullOrWhiteSpace(updateService.AvailableVersion)
                    && !string.Equals(
                        _lastNotifiedUpdateVersion,
                        updateService.AvailableVersion,
                        StringComparison.Ordinal)
                    && Current.MainWindow?.IsVisible == true;

                if (shouldNotify)
                {
                    var snackbar = _host.Services.GetRequiredService<ISnackbarService>();
                    snackbar.Show(
                        "Nova atualização disponível",
                        $"A versão {updateService.AvailableVersion} está pronta para baixar. Use o botão no rodapé para escolher quando instalar.",
                        Wpf.Ui.Controls.ControlAppearance.Info,
                        null,
                        TimeSpan.FromSeconds(8));
                    _lastNotifiedUpdateVersion = updateService.AvailableVersion;
                }

                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Encerramento normal do aplicativo.
        }
        catch
        {
            // O monitor nunca deve impedir o uso do aplicativo.
        }
    }

    private async Task MonitorSteamStateAsync(CancellationToken ct)
    {
        if (_host is null)
            return;

        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                try
                {
                    await Dispatcher.InvokeAsync(
                            () => mainViewModel.RefreshActiveAccountAsync(ct))
                        .Task
                        .Unwrap();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _host.Services.GetRequiredService<ILogger<App>>()
                        .LogWarning(ex, "Steam session refresh failed; retrying on next poll");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Encerramento normal do aplicativo.
        }
        catch
        {
            // A atualização de status nunca deve interromper o aplicativo.
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _updateMonitorCancellation.Cancel();
        _steamStateMonitorCancellation.Cancel();

        if (_host is not null)
        {
            if (FeatureFlags.Mods)
            {
                var modMonitor = _host.Services.GetRequiredService<IModMonitorService>();
                modMonitor.StopWatching();
            }

            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        _updateMonitorCancellation.Dispose();
        _steamStateMonitorCancellation.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core
        services.AddSteamSwitcherCore();

        // WPF-UI
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ISnackbarService, ModernSnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddNavigationViewPageProvider();
        services.AddSingleton<IUpdateService, VelopackUpdateService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<AccountsViewModel>();
        services.AddSingleton<GamesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        if (FeatureFlags.Mods) services.AddSingleton<ModsViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<EditAccountViewModel>();

        // Windows e Pages
        services.AddSingleton<MainWindow>();
        services.AddSingleton<OnboardingWindow>();
        services.AddTransient<AccountsPage>();
        services.AddTransient<GamesPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<DiagnosticsPage>();
        services.AddTransient<AboutPage>();
        if (FeatureFlags.Mods) services.AddSingleton<ModsPage>();
    }

    public static void ApplyTheme(
        SteamSwitcher.Core.Models.AppTheme theme,
        SteamSwitcher.Core.Models.CustomThemeSettings? customTheme = null)
    {
        var wpfUiTheme = theme switch
        {
            Core.Models.AppTheme.Light => Wpf.Ui.Appearance.ApplicationTheme.Light,
            Core.Models.AppTheme.Dark => Wpf.Ui.Appearance.ApplicationTheme.Dark,
            _ => Wpf.Ui.Appearance.ApplicationTheme.Unknown
        };

        // Aplica WPF-UI primeiro
        if (wpfUiTheme == Wpf.Ui.Appearance.ApplicationTheme.Unknown)
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        else
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfUiTheme);

        // Swap do ResourceDictionary de tema — após WPF-UI para sobrescrever os defaults da lib
        var themeFile = theme == Core.Models.AppTheme.Light ? "Light.xaml" : "Dark.xaml";
        var uri = new Uri($"Themes/{themeFile}", UriKind.Relative);

        var existing = Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/") == true);

        if (existing != null)
            Current.Resources.MergedDictionaries.Remove(existing);

        Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        if (customTheme is not { IsEnabled: true })
            CustomThemeManager.ApplyBaseAccent(theme);
        CustomThemeManager.Apply(customTheme);
    }

    private static string? GetArgValue(string[] args, string key)
    {
        var idx = Array.IndexOf(args, key);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private async Task HandleCliSwitchAsync(string steamId64, string? state)
    {
        var accountService = _host!.Services.GetRequiredService<ISteamAccountService>();
        var accounts = await accountService.GetAccountsAsync();
        var target = accounts.FirstOrDefault(a => a.SteamId64 == steamId64);
        if (target is null) return;

        SteamSwitcher.Core.Models.LoginState? loginState = state?.ToLowerInvariant() switch
        {
            "online" => Core.Models.LoginState.Online,
            "offline" => Core.Models.LoginState.Offline,
            "invisible" => Core.Models.LoginState.Invisible,
            "away" => Core.Models.LoginState.Away,
            _ => null
        };

        await accountService.SwitchAccountAsync(target, loginState);
        Shutdown();
    }
}
