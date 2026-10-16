using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SteamSwitcher.Converters;

/// <summary>Small local-cache thumbnails only; never initiate network requests from search.</summary>
public sealed class SearchThumbnailConverter : IValueConverter
{
    private readonly Dictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(path)) return null;
        if (_cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 80;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            if (_cache.Count >= 128) _cache.Clear();
            _cache[path] = image;
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
