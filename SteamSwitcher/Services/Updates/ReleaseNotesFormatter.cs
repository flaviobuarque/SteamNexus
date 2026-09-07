using System.Text.RegularExpressions;

namespace SteamSwitcher.Services.Updates;

public static class ReleaseNotesFormatter
{
    public static string Format(string notes)
    {
        var lines = new List<string>();
        foreach (var raw in notes.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            var plain = line.TrimStart('#', ' ').Replace("**", "");
            if (plain.Contains("Como instalar", StringComparison.OrdinalIgnoreCase)) break;
            if (string.IsNullOrWhiteSpace(line)
                || plain.Contains("Sobre esta versão", StringComparison.OrdinalIgnoreCase)
                || plain.StartsWith("Esta versão do SteamNexus reúne", StringComparison.OrdinalIgnoreCase)
                || plain.StartsWith("Full Changelog", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(plain, @"^https?://\S+$")) continue;
            lines.Add(line);
        }

        return lines.Count > 0
            ? string.Join('\n', lines)
            : "Esta versão ainda não possui uma descrição detalhada das alterações publicada.";
    }
}
