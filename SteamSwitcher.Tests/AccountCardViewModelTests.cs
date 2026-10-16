using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.ViewModels;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class AccountCardViewModelTests
{
    [Fact]
    public void FormatLastLogin_FormatsRelativeTimes_WithCorrectSingularAndPlural()
    {
        var now = DateTimeOffset.UtcNow;

        AccountCardViewModel.FormatLastLogin(0).Should().Be("Nunca");
        AccountCardViewModel.FormatLastLogin(-1).Should().Be("Nunca");

        AccountCardViewModel.FormatLastLogin(now.ToUnixTimeSeconds()).Should().Be("Hoje");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-1).ToUnixTimeSeconds()).Should().Be("Ontem");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-2).ToUnixTimeSeconds()).Should().Be("2 dias atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-5).ToUnixTimeSeconds()).Should().Be("5 dias atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-7).ToUnixTimeSeconds()).Should().Be("1 semana atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-14).ToUnixTimeSeconds()).Should().Be("2 semanas atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-21).ToUnixTimeSeconds()).Should().Be("3 semanas atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-35).ToUnixTimeSeconds()).Should().Be("1 mês atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-65).ToUnixTimeSeconds()).Should().Be("2 meses atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-120).ToUnixTimeSeconds()).Should().Be("4 meses atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-370).ToUnixTimeSeconds()).Should().Be("1 ano atrás");
        AccountCardViewModel.FormatLastLogin(now.AddDays(-750).ToUnixTimeSeconds()).Should().Be("2 anos atrás");
    }

    [Fact]
    public void FormatExactLogin_FormatsExactDateTimeAndHandlesZero()
    {
        AccountCardViewModel.FormatExactLogin(0).Should().Be("Nenhum login registrado");

        var testEpoch = 1792182064L;
        var expectedLocal = DateTimeOffset.FromUnixTimeSeconds(testEpoch).LocalDateTime;
        var tooltip = AccountCardViewModel.FormatExactLogin(testEpoch);

        tooltip.Should().Be($"Último login: {expectedLocal:dd/MM/yyyy 'às' HH:mm:ss}");
    }

    [Fact]
    public void AccountCardViewModel_ExposesExactTooltipProperty()
    {
        var testEpoch = 1792182064L;
        var account = new SteamAccount
        {
            SteamId64 = "76561198976344182",
            AccountName = "flaviobuarq",
            Timestamp = testEpoch,
        };

        var vm = new AccountCardViewModel(account);
        vm.LastLoginExactTooltip.Should().StartWith("Último login: ");
        vm.LastLoginExactTooltip.Should().Contain(DateTimeOffset.FromUnixTimeSeconds(testEpoch).LocalDateTime.ToString("dd/MM/yyyy"));
    }
}
