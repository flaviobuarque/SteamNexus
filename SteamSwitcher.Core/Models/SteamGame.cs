namespace SteamSwitcher.Core.Models;

public class SteamGame
{
    public required string AppId { get; init; }
    public required string Name { get; init; }
    public string InstallDir { get; init; } = string.Empty;
    public string LibraryPath { get; set; } = string.Empty;
    public long SizeOnDisk { get; set; }

    // Qual conta tem esse jogo (LastOwner do appmanifest)
    public string? OwnerSteamId64 { get; set; }
    public SteamAccount? OwnerAccount { get; set; }

    // Imagens
    public string? CoverUrl { get; set; }
    public string? HeroCoverUrl { get; set; }
    public string? CoverLocalPath { get; set; }

    // Stats (do localconfig.vdf)
    public int PlaytimeMinutes { get; set; }
    public int PlaytimeForever => PlaytimeMinutes;
    public string PlaytimeFormatted => FormatPlaytime(PlaytimeMinutes);

    public string SizeFormatted
    {
        get
        {
            if (SizeOnDisk <= 0) return string.Empty;
            if (SizeOnDisk >= 1_073_741_824)
                return $"{SizeOnDisk / 1_073_741_824.0:F1} GB";
            return $"{SizeOnDisk / 1_048_576.0:F0} MB";
        }
    }

    public string DriveLetter =>
        string.IsNullOrEmpty(LibraryPath) ? string.Empty
        : Path.GetPathRoot(LibraryPath)?.TrimEnd('\\') ?? string.Empty;

    public string SizeAndDrive =>
        string.IsNullOrEmpty(SizeFormatted) ? string.Empty
        : string.IsNullOrEmpty(DriveLetter) ? SizeFormatted
        : $"{SizeFormatted} • {DriveLetter}";

    private static string FormatPlaytime(int minutes)
    {
        if (minutes <= 0) return "Nunca jogado";
        if (minutes < 60) return $"{minutes}min";
        var hours = minutes / 60;
        return hours >= 1000 ? $"{hours:N0}h" : $"{hours}h";
    }

    /// <summary>Caminho completo da pasta do jogo. Ex: D:\SteamLibrary\steamapps\common\Elden Ring</summary>
    public string InstallFullPath =>
        string.IsNullOrEmpty(LibraryPath) || string.IsNullOrEmpty(InstallDir)
            ? string.Empty
            : Path.Combine(LibraryPath, "steamapps", "common", InstallDir).ToLowerInvariant();
}