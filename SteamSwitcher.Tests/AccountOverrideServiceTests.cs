using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class AccountOverrideServiceTests
{
    [Fact]
    public async Task SaveAndGetOverride_StoresAndRetrievesData()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"override-test-{Guid.NewGuid():N}.json");
        try
        {
            var service = new AccountOverrideService(NullLogger<AccountOverrideService>.Instance, tempFile);
            var data = new AccountOverride { CustomDisplayName = "ProGamer", CustomAvatarPath = "C:\\avatar.png" };

            await service.SaveOverrideAsync("76561198000000001", data);

            var retrieved = await service.GetOverrideAsync("76561198000000001");
            retrieved.Should().NotBeNull();
            retrieved!.CustomDisplayName.Should().Be("ProGamer");
            retrieved.CustomAvatarPath.Should().Be("C:\\avatar.png");

            await service.RemoveOverrideAsync("76561198000000001");
            var afterRemove = await service.GetOverrideAsync("76561198000000001");
            afterRemove.Should().BeNull();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ConcurrentAccess_HandlesMultipleReadsAndWritesWithoutRaceConditions()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"override-concurrent-{Guid.NewGuid():N}.json");
        try
        {
            var service = new AccountOverrideService(NullLogger<AccountOverrideService>.Instance, tempFile);

            var tasks = new List<Task>();
            for (var i = 0; i < 50; i++)
            {
                var id = $"765611980000000{i:D2}";
                tasks.Add(Task.Run(async () =>
                {
                    await service.SaveOverrideAsync(id, new AccountOverride { CustomDisplayName = $"Player_{id}" });
                    var retrieved = await service.GetOverrideAsync(id);
                    retrieved.Should().NotBeNull();
                }));
            }

            await Task.WhenAll(tasks);

            for (var i = 0; i < 50; i++)
            {
                var id = $"765611980000000{i:D2}";
                var item = await service.GetOverrideAsync(id);
                item.Should().NotBeNull();
                item!.CustomDisplayName.Should().Be($"Player_{id}");
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
