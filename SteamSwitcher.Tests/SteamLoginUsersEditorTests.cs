using System.Text;
using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using ValveKeyValue;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamLoginUsersEditorTests
{
    private const string FirstId = "76561198000000001";
    private const string SecondId = "76561198000000002";

    [Fact]
    public void Rewrite_SelectsExactlyOneAccountAndClearsPreviousIndicators()
    {
        using var output = Rewrite(CreateVdf(), SecondId, LoginState.Online);

        var snapshot = SteamAccountSnapshotParser.Parse(output);
        var first = snapshot.Accounts.Single(a => a.SteamId64 == FirstId);
        var second = snapshot.Accounts.Single(a => a.SteamId64 == SecondId);

        first.AutoLogin.Should().BeFalse();
        first.MostRecent.Should().BeFalse();
        first.RememberPassword.Should().BeFalse();
        first.WantsOfflineMode.Should().BeFalse();

        second.AutoLogin.Should().BeTrue();
        second.MostRecent.Should().BeTrue();
        second.RememberPassword.Should().BeTrue();
        second.WantsOfflineMode.Should().BeFalse();
        snapshot.ActiveAccount.Should().BeSameAs(second);
    }

    [Fact]
    public void Rewrite_InsertsMissingLoginFields()
    {
        var source = $$"""
            "users"
            {
                "{{FirstId}}"
                {
                    "AccountName" "first"
                    "PersonaName" "First"
                }
                "{{SecondId}}"
                {
                    "AccountName" "second"
                    "PersonaName" "Second"
                }
            }
            """;

        using var output = Rewrite(source, SecondId, LoginState.Online);
        var document = Deserialize(output);

        foreach (var user in document.Children)
        {
            user.Children.Select(child => child.Name).Should().Contain(
                ["MostRecent", "AutoLogin", "RememberPassword", "WantsOfflineMode", "SkipOfflineModeWarning"]);
        }
    }

    [Fact]
    public void Rewrite_EnablesOfflineModeOnlyForTarget()
    {
        using var output = Rewrite(CreateVdf(), SecondId, LoginState.Offline);
        var document = Deserialize(output);
        var first = document.Children.Single(user => user.Name == FirstId);
        var second = document.Children.Single(user => user.Name == SecondId);

        Value(first, "WantsOfflineMode").Should().Be("0");
        Value(first, "SkipOfflineModeWarning").Should().Be("0");
        Value(second, "WantsOfflineMode").Should().Be("1");
        Value(second, "SkipOfflineModeWarning").Should().Be("1");
    }

    [Fact]
    public void Rewrite_UpdatesExistingFieldsCaseInsensitivelyWithoutDuplicates()
    {
        var source = $$"""
            "users"
            {
                "{{FirstId}}"
                {
                    "AccountName" "first"
                    "PersonaName" "First"
                    "autologin" "1"
                    "mostrecent" "1"
                    "rememberpassword" "1"
                }
                "{{SecondId}}"
                {
                    "AccountName" "second"
                    "PersonaName" "Second"
                }
            }
            """;

        using var output = Rewrite(source, SecondId, LoginState.Online);
        var document = Deserialize(output);
        var first = document.Children.Single(user => user.Name == FirstId);

        first.Children.Count(child => child.Name.Equals("AutoLogin", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        Value(first, "AutoLogin").Should().Be("0");
        Value(first, "MostRecent").Should().Be("0");
        Value(first, "RememberPassword").Should().Be("0");
    }

    [Fact]
    public void Rewrite_ThrowsAndProducesNoOutputWhenTargetDoesNotExist()
    {
        using var input = StreamOf(CreateVdf());
        using var output = new MemoryStream();

        var action = () => SteamLoginUsersEditor.Rewrite(
            input, output, "76561198999999999", LoginState.Online);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*não foi encontrada*");
        output.Length.Should().Be(0);
    }

    [Fact]
    public void Rewrite_RestoresKnownTargetWhenMissingFromVdf()
    {
        const string restoredId = "76561198999999999";
        using var input = StreamOf(CreateVdf());
        using var output = new MemoryStream();
        var known = new SteamAccount
        {
            SteamId64 = restoredId,
            AccountName = "restored_login",
            PersonaName = "Restored",
            Timestamp = 1234,
        };

        SteamLoginUsersEditor.Rewrite(
            input, output, restoredId, LoginState.Online, known);
        output.Position = 0;

        var snapshot = SteamAccountSnapshotParser.Parse(output);
        var restored = snapshot.Accounts.Single(a => a.SteamId64 == restoredId);
        restored.AccountName.Should().Be("restored_login");
        restored.AutoLogin.Should().BeTrue();
        restored.MostRecent.Should().BeTrue();
        restored.RememberPassword.Should().BeTrue();
    }

    [Theory]
    [InlineData(LoginState.Online, false)]
    [InlineData(LoginState.Offline, true)]
    public void Create_BuildsValidVdfForArchivedAccount(
        LoginState state,
        bool expectsOffline)
    {
        var account = new SteamAccount
        {
            SteamId64 = FirstId,
            AccountName = "restored_login",
            PersonaName = "Restored",
            Timestamp = 1234,
        };
        using var output = new MemoryStream();

        SteamLoginUsersEditor.Create(output, account, state);
        output.Position = 0;

        var snapshot = SteamAccountSnapshotParser.Parse(output);
        var restored = snapshot.Accounts.Should().ContainSingle().Subject;
        restored.SteamId64.Should().Be(FirstId);
        restored.AccountName.Should().Be("restored_login");
        restored.AutoLogin.Should().BeTrue();
        restored.MostRecent.Should().BeTrue();
        restored.RememberPassword.Should().BeTrue();
        restored.WantsOfflineMode.Should().Be(expectsOffline);
        snapshot.ActiveAccount.Should().BeSameAs(restored);
    }

    [Fact]
    public void Rewrite_RejectsInvalidVdf()
    {
        using var input = StreamOf("not a valid { vdf");
        using var output = new MemoryStream();

        var action = () => SteamLoginUsersEditor.Rewrite(
            input, output, FirstId, LoginState.Online);

        action.Should().Throw<Exception>();
        output.Length.Should().Be(0);
    }

    private static MemoryStream Rewrite(string source, string target, LoginState state)
    {
        using var input = StreamOf(source);
        var output = new MemoryStream();
        SteamLoginUsersEditor.Rewrite(input, output, target, state);
        output.Position = 0;
        return output;
    }

    private static KVDocument Deserialize(Stream stream)
    {
        stream.Position = 0;
        return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
    }

    private static string Value(KVObject parent, string key) =>
        parent.Children.Single(child =>
            child.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Value.ToString()
            ?? string.Empty;

    private static MemoryStream StreamOf(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static string CreateVdf() => $$"""
        "users"
        {
            "{{FirstId}}"
            {
                "AccountName" "first"
                "PersonaName" "First"
                "RememberPassword" "1"
                "MostRecent" "1"
                "AutoLogin" "1"
                "WantsOfflineMode" "1"
                "SkipOfflineModeWarning" "1"
            }
            "{{SecondId}}"
            {
                "AccountName" "second"
                "PersonaName" "Second"
                "RememberPassword" "1"
                "MostRecent" "0"
                "AutoLogin" "0"
                "WantsOfflineMode" "0"
                "SkipOfflineModeWarning" "0"
            }
        }
        """;
}
