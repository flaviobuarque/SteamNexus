using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamSwitcher.Core;
using SteamSwitcher.Core.Extensions;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Services.Updates;
using SteamSwitcher.Services.Notifications;
using SteamSwitcher.ViewModels;
using SteamSwitcher.Views;
using SteamSwitcher.Views.Onboarding;
using SteamSwitcher.Views.Pages;
using System.Windows;
using Velopack;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace SteamSwitcher;

public partial class App : Application
{
    private IHost? _host;

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

        // Single instance — se já existe outra, encerra
        if (!systemService.IsSingleInstance(out _))
        {
            Shutdown();
            return;
        }

        // Carrega settings
        var settingsService = _host.Services.GetRequiredService<IAppSettingsService>();
        await settingsService.LoadAsync();

        // Aplica tema salvo
        ApplyTheme(settingsService.Current.Theme);

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

        // Não bloqueia a abertura: versões instaladas consultam e preparam a
        // atualização alguns segundos depois que a interface estiver pronta.
        _ = PrepareUpdateInBackgroundAsync();

        // Arg --minimized (startup com Windows)
        if (e.Args.Contains("--minimized"))
            Application.Current.MainWindow?.Hide();

        // Arg --switch <steamid>
        var switchArg = GetArgValue(e.Args, "--switch");
        if (!string.IsNullOrEmpty(switchArg))
            await HandleCliSwitchAsync(switchArg, GetArgValue(e.Args, "--state"));
    }

    private async Task PrepareUpdateInBackgroundAsync()
    {
        if (_host is null)
            return;

        var updateService = _host.Services.GetRequiredService<IUpdateService>();
        if (!updateService.CanCheckForUpdates)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            await updateService.CheckForUpdatesAsync();
            if (!updateService.IsUpdateAvailable)
                return;

            await updateService.DownloadUpdateAsync();
            if (!updateService.IsUpdateReady)
                return;

            var snackbar = _host.Services.GetRequiredService<ISnackbarService>();
            snackbar.Show(
                "Atualização pronta",
                $"A versão {updateService.AvailableVersion} será instalada quando você confirmar nas configurações.",
                Wpf.Ui.Controls.ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(7));
        }
        catch
        {
            // Atualização automática nunca deve impedir o uso do aplicativo.
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
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
        if (FeatureFlags.Mods) services.AddSingleton<ModsViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<EditAccountViewModel>();

        // Windows e Pages
        services.AddSingleton<MainWindow>();
        services.AddSingleton<OnboardingWindow>();
        services.AddTransient<AccountsPage>();
        services.AddTransient<GamesPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AboutPage>();
        if (FeatureFlags.Mods) services.AddSingleton<ModsPage>();
    }

    public static void ApplyTheme(SteamSwitcher.Core.Models.AppTheme theme)
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
