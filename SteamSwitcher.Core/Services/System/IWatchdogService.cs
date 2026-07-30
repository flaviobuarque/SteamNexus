namespace SteamSwitcher.Core.Services;

public interface IWatchdogService
{
    void BeginSwitch(string targetSteamId64);
    void EndSwitch();
    bool HasInterruptedSwitch(out string? interruptedSteamId64);
    void ClearInterruptedSwitch();
}