namespace SteamSwitcher.Core.Models;

public sealed class SavedTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CustomThemeSettings Theme { get; set; } = new();
    public SavedTheme Clone() => new() { Id = Id, Theme = Theme.Clone() };
}
