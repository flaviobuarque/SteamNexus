namespace SteamSwitcher.Core.Services;

public interface IImageCacheService
{
    string? TryGetCachedPath(string url);
    string? TryGetString(string key);
    Task<string?> GetCachedPathAsync(string url, CancellationToken ct = default);
    Task<string?> GetStringAsync(string key);
    Task SetStringAsync(string key, string value, TimeSpan expiry);
    string GetCacheDirectory();
    Task ClearCacheAsync();
    Task<long> GetCacheSizeAsync();
}
