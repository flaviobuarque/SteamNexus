using SkiaSharp;

namespace SteamSwitcher.Core.Helpers;

public static class CoverCompressor
{
    // Dimensoes nominais de capa Steam (2:3).
    private const int MaxWidth = 600;
    private const int MaxHeight = 900;
    private const int JpegQuality = 85;
    private const long MinInputBytes = 5 * 1024;

    /// <summary>
    /// Comprime e (se necessario) redimensiona a imagem de origem, salvando JPEG em destPath.
    /// Mantem proporcao original: so redimensiona se maior que MaxWidth x MaxHeight.
    /// </summary>
    public static bool TryCompress(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath)) return false;
        if (new FileInfo(sourcePath).Length < MinInputBytes) return false;

        try
        {
            using var src = SKBitmap.Decode(sourcePath);
            if (src is null) return false;

            // Redimensiona mantendo proporcao se exceder o tamanho nominal.
            SKBitmap target = src;
            if (src.Width > MaxWidth || src.Height > MaxHeight)
            {
                var scale = Math.Min(
                    (double)MaxWidth / src.Width,
                    (double)MaxHeight / src.Height);

                var newW = Math.Max(1, (int)(src.Width * scale));
                var newH = Math.Max(1, (int)(src.Height * scale));

                target = src.Resize(new SKImageInfo(newW, newH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                src.Dispose();
            }

            using var img = SKImage.FromBitmap(target);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var fs = File.Create(destPath);
            data.SaveTo(fs);

            return true;
        }
        catch
        {
            return false;
        }
    }
}