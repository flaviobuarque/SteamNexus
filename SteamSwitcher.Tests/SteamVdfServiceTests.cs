using FluentAssertions;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamVdfServiceTests
{
    [Fact]
    public void WriteAtomically_CreatesFileAndBackup_WhenTargetExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"vdf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var targetFile = Path.Combine(tempDir, "loginusers.vdf");
        var initialContent = "initial content"u8.ToArray();
        var updatedContent = "updated content"u8.ToArray();

        try
        {
            File.WriteAllBytes(targetFile, initialContent);

            var service = new SteamVdfService();
            service.WriteAtomically(targetFile, updatedContent, createBackup: true);

            File.Exists(targetFile).Should().BeTrue();
            File.ReadAllBytes(targetFile).Should().Equal(updatedContent);

            var backupFile = targetFile + "_last";
            File.Exists(backupFile).Should().BeTrue();
            File.ReadAllBytes(backupFile).Should().Equal(initialContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
