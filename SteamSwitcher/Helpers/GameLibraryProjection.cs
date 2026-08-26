using SteamSwitcher.Core.Models;
using SteamSwitcher.ViewModels;

namespace SteamSwitcher.Helpers;

public static class GameLibraryProjection
{
    public static IReadOnlyList<GameCardViewModel> FilterAndSort(
        IReadOnlyList<GameCardViewModel> games,
        string? ownerUniqueKey,
        string? searchText,
        GameSortMode sortMode)
    {
        var filtered = games.AsEnumerable();

        if (!string.IsNullOrEmpty(ownerUniqueKey))
        {
            filtered = filtered.Where(game =>
                string.Equals(
                    game.Game.OwnerAccount?.UniqueKey ?? game.Game.OwnerSteamId64,
                    ownerUniqueKey,
                    StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filtered = filtered.Where(game =>
                game.Game.Name.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase));
        }

        var sorted = sortMode switch
        {
            GameSortMode.MostPlayed => filtered
                .OrderByDescending(game => game.IsFavorite)
                .ThenByDescending(game => game.Game.PlaytimeMinutes)
                .ThenBy(game => game.Game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            GameSortMode.LargestSize => filtered
                .OrderByDescending(game => game.IsFavorite)
                .ThenByDescending(game => game.Game.SizeOnDisk)
                .ThenBy(game => game.Game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => filtered
                .OrderByDescending(game => game.IsFavorite)
                .ThenBy(game => game.Game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return sorted;
    }

    public static IReadOnlyList<GameCardViewModel> GetPage(
        IReadOnlyList<GameCardViewModel> games,
        int page,
        int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return games
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }
}
