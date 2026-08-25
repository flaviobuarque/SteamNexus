using SteamSwitcher.Core.Models;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public static class SteamLoginUsersEditor
{
    public static void Rewrite(
        Stream input,
        Stream output,
        string targetSteamId64,
        LoginState state)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSteamId64);

        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var document = serializer.Deserialize(input);
        var foundTarget = false;
        var wantsOffline = state == LoginState.Offline;

        foreach (var user in document.Children)
        {
            var isTarget = string.Equals(
                user.Name,
                targetSteamId64,
                StringComparison.Ordinal);
            foundTarget |= isTarget;

            SetString(user, "MostRecent", isTarget ? "1" : "0");
            if (isTarget)
                SetString(user, "RememberPassword", "1");

            SetString(user, "WantsOfflineMode", isTarget && wantsOffline ? "1" : "0");
            SetString(user, "SkipOfflineModeWarning", isTarget && wantsOffline ? "1" : "0");
        }

        if (!foundTarget)
            throw new InvalidOperationException(
                "A conta selecionada não foi encontrada no loginusers.vdf.");

        serializer.Serialize(output, document);
    }

    private static void SetString(KVObject parent, string key, string value)
    {
        var existingKey = parent.Children
            .Select(child => child.Name)
            .FirstOrDefault(candidate =>
                string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));

        if (existingKey is null)
            parent.Add(new KVObject(key, value));
        else
            parent[existingKey] = value;
    }
}
