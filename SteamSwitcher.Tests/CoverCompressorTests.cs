using FluentAssertions;
using SkiaSharp;
using SteamSwitcher.Core.Helpers;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class CoverCompressorTests
{
    [Fact]
    public void TryCompress_ReturnsFalse_WhenFileDoesNotExist()
    {
        var result = CoverCompressor.TryCompress("non_existent_file.png", "output.jpg");
        result.Should().BeFalse();
    }

    [Fact]
    public void TryCompress_ResizesAndDisposes_WhenImageExceedsDimensions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cover-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceFile = Path.Combine(tempDir, "large.png");
        var destFile = Path.Combine(tempDir, "compressed.jpg");

        try
        {
            // Create a bitmap larger than MaxWidth (600) x MaxHeight (900)
            using (var bmp = new SKBitmap(800, 1200))
            {
                using var canvas = new SKCanvas(bmp);
                canvas.Clear(SKColors.CornflowerBlue);
                using var image = SKImage.FromBitmap(bmp);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var fs = File.Create(sourceFile);
                data.SaveTo(fs);
            }

            var success = CoverCompressor.TryCompress(sourceFile, destFile);
            success.Should().BeTrue();
            File.Exists(destFile).Should().BeTrue();

            using var resultBmp = SKBitmap.Decode(destFile);
            resultBmp.Should().NotBeNull();
            resultBmp.Width.Should().BeLessThanOrEqualTo(600);
            resultBmp.Height.Should().BeLessThanOrEqualTo(900);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
