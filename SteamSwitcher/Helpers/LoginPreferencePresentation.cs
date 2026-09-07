using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Helpers;

public static class LoginPreferencePresentation
{
    public static string StateName(LoginState? state) => state == LoginState.Offline ? "Offline" : "Online";
    public static string Label(LoginState? preference, LoginState? inherited) => preference.HasValue
        ? StateName(preference)
        : "Padrão";
}

public sealed record LoginPreferenceSelection(object Target, LoginState? State);
