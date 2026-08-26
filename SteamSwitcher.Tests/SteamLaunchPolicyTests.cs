using FluentAssertions;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamLaunchPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void LaunchesAsDesktopUserOnlyWhenElevationMustNotBeInherited(
        bool currentProcessIsElevated,
        bool startAsAdmin,
        bool expected)
    {
        SteamLaunchPolicy.ShouldLaunchAsDesktopUser(
                currentProcessIsElevated,
                startAsAdmin)
            .Should().Be(expected);
    }
}
