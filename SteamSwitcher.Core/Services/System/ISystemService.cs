namespace SteamSwitcher.Core.Services;

public interface ISystemService
{
    event EventHandler? ExistingInstanceActivated;
    void SetStartWithWindows(bool enable);
    bool GetStartWithWindows();
    bool IsSingleInstance(out bool brought);
}
