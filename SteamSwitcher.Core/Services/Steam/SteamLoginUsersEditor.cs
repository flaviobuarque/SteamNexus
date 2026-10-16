using SteamSwitcher.Core.Models;
using System.Text;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public static class SteamLoginUsersEditor
{
    public static bool EnableRememberPassword(Stream input, Stream output, ISet<string> excludedIds)
    {
        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var document = serializer.Deserialize(input);
        if (!string.Equals(document.Name, "users", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("loginusers.vdf inválido.");
        var changed = false;
        foreach (var user in document.Children)
        {
            if (!ulong.TryParse(user.Name, out _) || excludedIds.Contains(user.Name)) continue;
            var field = user.Children.FirstOrDefault(child => child.Name.Equals("RememberPassword", StringComparison.OrdinalIgnoreCase));
            if (field?.Value?.ToString() == "1") continue;
            SetString(user, "RememberPassword", "1");
            changed = true;
        }
        if (changed) serializer.Serialize(output, document);
        return changed;
    }

    public static void Create(
        Stream output,
        SteamAccount target,
        LoginState state)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.SteamId64);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.AccountName);

        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        using var empty = new MemoryStream(Encoding.UTF8.GetBytes(
            "\"users\"\n{\n}\n"));
        var document = serializer.Deserialize(empty);
        var user = CreateTargetObject(target);
        ApplySessionFlags(user, state);
        document.Add(user);
        serializer.Serialize(output, document);
    }

    public static void Rewrite(
        Stream input,
        Stream output,
        string targetSteamId64,
        LoginState state,
        SteamAccount? missingTarget = null,
        bool enableRememberPassword = true,
        bool updateTimestampToNow = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSteamId64);

        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var document = serializer.Deserialize(input);
        var foundTarget = false;
        var wantsOffline = state == LoginState.Offline;

        if (!document.Children.Any(user => string.Equals(
                user.Name, targetSteamId64, StringComparison.Ordinal))
            && missingTarget is not null
            && string.Equals(missingTarget.SteamId64, targetSteamId64, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(missingTarget.AccountName))
        {
            var restored = CreateTargetObject(missingTarget);
            document.Add(restored);
        }

        foreach (var user in document.Children)
        {
            var isTarget = string.Equals(
                user.Name,
                targetSteamId64,
                StringComparison.Ordinal);
            foundTarget |= isTarget;
            if (isTarget && enableRememberPassword) SetString(user, "RememberPassword", "1");

            SetString(user, "MostRecent", isTarget ? "1" : "0");
            SetString(user, "AutoLogin", isTarget ? "1" : "0");

            if (isTarget)
            {
                if (updateTimestampToNow)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    SetString(user, "Timestamp", now.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    var timestampField = user.Children.FirstOrDefault(child => child.Name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase));
                    var currentVal = timestampField?.Value?.ToString();
                    if (string.IsNullOrWhiteSpace(currentVal) || currentVal == "0")
                    {
                        var repairTimestamp = missingTarget?.EffectiveTimestamp > 0
                            ? missingTarget.EffectiveTimestamp
                            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        SetString(user, "Timestamp", repairTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }

            // A Steam guarda esta preferencia por conta. A troca só define qual
            // conta entra automaticamente; nunca deve desconectar as demais.
            // Campos ausentes continuam ausentes, inclusive na recuperação.

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

    private static KVObject CreateTargetObject(SteamAccount target)
    {
        var timestamp = target.EffectiveTimestamp > 0
            ? target.EffectiveTimestamp
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new(target.SteamId64,
        [
            new KVObject("AccountName", target.AccountName),
            new KVObject("PersonaName", target.PersonaName),
            new KVObject("Timestamp", timestamp.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
        ]);
    }

    private static void ApplySessionFlags(KVObject user, LoginState state)
    {
        SetString(user, "RememberPassword", "1");
        SetString(user, "MostRecent", "1");
        SetString(user, "AutoLogin", "1");
        var offline = state == LoginState.Offline ? "1" : "0";
        SetString(user, "WantsOfflineMode", offline);
        SetString(user, "SkipOfflineModeWarning", offline);
    }
}
