using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamSwitcher.Core.Services;

/// <summary>Read-only discovery; destructive actions require a fresh, exact process identity.</summary>
public sealed class GameRuntimeService : IGameRuntimeService
{
    private static readonly HashSet<string> SharedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "steamservice", "gameoverlayui", "crashhandler", "crashhandler64",
        "crashreporter", "unitycrashhandler64", "unitycrashhandler32", "easyanticheat", "easyanticheat_eos",
        "beservice", "beservice_x64", "eadesktop", "ealauncher", "epicgameslauncher", "ubisoftconnect",
        "upc", "rockstarservice", "launcher", "setup", "unins000", "dxsetup", "vc_redist.x64", "vc_redist.x86"
    };

    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", "fontdrvhost",
        "svchost", "dwm", "audiodg", "spoolsv", "sihost", "taskhostw", "explorer", "shellexperiencehost",
        "startmenuexperiencehost", "searchhost", "searchindexer", "securityhealthservice",
        "msmpeng", "nissrv", "runtimebroker", "ctfmon", "conhost", "cmd", "powershell", "pwsh",
        "devenv", "code", "idle"
    };

    public Task<IReadOnlyDictionary<string, GameRuntimeSnapshot>> InspectAsync(
        IReadOnlyList<SteamGame> games, CancellationToken ct = default) => Task.Run(() =>
    {
        var processes = new List<GameProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (process.Id <= 4) continue;
                    var name = process.ProcessName;
                    if (SystemProcesses.Contains(name) || SharedExecutables.Contains(name)) continue;
                    var path = process.MainModule?.FileName;
                    if (path is not null)
                    {
                        var normalized = Path.GetFullPath(path);
                        processes.Add(new(process.Id, process.StartTime.ToUniversalTime(), normalized));
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Protected/exited processes are not safe targets for termination.
                }
            }
        }

        var result = new Dictionary<string, GameRuntimeSnapshot>(StringComparer.Ordinal);
        var groups = games.GroupBy(game => game.UniqueKey).ToDictionary(group => group.Key, group => group.ToArray());

        var prefixesByGroup = groups.ToDictionary(
            group => group.Key,
            group => group.Value.Select(GetInstallPrefix).Where(p => p is not null).Select(p => p!).ToArray());

        var matchesByGame = groups.ToDictionary(
            group => group.Key,
            group =>
            {
                var prefixes = prefixesByGroup[group.Key];
                if (prefixes.Length == 0) return [];
                return processes.Where(p => prefixes.Any(prefix => p.Executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray();
            });

        foreach (var game in games)
        {
            var matches = matchesByGame[game.UniqueKey];
            var steamRunning = ReadSteamRunning(game.AppId);
            // Steam's registry hint has no installation identity. Do not attribute it
            // to a second copy if the process already identifies a different library.
            if (matches.Length == 0 && games.Any(other => other.AppId == game.AppId && matchesByGame[other.UniqueKey].Length > 0))
                steamRunning = false;
            result[game.UniqueKey] = new(matches.Length > 0 || steamRunning == true,
                (matches.Length == 0 && steamRunning != false)
                    || (matches.Length > 0 && groups[game.UniqueKey].Select(copy => copy.InstallFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1), matches);
        }
        return (IReadOnlyDictionary<string, GameRuntimeSnapshot>)result;
    }, ct);

    private static bool? ReadSteamRunning(string appId)
    {
        if (!uint.TryParse(appId, out _)) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Valve\Steam\Apps\{appId}");
            return Convert.ToInt32(key?.GetValue("Running") ?? 0) != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? GetInstallPrefix(SteamGame game)
    {
        if (string.IsNullOrWhiteSpace(game.LibraryPath) || string.IsNullOrWhiteSpace(game.InstallDir)) return null;
        try
        {
            var common = Path.GetFullPath(Path.Combine(game.LibraryPath, "steamapps", "common")) + Path.DirectorySeparatorChar;
            var install = Path.GetFullPath(game.InstallFullPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return (install.StartsWith(common, StringComparison.OrdinalIgnoreCase) && install.Length > common.Length)
                ? install
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static bool BelongsToGame(string executable, SteamGame game)
    {
        var prefix = GetInstallPrefix(game);
        if (prefix is null) return false;
        try
        {
            var fullExe = Path.GetFullPath(executable);
            return fullExe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static Process? OpenExact(GameProcessIdentity identity, SteamGame? game = null)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(identity.Id);
            var path = process.MainModule?.FileName;
            if (process.StartTime.ToUniversalTime() == identity.StartedUtc
                && string.Equals(path, identity.Executable, StringComparison.OrdinalIgnoreCase)
                && !SharedExecutables.Contains(process.ProcessName)
                && (game is null || (path is not null && BelongsToGame(path, game)))) return process;
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
        process?.Dispose();
        return null;
    }

    public async Task<bool> StopAsync(SteamGame game, GameRuntimeSnapshot snapshot, bool force, CancellationToken ct = default)
    {
        if (snapshot.IsUncertain || snapshot.Processes.Count == 0)
            throw new InvalidOperationException("Não foi possível identificar com segurança o processo. Feche o jogo pela Steam ou pela própria janela.");

        foreach (var identity in snapshot.Processes)
        {
            ct.ThrowIfCancellationRequested();
            using var process = OpenExact(identity, game);
            if (process is null) continue;
            if (force) process.Kill(entireProcessTree: false);
            else if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
        }

        var deadline = DateTime.UtcNow.AddSeconds(force ? 10 : 15);
        do
        {
            var current = (await InspectAsync([game], ct))[game.UniqueKey];
            if (!current.IsRunning && !current.IsUncertain) return true;
            await Task.Delay(750, ct);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    public void BringToFront(GameRuntimeSnapshot snapshot)
    {
        foreach (var identity in snapshot.Processes)
        {
            using var process = OpenExact(identity);
            if (process is null || process.MainWindowHandle == IntPtr.Zero) continue;
            if (IsIconic(process.MainWindowHandle)) ShowWindow(process.MainWindowHandle, 9);
            if (SetForegroundWindow(process.MainWindowHandle)) return;
        }
        throw new InvalidOperationException("Não foi possível trazer a janela à frente. Use a barra de tarefas ou Alt+Tab.");
    }

    public void ValidateUninstallTarget(SteamGame game)
    {
        if (!uint.TryParse(game.AppId, out _)) throw new InvalidOperationException("Identificador de jogo inválido.");
        var exe = Path.Combine(game.InstallationRootPath, "steam.exe");
        if (!File.Exists(exe)) throw new InvalidOperationException("A instalação da Steam não está disponível.");
        foreach (var process in Process.GetProcessesByName("steam"))
        {
            using (process)
            {
                if (!string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Outra instalação da Steam está aberta. Feche-a e abra a instalação deste jogo antes de desinstalar.");
            }
        }
    }

    public void RequestUninstall(SteamGame game)
    {
        ValidateUninstallTarget(game);
        var exe = Path.Combine(game.InstallationRootPath, "steam.exe");
        Process.Start(new ProcessStartInfo(exe, $"steam://uninstall/{game.AppId}")
        {
            WorkingDirectory = game.InstallationRootPath,
            UseShellExecute = false
        })?.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
}
