using Microsoft.Extensions.Logging;
using System.Text.Json;

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
        // Abrimos apenas a pagina oficial de download do Steam no navegador padrao.
        // Antes baixavamos SteamSetup.exe e o executavamos com /S /runas, padrao
        // heuristico identico ao de "droppers" — alvo de antivirrus. O usuario
        // instala manualmente a partir dai.
        SetCorruptedInstallFlag(true);
        progress.Report(50);

        try
        {
            var url = "https://store.steampowered.com/about/";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            progress.Report(100);

            // Aguarda alguns segundos para o usuario iniciar o download se quiser.
            await Task.Delay(TimeSpan.FromSeconds(2), ct);

            // Sinaliza false: a instalacao nao foi confirmada por nos; o usuario
            // concluir manualmente e o onboarding re-detecta o Steam na proxima exec.
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao abrir pagina de download do Steam");
            return false;
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