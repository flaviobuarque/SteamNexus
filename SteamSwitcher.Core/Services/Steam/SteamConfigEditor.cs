using System.Text.RegularExpressions;

namespace SteamSwitcher.Core.Services;

public static partial class SteamConfigEditor
{
    public static string DisableAccountChooser(string content, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(content);

        var didChange = false;
        var result = AccountChooserRegex().Replace(content, match =>
        {
            if (match.Groups["value"].Value == "0")
                return match.Value;

            didChange = true;
            return match.Groups["prefix"].Value
                + "0"
                + match.Groups["suffix"].Value;
        }, count: 1);

        changed = didChange;
        return result;
    }

    public static bool IsAccountChooserEnabled(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var match = AccountChooserRegex().Match(content);
        return match.Success && match.Groups["value"].Value != "0";
    }

    [GeneratedRegex(
        "^(?<prefix>\\s*\"AlwaysShowUserChooser\"\\s*\")(?<value>[^\"]*)(?<suffix>\".*)$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AccountChooserRegex();
}
