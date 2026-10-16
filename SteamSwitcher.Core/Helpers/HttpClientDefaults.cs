using System.Net;

namespace SteamSwitcher.Core.Helpers;

public static class HttpClientDefaults
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        AutomaticDecompression = DecompressionMethods.All
    };

    public static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(CreateHandler(), disposeHandler: true);
        if (timeout.HasValue)
            client.Timeout = timeout.Value;
        return client;
    }
}
