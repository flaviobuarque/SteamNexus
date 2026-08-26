namespace SteamSwitcher.Core.Models;

public sealed record SteamInstallation
{
    public required string Id { get; init; }
    public required string RootPath { get; init; }
    public required string SteamExePath { get; init; }
    public required string LoginUsersPath { get; init; }
    public string DisplayName { get; init; } = "Steam";
    public bool HasCustomDisplayName { get; init; }
    public int AccountCount { get; init; }
    public bool IsRegistryDefault { get; init; }
    public bool IsCustom { get; init; }
    public bool IsValid { get; init; }
    public bool HasLoginUsersFile { get; init; }
    public bool IsSelected { get; init; }

    public string StatusText => IsValid && !HasLoginUsersFile
        ? "loginusers.vdf ausente — recuperação disponível"
        : IsValid
        ? AccountCount == 1 ? "1 conta encontrada" : $"{AccountCount} contas encontradas"
        : "Instalação indisponível";
}

public sealed record SteamOperationContext(
    string InstallationId,
    string RootPath,
    string SteamExePath,
    string LoginUsersPath,
    string UserDataPath,
    string LibraryFoldersPath);
