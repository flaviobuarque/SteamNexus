using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace SteamSwitcher.Helpers;

public static class ImageLoader
{
    private static readonly ConcurrentDictionary<string, BitmapImage?> _avatarCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, BitmapImage?> _coverCache = new(StringComparer.OrdinalIgnoreCase);

    public static Task<BitmapImage?> LoadAvatarAsync(string? path) =>
        LoadAsync(path, 256, _avatarCache);

    public static Task<BitmapImage?> LoadCoverAsync(string? path) =>
        LoadAsync(path, 512, _coverCache);

    public static BitmapImage? GetCached(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_avatarCache.TryGetValue(path, out var a)) return a;
        if (_coverCache.TryGetValue(path, out var c)) return c;
        return null;
    }

    private static async Task<BitmapImage?> LoadAsync(
        string? path,
        int decodePixelWidth,
        ConcurrentDictionary<string, BitmapImage?> cache)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (cache.TryGetValue(path, out var cached)) return cached;

        try
        {
            return await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.None;
                bmp.UriSource = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(path, UriKind.Absolute)
                    : new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = decodePixelWidth;
                bmp.EndInit();
                bmp.Freeze();
                cache.TryAdd(path, bmp);
                return bmp;
            });
        }
        catch
        {
            cache.TryAdd(path, null);
            return null;
        }
    }
}