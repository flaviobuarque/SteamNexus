using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace SteamSwitcher.Core.Services;

public sealed class SteamInstallationService(
    IAppSettingsService settingsService,
    ILogger<SteamInstallationService> logger) : ISteamInstallationService
{
    private readonly List<SteamInstallation> _installations = [];
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);

    public IReadOnlyList<SteamInstallation> Installations => _installations;
    public SteamInstallation? SelectedInstallation { get; private set; }
    public event EventHandler? SelectedInstallationChanged;

    public async Task DiscoverAsync(CancellationToken ct = default)
    {
        await _discoveryGate.WaitAsync(ct);
        try
        {
            var candidates = DiscoverCandidatePaths(settingsService.Current);
            var discovered = new List<SteamInstallation>();

            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var installation = await BuildInstallationAsync(
                    candidate.Path, candidate.Registry, candidate.Custom, ct);
                if (installation is not null)
                    discovered.Add(installation);
            }

            _installations.Clear();
            _installations.AddRange(discovered);

            var configuredPath = settingsService.Current.SteamInstallPath;
            var previousId = SelectedInstallation?.Id;
            SelectedInstallation = _installations.FirstOrDefault(i =>
                    PathsEqual(i.RootPath, configuredPath))
                ?? _installations.FirstOrDefault(i => i.Id == previousId)
                ?? _installations.FirstOrDefault(i => i.IsValid)
                ?? _installations.FirstOrDefault();

            logger.LogInformation(
                "Instalações Steam detectadas: {Count}; selecionada={Selected}",
                _installations.Count,
                SelectedInstallation?.RootPath ?? "nenhuma");
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    public async Task SelectAsync(string installationId, CancellationToken ct = default)
    {
        var selected = _installations.FirstOrDefault(i => i.Id == installationId)
            ?? throw new InvalidOperationException("A instalação selecionada não foi encontrada.");
        if (!selected.IsValid)
            throw new InvalidOperationException("A instalação selecionada não está disponível.");

        if (SelectedInstallation?.Id == selected.Id) return;

        SelectedInstallation = selected;
        var settings = settingsService.Current;
        settings.SteamInstallPath = selected.RootPath;
        AddKnownPath(settings, selected.RootPath);
        await settingsService.SaveAsync(settings);
        SelectedInstallationChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddCustomPathAsync(string path, CancellationToken ct = default)
    {
        var normalized = NormalizeSteamRoot(path);
        var settings = settingsService.Current;
        AddKnownPath(settings, normalized);
        settings.SteamInstallPath = normalized;
        await settingsService.SaveAsync(settings);
        await DiscoverAsync(ct);

        var selected = _installations.FirstOrDefault(i => PathsEqual(i.RootPath, normalized));
        if (selected is null || !selected.IsValid)
            throw new InvalidOperationException(
                "O caminho não contém Steam.exe e config\\loginusers.vdf válidos.");

        SelectedInstallation = selected;
        SelectedInstallationChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveCustomPathAsync(string installationId, CancellationToken ct = default)
    {
        var installation = _installations.FirstOrDefault(i => i.Id == installationId)
            ?? throw new InvalidOperationException("A instalação não foi encontrada.");
        if (!installation.IsCustom)
            throw new InvalidOperationException("Uma instalação detectada pelo sistema não pode ser removida.");

        var settings = settingsService.Current;
        settings.KnownSteamInstallPaths.RemoveAll(path => PathsEqual(path, installation.RootPath));
        if (PathsEqual(settings.SteamInstallPath, installation.RootPath))
            settings.SteamInstallPath = null;
        await settingsService.SaveAsync(settings);
        await DiscoverAsync(ct);
        SelectedInstallationChanged?.Invoke(this, EventArgs.Empty);
    }

    public SteamOperationContext CaptureContext()
    {
        var installation = SelectedInstallation is { IsValid: true }
            ? SelectedInstallation
            : throw new InvalidOperationException("Nenhuma instalação válida da Steam está selecionada.");

        if (!File.Exists(installation.SteamExePath)
            || !File.Exists(installation.LoginUsersPath))
        {
            throw new InvalidOperationException(
                "A instalação selecionada foi removida ou está desconectada. Selecione outra instalação.");
        }

        return new SteamOperationContext(
            installation.Id,
            installation.RootPath,
            installation.SteamExePath,
            installation.LoginUsersPath,
            Path.Combine(installation.RootPath, "userdata"),
            Path.Combine(installation.RootPath, "steamapps", "libraryfolders.vdf"));
    }

    private static IEnumerable<(string Path, bool Registry, bool Custom)> DiscoverCandidatePaths(
        AppSettings settings)
    {
        var candidates = new List<(string, bool, bool)>();

        if (!string.IsNullOrWhiteSpace(settings.SteamInstallPath))
            candidates.Add((settings.SteamInstallPath, false, true));
        foreach (var path in settings.KnownSteamInstallPaths)
            candidates.Add((path, false, true));

        AddRegistryValue(candidates, Registry.CurrentUser,
            @"Software\Valve\Steam", "SteamPath");
        AddRegistryValue(candidates, Registry.CurrentUser,
            @"Software\Valve\Steam", "SteamExe", isExecutable: true);
        AddRegistryValue(candidates, Registry.LocalMachine,
            @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddRegistryValue(candidates, Registry.LocalMachine,
            @"SOFTWARE\Valve\Steam", "InstallPath");

        candidates.Add((@"C:\Program Files (x86)\Steam", false, false));
        candidates.Add((@"C:\Program Files\Steam", false, false));

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
            .Select(item => (NormalizePath(item.Item1), item.Item2, item.Item3))
            .DistinctBy(item => item.Item1, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddRegistryValue(
        ICollection<(string, bool, bool)> candidates,
        RegistryKey hive,
        string subKey,
        string valueName,
        bool isExecutable = false)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            var value = key?.GetValue(valueName)?.ToString();
            if (string.IsNullOrWhiteSpace(value)) return;
            candidates.Add((isExecutable ? Path.GetDirectoryName(value) ?? value : value, true, false));
        }
        catch
        {
            // Uma origem inacessível não invalida as demais.
        }
    }

    private static async Task<SteamInstallation?> BuildInstallationAsync(
        string rootPath,
        bool registryDefault,
        bool custom,
        CancellationToken ct)
    {
        var steamExe = Path.Combine(rootPath, "Steam.exe");
        var loginUsers = Path.Combine(rootPath, "config", "loginusers.vdf");
        if (!Directory.Exists(rootPath) && !custom) return null;

        var accountCount = 0;
        if (Directory.Exists(rootPath) && File.Exists(loginUsers))
        {
            try
            {
                await using var stream = new FileStream(
                    loginUsers, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, true);
                accountCount = SteamAccountSnapshotParser.Parse(stream).Accounts.Count;
            }
            catch
            {
                accountCount = 0;
            }
        }

        return new SteamInstallation
        {
            Id = CreateId(rootPath),
            RootPath = rootPath,
            SteamExePath = steamExe,
            LoginUsersPath = loginUsers,
            DisplayName = registryDefault ? "Steam principal" : $"Steam — {Path.GetFileName(rootPath)}",
            AccountCount = accountCount,
            IsRegistryDefault = registryDefault,
            IsCustom = custom,
            IsValid = File.Exists(steamExe) && File.Exists(loginUsers),
        };
    }

    private static string NormalizeSteamRoot(string path)
    {
        var normalized = NormalizePath(path);
        return string.Equals(Path.GetFileName(normalized), "steam.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(normalized)!
            : normalized;
    }

    private static void AddKnownPath(AppSettings settings, string path)
    {
        if (!settings.KnownSteamInstallPaths.Any(existing => PathsEqual(existing, path)))
            settings.KnownSteamInstallPaths.Add(path);
    }

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);

    private static string CreateId(string rootPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rootPath.ToUpperInvariant()));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
