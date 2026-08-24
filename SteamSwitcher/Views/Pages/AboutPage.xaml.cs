using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SteamSwitcher.Views.Pages;

public partial class AboutPage : Page
{
    private const string RepositoryUrl = "https://github.com/flaviobuarque/SteamNexus";

    public string VersionText { get; } = GetVersionText();

    public AboutPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    private static string GetVersionText()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null
            ? "Versão em desenvolvimento"
            : $"Versão {version.Major}.{version.Minor}.{version.Build}";
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
