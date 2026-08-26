using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamKnownAccountStoreTests
{
    [Fact]
    public async Task KeepsSameSteamIdSeparatedAcrossInstallations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"steam-known-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SteamKnownAccountStore(path);
            await store.RememberAsync(
            [
                Account("first", "76561198000000001", "first_login"),
                Account("second", "76561198000000001", "second_login"),
            ]);

            var records = await store.LoadAsync();

            records.Should().HaveCount(2);
            records["first:76561198000000001"].AccountName.Should().Be("first_login");
            records["second:76561198000000001"].AccountName.Should().Be("second_login");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RemovesOnlyRequestedInstallationAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"steam-known-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SteamKnownAccountStore(path);
            await store.RememberAsync(
            [
                Account("first", "76561198000000001", "first_login"),
                Account("second", "76561198000000001", "second_login"),
            ]);

            await store.RemoveAsync(["first:76561198000000001"]);
            var records = await store.LoadAsync();

            records.Keys.Should().Equal("second:76561198000000001");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SteamAccount Account(
        string installationId,
        string steamId,
        string accountName) => new()
        {
            InstallationId = installationId,
            SteamId64 = steamId,
            AccountName = accountName,
        };
}
