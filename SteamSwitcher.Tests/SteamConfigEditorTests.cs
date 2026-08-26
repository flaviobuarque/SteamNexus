using FluentAssertions;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamConfigEditorTests
{
    [Fact]
    public void DisableAccountChooser_ChangesEnabledValueWithoutTouchingOtherSettings()
    {
        const string source = """
            "InstallConfigStore"
            {
                "Software"
                {
                    "AlwaysShowUserChooser"        "1"
                    "OtherSetting"                 "value"
                }
            }
            """;

        var result = SteamConfigEditor.DisableAccountChooser(source, out var changed);

        changed.Should().BeTrue();
        result.Should().Contain("\"AlwaysShowUserChooser\"        \"0\"");
        result.Should().Contain("\"OtherSetting\"                 \"value\"");
    }

    [Fact]
    public void DisableAccountChooser_DoesNotRewriteValueAlreadyDisabled()
    {
        const string source = "\t\"AlwaysShowUserChooser\"\t\t\"0\"\r\n";

        var result = SteamConfigEditor.DisableAccountChooser(source, out var changed);

        changed.Should().BeFalse();
        result.Should().Be(source);
    }

    [Fact]
    public void DisableAccountChooser_LeavesConfigWithoutSettingUntouched()
    {
        const string source = "\"OtherSetting\"\t\t\"1\"\n";

        var result = SteamConfigEditor.DisableAccountChooser(source, out var changed);

        changed.Should().BeFalse();
        result.Should().Be(source);
    }

    [Theory]
    [InlineData("\"AlwaysShowUserChooser\" \"1\"", true)]
    [InlineData("\"alwaysshowuserchooser\" \"true\"", true)]
    [InlineData("\"AlwaysShowUserChooser\" \"0\"", false)]
    [InlineData("\"OtherSetting\" \"1\"", false)]
    public void IsAccountChooserEnabled_RecognizesCurrentSetting(
        string source,
        bool expected)
    {
        SteamConfigEditor.IsAccountChooserEnabled(source).Should().Be(expected);
    }
}
