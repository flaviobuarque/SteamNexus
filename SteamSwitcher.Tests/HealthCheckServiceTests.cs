using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class HealthCheckServiceTests
{
    private sealed class MockHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Network error");
    }

    [Fact]
    public async Task CheckAccountAsync_ReportsClean_WhenXmlIndicatesNoBan()
    {
        var xml = "<profile><vacBanned>0</vacBanned><isLimitedAccount>0</isLimitedAccount></profile>";
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml)
        });
        var http = new HttpClient(handler);
        var settingsMock = Substitute.For<IAppSettingsService>();
        settingsMock.Current.Returns(new AppSettings());

        var service = new HealthCheckService(settingsMock, NullLogger<HealthCheckService>.Instance, http);
        var account = new SteamAccount { SteamId64 = "76561198000000001", AccountName = "test", RememberPassword = true };

        var result = await service.CheckAccountAsync(account);
        result.IsCheckSuccessful.Should().BeTrue();
        result.IsVacBanned.Should().BeFalse();
        result.IsLimitedAccount.Should().BeFalse();
        result.HasValidSession.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccountAsync_ReportsUnknown_WhenNetworkFails()
    {
        var http = new HttpClient(new FailingHttpMessageHandler());
        var settingsMock = Substitute.For<IAppSettingsService>();
        settingsMock.Current.Returns(new AppSettings());

        var service = new HealthCheckService(settingsMock, NullLogger<HealthCheckService>.Instance, http);
        var account = new SteamAccount { SteamId64 = "76561198000000001", AccountName = "test", RememberPassword = true };

        var result = await service.CheckAccountAsync(account);
        result.IsCheckSuccessful.Should().BeFalse();
        result.IsVacBanned.Should().BeNull();
        result.IsLimitedAccount.Should().BeNull();
        result.IsBanned.Should().BeFalse();
    }
}
