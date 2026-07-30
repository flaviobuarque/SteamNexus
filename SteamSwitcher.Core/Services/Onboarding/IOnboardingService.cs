namespace SteamSwitcher.Core.Services;

public interface IOnboardingService
{
    bool IsFirstRun { get; }
    bool HasCorruptedInstallFlag { get; }
    void CompleteOnboarding();
    void SetCorruptedInstallFlag(bool value);
    Task<bool> TryImportFromTcNoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DriveInfo>> GetSuitableDrivesAsync();
    Task<bool> InstallSteamAsync(string targetDrive, IProgress<int> progress, CancellationToken ct = default);
    Task UninstallSteamAsync(CancellationToken ct = default);
}