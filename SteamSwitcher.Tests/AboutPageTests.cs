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
        updateService.CurrentVersion.Returns("1.0.31");

        var text = AboutPage.ResolveVersionText(updateService);
        text.Should().Be("Versão 1.0.31");
    }

    [Fact]
    public void ResolveVersionText_WithoutUpdateService_ReturnsAssemblyVersion()
    {
        var text = AboutPage.ResolveVersionText(null);
        text.Should().StartWith("Versão ");
        text.Should().Be("Versão 1.0.31");
    }
}
