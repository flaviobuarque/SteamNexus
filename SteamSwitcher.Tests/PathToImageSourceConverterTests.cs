using System.Globalization;
using FluentAssertions;
using SteamSwitcher.Converters;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class PathToImageSourceConverterTests
{
    [Fact]
    public void Convert_ReturnsNull_WhenPathIsNullOrEmpty()
    {
        var converter = new PathToImageSourceConverter();

        var result1 = converter.Convert(null!, typeof(object), null!, CultureInfo.InvariantCulture);
        var result2 = converter.Convert(string.Empty, typeof(object), null!, CultureInfo.InvariantCulture);

        result1.Should().BeNull();
        result2.Should().BeNull();
    }

    [Fact]
    public void Convert_ReturnsNull_WhenPathDoesNotExist()
    {
        var converter = new PathToImageSourceConverter();

        var result = converter.Convert(@"C:\non_existent_folder_xyz\image.jpg", typeof(object), null!, CultureInfo.InvariantCulture);

        result.Should().BeNull();
    }
}
