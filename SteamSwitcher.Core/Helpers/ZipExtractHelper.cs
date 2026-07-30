using System.IO.Compression;

namespace SteamSwitcher.Core.Helpers;

public static class ZipExtractHelper
{
    public static bool TryGetSafeExtractPath(string destRoot, string entryName, out string destPath)
    {
        destPath = string.Empty;
        if (string.IsNullOrWhiteSpace(entryName)) return false;

        var relative = entryName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(relative)) return false;

        destPath = Path.GetFullPath(Path.Combine(destRoot, relative));
        var rootPrefix = Path.GetFullPath(destRoot.TrimEnd(Path.DirectorySeparatorChar))
            + Path.DirectorySeparatorChar;

        return destPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(destPath, Path.GetFullPath(destRoot), StringComparison.OrdinalIgnoreCase);
    }

    public static void ExtractEntry(ZipArchiveEntry entry, string destRoot)
    {
        if (string.IsNullOrEmpty(entry.Name)) return;

        if (!TryGetSafeExtractPath(destRoot, entry.FullName, out var dest))
            throw new InvalidDataException($"Entrada de zip inválida: {entry.FullName}");

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        entry.ExtractToFile(dest, overwrite: true);
    }
}
