using SteamSwitcher.Helpers;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SteamSwitcher.Converters;

// Bool → Visibility
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

// Bool → Visibility (invertido)
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

// String vazia → Visibility
public class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

// Path local → BitmapImage (com fallback gracioso)
// Prioriza o cache compartilhado do ImageLoader (pré-decodado e frozen).
// Em cache miss síncrono, decodifica uma única vez com resolução delimitada para economia drástica de RAM.
public class PathToImageSourceConverter : IValueConverter
{
    public int DefaultDecodePixelWidth { get; set; } = 256;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrEmpty(path)) return null;

        var cached = ImageLoader.GetCached(path);
        if (cached is not null) return cached;

        var decodeWidth = DefaultDecodePixelWidth;
        if (parameter is int intWidth && intWidth > 0)
        {
            decodeWidth = intWidth;
        }
        else if (parameter is string strWidth && int.TryParse(strWidth, out var parsedWidth) && parsedWidth > 0)
        {
            decodeWidth = parsedWidth;
        }

        try
        {
            var uri = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(path)
                : new Uri(path, UriKind.Absolute);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            if (decodeWidth > 0)
            {
                bitmap.DecodePixelWidth = decodeWidth;
            }
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

// Enum → bool (para RadioButton)
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.Equals(parameter) ?? false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return new GridLength(1, GridUnitType.Star);
        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}