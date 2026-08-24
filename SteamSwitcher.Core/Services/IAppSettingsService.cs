using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface IAppSettingsService
{
    AppSettings Current { get; }
    Task SaveAsync(AppSettings settings);
    Task LoadAsync();
}