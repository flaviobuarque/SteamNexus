using SteamSwitcher.Services.Updates;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SteamSwitcher.Views.Pages;

public partial class AboutPage : Page
{
    private const string RepositoryUrl = "https://github.com/flaviobuarque/SteamNexus";

    public string VersionText { get; }

    public AboutPage() : this(null) { }

    public AboutPage(IUpdateService? updateService)
    {
        InitializeComponent();
        VersionText = ResolveVersionText(updateService);
        DataContext = this;
    }

    public static string ResolveVersionText(IUpdateService? updateService = null)
    {
        // 1. If UpdateService has an active installed version (Velopack)
        if (updateService is not null && !string.IsNullOrWhiteSpace(updateService.CurrentVersion)
            && updateService.CurrentVersion != "0.0.0" && updateService.CurrentVersion != "1.0.0")
        {
            return $"Versão {updateService.CurrentVersion}";
        }

        var appAssembly = typeof(AboutPage).Assembly;

        // 2. InformationalVersion (e.g. 1.0.31 or 1.0.31+git_hash)
        var infoVersion = appAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var clean = infoVersion.Split('+')[0].Trim();
            if (clean != "0.0.0" && clean != "1.0.0" && clean != "1.0.0.0")
            {
                return $"Versão {clean}";
            }
        }

        // 3. Assembly Name Version (e.g. 1.0.31.0)
        var version = appAssembly.GetName().Version;
        if (version is not null && (version.Major != 1 || version.Minor != 0 || version.Build != 0))
        {
            return $"Versão {version.Major}.{version.Minor}.{version.Build}";
        }

        // 4. Fallback to update service or assembly version
        if (updateService is not null && !string.IsNullOrWhiteSpace(updateService.CurrentVersion) && updateService.CurrentVersion != "0.0.0")
        {
            return $"Versão {updateService.CurrentVersion}";
        }

        return version is not null
            ? $"Versão {version.Major}.{version.Minor}.{version.Build}"
            : "Versão em desenvolvimento";
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        => OpenUrl(RepositoryUrl);

    private void OpenReleases_Click(object sender, RoutedEventArgs e)
        => OpenUrl($"{RepositoryUrl}/releases");
}
