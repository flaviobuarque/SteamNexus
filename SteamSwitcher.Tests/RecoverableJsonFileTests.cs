using FluentAssertions;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Models;
using System.Text.Json;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using SteamSwitcher.Core.Services;

namespace SteamSwitcher.Tests;

public sealed class RecoverableJsonFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SteamNexusRecoveryTests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task CorruptPrimary_RecoversLastValidBackup_WithoutReplacingItWithCorruption()
    {
        await RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings { CoverCacheExpiryDays = 15 }, Options);
        await RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings { CoverCacheExpiryDays = 25 }, Options);
        await File.WriteAllTextAsync(SettingsPath, "{incomplete");
        var recovered = await RecoverableJsonFile.ReadAsync<AppSettings>(SettingsPath, Options);
        recovered!.CoverCacheExpiryDays.Should().Be(15);
        await RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings { CoverCacheExpiryDays = 35 }, Options);
        JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(SettingsPath + ".bak"), Options)!
            .CoverCacheExpiryDays.Should().Be(15);
        (await RecoverableJsonFile.ReadAsync<AppSettings>(SettingsPath, Options))!.CoverCacheExpiryDays.Should().Be(35);
    }

    [Fact]
    public async Task MissingPrimary_StillReadsBackup()
    {
        await RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings { ActiveThemePresetName = "Roxo neon" }, Options);
        await RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings(), Options);
        File.Delete(SettingsPath);
        (await RecoverableJsonFile.ReadAsync<AppSettings>(SettingsPath, Options))!.ActiveThemePresetName.Should().Be("Roxo neon");
    }

    [Fact]
    public async Task ConcurrentSaves_ProduceCompleteSnapshots_AndNoTemporaryFiles()
    {
        var shared = new AppSettings();
        var tasks = new List<Task>();
        for (var i = 0; i < 100; i++)
        {
            shared.CoverCacheExpiryDays = i;
            tasks.Add(RecoverableJsonFile.WriteAsync(SettingsPath, shared, Options));
        }
        await Task.WhenAll(tasks);
        var result = await RecoverableJsonFile.ReadAsync<AppSettings>(SettingsPath, Options);
        result.Should().NotBeNull();
        result!.CoverCacheExpiryDays.Should().Be(99);
        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
        JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(SettingsPath + ".bak"), Options).Should().NotBeNull();
    }

    [Fact]
    public async Task SerializationFailure_PreservesExistingFile()
    {
        await RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings { CoverCacheExpiryDays = 42 }, Options);
        var cycle = new Dictionary<string, object>();
        cycle["self"] = cycle;
        var save = () => RecoverableJsonFile.WriteAsync(SettingsPath, cycle, Options);
        await save.Should().ThrowAsync<JsonException>();
        (await RecoverableJsonFile.ReadAsync<AppSettings>(SettingsPath, Options))!.CoverCacheExpiryDays.Should().Be(42);
    }

    [Fact]
    public async Task SettingsService_RoundTripsExistingPreferences_AndLoadsBackup()
    {
        var service = new AppSettingsService(NullLogger<AppSettingsService>.Instance, SettingsPath);
        await service.SaveAsync(new AppSettings
        {
            ActiveThemePresetName = "Roxo neon", PreserveRememberPasswordAccounts = ["test-account"],
            GameViewMode = GameViewMode.Compact, GameSortMode = GameSortMode.RecentlyPlayed,
            AcknowledgedAccountPolicy = "test-policy"
        });
        await service.SaveAsync(new AppSettings());
        await File.WriteAllTextAsync(SettingsPath, "broken");
        await service.LoadAsync();
        service.Current.ActiveThemePresetName.Should().Be("Roxo neon");
        service.Current.PreserveRememberPasswordAccounts.Should().Equal("test-account");
        service.Current.GameViewMode.Should().Be(GameViewMode.Compact);
        service.Current.GameSortMode.Should().Be(GameSortMode.RecentlyPlayed);
        service.Current.AcknowledgedAccountPolicy.Should().Be("test-policy");
    }

    [Fact]
    public async Task LockedDestination_DoesNotLosePreviousSettings()
    {
        await RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings { CoverCacheExpiryDays = 42 }, Options);
        using (var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var save = () => RecoverableJsonFile.WriteAsync(SettingsPath, new AppSettings(), Options);
            await save.Should().ThrowAsync<IOException>();
        }
        (await RecoverableJsonFile.ReadAsync<AppSettings>(SettingsPath, Options))!.CoverCacheExpiryDays.Should().Be(42);
        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
