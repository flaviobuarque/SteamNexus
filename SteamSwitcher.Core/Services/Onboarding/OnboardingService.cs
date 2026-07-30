using System.Text.Json;
using Microsoft.Extensions.Logging;
using SteamSwitcher.Core.Models;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public class OnboardingService : IOnboardingService
{
    private readonly ISteamLocatorService _locator;
    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<OnboardingService> _logger;
    private OnboardingFlags _flags = new();

    private static readonly string _flagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "onboarding.json");

    public OnboardingService(
        ISteamLocatorService locator,
        IAppSettingsService settingsService,
        ILogger<OnboardingService> logger)
    {
        _locator = locator;
        _settingsService = settingsService;
        _logger = logger;
        LoadFlagsSync();
    }

    public bool IsFirstRun => !_flags.Completed;
    public bool HasCorruptedInstallFlag => _flags.CorruptedInstall;
    private void LoadFlagsSync()
    {
        if (!File.Exists(_flagPath)) return;
        try
        {
            var json = File.ReadAllText(_flagPath); // síncrono
            _flags = JsonSerializer.Deserialize<OnboardingFlags>(json) ?? new();
        }
        catch { _flags = new(); }
    }
    public void CompleteOnboarding()
    {
        _flags.Completed = true;
        _flags.CorruptedInstall = false;
        _ = SaveFlagsAsync();
    }

    public void SetCorruptedInstallFlag(bool value)
    {
        _flags.CorruptedInstall = value;
        _ = SaveFlagsAsync();
    }

    public async Task<bool> TryImportFromTcNoAsync(CancellationToken ct = default)
    {
        var tcnoPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TcNo Account Switcher");

        if (!Directory.Exists(tcnoPath))
        {
            _logger.LogInformation("TcNo não encontrado em {Path}", tcnoPath);
            return false;
        }

        var steamCachePath = Path.Combine(tcnoPath, "LoginCache", "Steam", "LoginCache.json");
        if (!File.Exists(steamCachePath)) return false;

        _logger.LogInformation("TcNo encontrado, importando contas de {Path}", steamCachePath);
        return true;
    }

    public async Task<IReadOnlyList<DriveInfo>> GetSuitableDrivesAsync()
    {
        return await Task.Run(() =>
            DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .OrderByDescending(d => d.AvailableFreeSpace)
                .ToList());
    }

    public async Task<bool> InstallSteamAsync(
        string targetDrive,
        IProgress<int> progress,
        CancellationToken ct = default)
    {
        SetCorruptedInstallFlag(true);
        var installerPath = Path.Combine(Path.GetTempPath(), "SteamSetup.exe");

        try
        {
            progress.Report(5);
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(
                "https://cdn.cloudflare.steamstatic.com/client/installer/SteamSetup.exe", ct);
            await File.WriteAllBytesAsync(installerPath, bytes, ct);
            progress.Report(50);

            var targetPath = Path.Combine(targetDrive, "Steam");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"/S /D={targetPath}",
                UseShellExecute = true,
                Verb = "runas"
            };

            var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync(ct);
            progress.Report(90);

            var steamExe = Path.Combine(targetPath, "Steam.exe");
            if (File.Exists(steamExe))
            {
                SetCorruptedInstallFlag(false);
                progress.Report(100);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao instalar Steam");
            return false;
        }
        finally
        {
            if (File.Exists(installerPath))
                File.Delete(installerPath);
        }
    }

    public async Task UninstallSteamAsync(CancellationToken ct = default)
    {
        var steamPath = _locator.FindSteamInstallPath();
        if (string.IsNullOrEmpty(steamPath)) return;

        var steamExe = Path.Combine(steamPath, "Steam.exe");
        if (!File.Exists(steamExe)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = steamExe,
            Arguments = "/uninstall",
            UseShellExecute = true
        });
    }

    private async Task SaveFlagsAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_flagPath)!);
        var json = JsonSerializer.Serialize(_flags, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_flagPath, json);
    }

    private class OnboardingFlags
    {
        public bool Completed { get; set; }
        public bool CorruptedInstall { get; set; }
    }
}