using System.Globalization;
using System.Text;

namespace SteamSwitcher.Core.Helpers;

public sealed record QuickSearchEntry(string Id, string Category, string Title, string Description, string Keywords,
    string? ImagePath = null);

public static class QuickSearch
{
    public static string Normalize(string value)
    {
        var output = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                output.Append(char.ToUpperInvariant(c));
        return output.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<QuickSearchEntry> Find(IEnumerable<QuickSearchEntry> entries, string query,
        IReadOnlyList<string> recentIds)
    {
        var tokens = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return entries.Select(entry => (Entry: entry, Text: Normalize(entry.Title + " " + entry.Keywords)))
            .Where(item => tokens.All(token => item.Text.Contains(token, StringComparison.Ordinal)))
            .OrderBy(item => tokens.Length == 0
                ? (recentIds.Contains(item.Entry.Id) ? 0 : 1)
                : (Normalize(item.Entry.Title).StartsWith(Normalize(query), StringComparison.Ordinal) ? 0 : 1))
            .ThenBy(item => tokens.Length == 0 ? RecentRank(recentIds, item.Entry.Id) : 0)
            .ThenBy(item => tokens.Length == 0 && item.Entry.Category == "Página" ? 0 : 1)
            .ThenBy(item => item.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(40).Select(item => item.Entry).ToArray();
    }

    private static int RecentRank(IReadOnlyList<string> ids, string id)
    {
        for (var i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
        return int.MaxValue;
    }
}
