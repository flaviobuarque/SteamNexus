using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public class HealthCheckService : IHealthCheckService
{
    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _throttle = new(3, 3);

    public HealthCheckService(
        IAppSettingsService settingsService,
        ILogger<HealthCheckService> logger,
        HttpClient? httpClient = null)
    {
        _settingsService = settingsService;
        _logger = logger;
        _http = httpClient ?? HttpClientDefaults.CreateClient(TimeSpan.FromSeconds(10));
    }

    public async Task<AccountHealth> CheckAccountAsync(
        SteamAccount account,
        CancellationToken ct = default)
    {
        var health = new AccountHealth { SteamId64 = account.SteamId64 };

        // Sessão válida = account está no loginusers.vdf com RememberPassword=1
        health.HasValidSession = account.RememberPassword;

        // VAC/ban via XML público (sem API key)
        await CheckBansViaXmlAsync(health, account.SteamId64, ct);

        // Com API key: mais preciso via GetPlayerBans
        if (FeatureFlags.SteamWebApiKey)
        {
            var apiKey = _settingsService.Current.SteamApiKey;
            if (!string.IsNullOrEmpty(apiKey))
                await CheckBansViaApiAsync(health, account.SteamId64, apiKey, ct);
        }

        return health;
    }

    public async Task<IReadOnlyList<AccountHealth>> CheckAllAccountsAsync(
        IReadOnlyList<SteamAccount> accounts,
        CancellationToken ct = default)
    {
        var tasks = accounts.Select(async a =>
        {
            await _throttle.WaitAsync(ct);
            try
            {
                return await CheckAccountAsync(a, ct);
            }
            finally
            {
                _throttle.Release();
            }
        });
        return await Task.WhenAll(tasks);
    }

    private async Task CheckBansViaXmlAsync(
        AccountHealth health, string steamId64, CancellationToken ct)
    {
        try
        {
            var url = $"https://steamcommunity.com/profiles/{steamId64}/?xml=1";
            var xml = await _http.GetStringAsync(url, ct);

            health.IsVacBanned = xml.Contains("<vacBanned>1</vacBanned>");
            health.IsLimitedAccount = xml.Contains("<isLimitedAccount>1</isLimitedAccount>");
            health.IsCheckSuccessful = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Erro ao checar bans via XML para {Id}", steamId64);
            health.IsVacBanned = null;
            health.IsLimitedAccount = null;
            health.IsCheckSuccessful = false;
        }
    }

    private async Task CheckBansViaApiAsync(
        AccountHealth health, string steamId64, string apiKey, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerBans/v1/" +
                      $"?key={apiKey}&steamids={steamId64}";
            var response = await _http.GetStringAsync(url, ct);
            var json = JsonNode.Parse(response);
            var player = json?["players"]?[0];

            if (player is null) return;

            health.IsVacBanned = player["VACBanned"]?.GetValue<bool>() ?? false;
            health.IsGameBanned = (player["NumberOfGameBans"]?.GetValue<int>() ?? 0) > 0;
            health.IsCommunityBanned = player["CommunityBanned"]?.GetValue<bool>() ?? false;
            health.IsCheckSuccessful = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Erro ao checar bans via API para {Id}", steamId64);
            if (!health.IsCheckSuccessful)
            {
                health.IsVacBanned = null;
                health.IsGameBanned = null;
                health.IsCommunityBanned = null;
            }
        }
    }
}