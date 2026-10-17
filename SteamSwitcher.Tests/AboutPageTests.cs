using FluentAssertions;
using NSubstitute;
using SteamSwitcher.Services.Updates;
using SteamSwitcher.Views.Pages;
using Xunit;

namespace SteamSwitcher.Tests;

public class AboutPageTests
{
    [Fact]
    public void ResolveVersionText_WithUpdateService_ReturnsServiceVersion()
    {
        var updateService = Substitute.For<IUpdateService>();
        updateService.CurrentVersion.Returns("1.0.32");

        var text = AboutPage.ResolveVersionText(updateService);
        text.Should().Be("Versão 1.0.32");
    }

    [Fact]
    public void ResolveVersionText_WithoutUpdateService_ReturnsAssemblyVersion()
    {
        var text = AboutPage.ResolveVersionText(null);
        text.Should().StartWith("Versão ");
        var version = typeof(AboutPage).Assembly.GetName().Version;
        var versionStr = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : string.Empty;
        text.Should().Be($"Versão {versionStr}");
    }
}
