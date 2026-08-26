using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Services.Themes;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class CustomThemeManagerTests
{
    [Fact]
    public void Validate_AcceptsBuiltInThemeDefaults()
    {
        var action = () => CustomThemeManager.Validate(CustomThemeSettings.CreateDark());
        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsInvalidColor()
    {
        var theme = CustomThemeSettings.CreateDark();
        theme.Accent = "azul";
        var action = () => CustomThemeManager.Validate(theme);
        action.Should().Throw<InvalidDataException>().WithMessage("*Cor inválida*");
    }

    [Fact]
    public void ContrastRatio_IdentifiesHighContrast()
    {
        CustomThemeManager.ContrastRatio("#FFFFFF", "#000000").Should().BeApproximately(21, 0.01);
    }

    [Fact]
    public async Task ThemePackage_RoundTripsManifestAndBackground()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SteamNexusThemeTests-{Guid.NewGuid():N}");
        string? importedBackground = null;
        Directory.CreateDirectory(root);
        try
        {
            var image = Path.Combine(root, "background.png");
            await File.WriteAllBytesAsync(image, [137, 80, 78, 71]);
            var package = Path.Combine(root, "theme.steamnexus-theme");
            var theme = CustomThemeSettings.CreateDark();
            theme.Name = "Teste";
            theme.BackgroundImagePath = image;

            await CustomThemeManager.ExportAsync(theme, package);
            var imported = await CustomThemeManager.ImportAsync(package);
            importedBackground = imported.BackgroundImagePath;

            imported.Name.Should().Be("Teste");
            imported.BackgroundImagePath.Should().NotBeNull();
            File.Exists(imported.BackgroundImagePath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
            if (!string.IsNullOrWhiteSpace(importedBackground) && File.Exists(importedBackground))
                File.Delete(importedBackground);
        }
    }
}
