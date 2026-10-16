using FluentAssertions;
using SteamSwitcher.Core.Helpers;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class HttpClientDefaultsTests
{
    [Fact]
    public void CreateHandler_ConfiguresPooledConnectionLifetime()
    {
        using var handler = HttpClientDefaults.CreateHandler();

        handler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(15));
        handler.AutomaticDecompression.Should().Be(System.Net.DecompressionMethods.All);
    }

    [Fact]
    public void CreateClient_SetsConfiguredTimeout()
    {
        var expectedTimeout = TimeSpan.FromSeconds(12);
        using var client = HttpClientDefaults.CreateClient(expectedTimeout);

        client.Timeout.Should().Be(expectedTimeout);
    }
}
