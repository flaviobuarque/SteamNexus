using FluentAssertions;
using SteamSwitcher.Core.Helpers;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class AtomicJsonFileTests
{
    [Fact]
    public async Task ConcurrentUpdates_PreserveEveryEntry()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SteamSwitcher.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "games.json");

        try
        {
            var updates = Enumerable.Range(0, 100)
                .Select(index => AtomicJsonFile.UpdateAsync(
                    path,
                    static () => new Dictionary<string, string>(),
                    map => map[index.ToString()] = $"account-{index}"));

            await Task.WhenAll(updates);

            var result = await AtomicJsonFile.ReadAsync(
                path,
                static () => new Dictionary<string, string>());

            result.Should().HaveCount(100);
            result["0"].Should().Be("account-0");
            result["99"].Should().Be("account-99");
            Directory.EnumerateFiles(directory, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CanceledWrite_DoesNotReplaceExistingFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SteamSwitcher.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "games.json");

        try
        {
            await AtomicJsonFile.UpdateAsync(
                path,
                static () => new Dictionary<string, string>(),
                map => map["existing"] = "value");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var update = () => AtomicJsonFile.UpdateAsync(
                path,
                static () => new Dictionary<string, string>(),
                map => map["new"] = "value",
                cts.Token);

            await update.Should().ThrowAsync<OperationCanceledException>();

            var result = await AtomicJsonFile.ReadAsync(
                path,
                static () => new Dictionary<string, string>());
            result.Should().ContainKey("existing").And.NotContainKey("new");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
