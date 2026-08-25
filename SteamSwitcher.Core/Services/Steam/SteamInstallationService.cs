using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace SteamSwitcher.Core.Services;

public sealed class SteamInstallationService(
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
            var candidates = DiscoverCandidatePaths();
            var discovered = new List<SteamInstallation>();

            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var installation = await BuildInstallationAsync(candidate.Path, candidate.Registry, ct);
                if (installation is not null)
                    discovered.Add(installation);
            }

            _installations.Clear();
            _installations.AddRange(discovered);

            var previousId = SelectedInstallation?.Id;
            SelectedInstallation = _installations.FirstOrDefault(i => i.Id == previousId)
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

    public SteamOperationContext CaptureContext()
    {
        var installation = SelectedInstallation is { IsValid: true }
            ? SelectedInstallation
            : throw new InvalidOperationException("Nenhuma instalação válida da Steam está selecionada.");

        return new SteamOperationContext(
            installation.Id,
            installation.RootPath,
            installation.SteamExePath,
            installation.LoginUsersPath,
            Path.Combine(installation.RootPath, "userdata"),
            Path.Combine(installation.RootPath, "steamapps", "libraryfolders.vdf"));
    }

    private static IEnumerable<(string Path, bool Registry)> DiscoverCandidatePaths()
    {
        var candidates = new List<(string, bool)>();

        AddRegistryValue(candidates, Registry.CurrentUser,
            @"Software\Valve\Steam", "SteamPath");
        AddRegistryValue(candidates, Registry.CurrentUser,
            @"Software\Valve\Steam", "SteamExe", isExecutable: true);
        AddRegistryValue(candidates, Registry.LocalMachine,
            @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddRegistryValue(candidates, Registry.LocalMachine,
            @"SOFTWARE\Valve\Steam", "InstallPath");

        candidates.Add((@"C:\Program Files (x86)\Steam", false));
        candidates.Add((@"C:\Program Files\Steam", false));

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
            .Select(item => (NormalizePath(item.Item1), item.Item2))
            .DistinctBy(item => item.Item1, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddRegistryValue(
        ICollection<(string, bool)> candidates,
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
            candidates.Add((isExecutable ? Path.GetDirectoryName(value) ?? value : value, true));
        }
        catch
        {
            // Uma origem inacessível não invalida as demais.
        }
    }

    private static async Task<SteamInstallation?> BuildInstallationAsync(
        string rootPath,
        bool registryDefault,
        CancellationToken ct)
    {
        var steamExe = Path.Combine(rootPath, "Steam.exe");
        var loginUsers = Path.Combine(rootPath, "config", "loginusers.vdf");
        if (!Directory.Exists(rootPath)) return null;

        var accountCount = 0;
        if (File.Exists(loginUsers))
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
            IsValid = File.Exists(steamExe) && File.Exists(loginUsers),
        };
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);

    private static string CreateId(string rootPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rootPath.ToUpperInvariant()));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
