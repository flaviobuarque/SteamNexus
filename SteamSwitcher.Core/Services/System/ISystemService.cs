namespace SteamSwitcher.Core.Services;

public interface ISystemService
{
    void SetStartWithWindows(bool enable);
    bool GetStartWithWindows();
    bool IsSingleInstance(out bool brought);
    void BringExistingInstanceToFront();
}