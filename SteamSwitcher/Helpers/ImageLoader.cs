using System.Windows.Media.Imaging;

namespace SteamSwitcher.Helpers;

public static class ImageLoader
{
    private const int AvatarCacheCapacity = 150;
    private const int CoverCacheCapacity = 90;
    private static readonly BoundedImageCache _avatarCache = new(AvatarCacheCapacity);
    private static readonly BoundedImageCache _coverCache = new(CoverCacheCapacity);

    public static Task<BitmapImage?> LoadAvatarAsync(string? path) =>
        LoadAvatarBoundedAsync(path);

    public static Task<BitmapImage?> LoadCoverAsync(string? path) =>
        LoadBoundedAsync(path, 256, _coverCache);

    public static BitmapImage? GetCached(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_avatarCache.TryGet(path, out var a)) return a;
        if (_coverCache.TryGet(path, out var c)) return c;
        return null;
    }

    private static async Task<BitmapImage?> LoadAvatarBoundedAsync(string? path)
        => await LoadBoundedAsync(path, 96, _avatarCache);

    private static async Task<BitmapImage?> LoadBoundedAsync(
        string? path,
        int decodePixelWidth,
        BoundedImageCache cache)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (cache.TryGet(path, out var cached)) return cached;

        var image = await DecodeAsync(path, decodePixelWidth);
        cache.Set(path, image);
        return image;
    }

    private static async Task<BitmapImage?> DecodeAsync(string path, int decodePixelWidth)
    {
        try
        {
            return await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.None;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = decodePixelWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            });
        }
        catch
        {
            return null;
        }
    }

    private sealed class BoundedImageCache(int capacity)
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, CacheEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _usage = new();

        public bool TryGet(string path, out BitmapImage? image)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(path, out var entry))
                {
                    image = null;
                    return false;
                }

                _usage.Remove(entry.Node);
                _usage.AddFirst(entry.Node);
                image = entry.Image;
                return true;
            }
        }

        public void Set(string path, BitmapImage? image)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(path, out var existing))
                {
                    existing.Image = image;
                    _usage.Remove(existing.Node);
                    _usage.AddFirst(existing.Node);
                    return;
                }

                var node = _usage.AddFirst(path);
                _entries[path] = new CacheEntry(node, image);

                while (_entries.Count > capacity && _usage.Last is { } last)
                {
                    _usage.RemoveLast();
                    _entries.Remove(last.Value);
                }
            }
        }

        private sealed class CacheEntry(LinkedListNode<string> node, BitmapImage? image)
        {
            public LinkedListNode<string> Node { get; } = node;
            public BitmapImage? Image { get; set; } = image;
        }
    }
}
