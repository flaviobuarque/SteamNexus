using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public sealed class SteamDiagnosticsService(
    ISteamInstallationService installationService,
    ISteamGameService gameService,
    IGameRuntimeService runtimeService,
    ISteamAccountService accountService) : ISteamDiagnosticsService
{
    private readonly SemaphoreSlim _rememberRepairGate = new(1, 1);

    private static IReadOnlyList<RememberPasswordAccount> FindUnremembered(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var users = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        return users.Where(user => user["RememberPassword"]?.ToString() == "0")
            .Select(user => new RememberPasswordAccount(user.Name,
                user["AccountName"]?.ToString() ?? string.Empty,
                user["PersonaName"]?.ToString() ?? user["AccountName"]?.ToString() ?? user.Name)).ToArray();
    }

    public async Task<int> RepairRememberPasswordAsync(string installationId,
        IReadOnlyCollection<string> steamIds, CancellationToken ct = default)
    {
        await _rememberRepairGate.WaitAsync(ct);
        string? temp = null;
        try
        {
            var installation = RequireInstallation(installationId);
            var path = installation.LoginUsersPath;
            var original = await File.ReadAllBytesAsync(path, ct);
            var eligible = FindUnremembered(original).Select(account => account.SteamId64).ToHashSet(StringComparer.Ordinal);
            var selected = steamIds.Where(eligible.Contains).ToHashSet(StringComparer.Ordinal);
            if (selected.Count == 0) return 0;
            var previousAccount = await accountService.GetActiveAccountAsync(ct);
            if (previousAccount is null)
            {
                using var previousStream = new MemoryStream(original);
                previousAccount = SteamAccountSnapshotParser.Parse(previousStream).ActiveAccount;
                if (previousAccount is not null)
                {
                    previousAccount.InstallationId = installation.Id;
                    previousAccount.InstallationRootPath = installation.RootPath;
                }
            }
            await CloseSteamForRepairAsync(ct);
            // Steam may flush its preferences on exit. Always edit the final file.
            original = await File.ReadAllBytesAsync(path, ct);
            eligible = FindUnremembered(original).Select(account => account.SteamId64).ToHashSet(StringComparer.Ordinal);
            selected.IntersectWith(eligible);
            if (selected.Count == 0)
            {
                await RestorePreviousAccountAsync(previousAccount, ct);
                return 0;
            }
            var encoding = new UTF8Encoding(false, true);
            var content = encoding.GetString(original);
            var changed = new HashSet<string>(StringComparer.Ordinal);
            var updated = Regex.Replace(content, "\"(?<id>[0-9]+)\"\\s*\\{(?<body>[^{}]*)\\}", match =>
            {
                var id = match.Groups["id"].Value;
                if (!selected.Contains(id)) return match.Value;
                var result = Regex.Replace(match.Value, "(\"RememberPassword\"\\s*\")0(\")", "${1}1${2}");
                if (result != match.Value) changed.Add(id);
                return result;
            });
            if (!changed.SetEquals(selected))
                throw new InvalidOperationException("Formato do arquivo não suportado. Nenhuma alteração foi aplicada.");
            var bytes = encoding.GetBytes(updated);
            if (FindUnremembered(bytes).Any(account => selected.Contains(account.SteamId64)))
                throw new InvalidOperationException("Não foi possível validar a correção.");
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, ct);
            EnsureSteamClosed();
            if (!(await File.ReadAllBytesAsync(path, ct)).AsSpan().SequenceEqual(original))
                throw new InvalidOperationException("O arquivo mudou durante a correção. Atualize o diagnóstico e tente novamente.");
            ct.ThrowIfCancellationRequested();
            File.Replace(temp, path, path + ".rememberpassword-" + Guid.NewGuid().ToString("N") + ".bak");
            await RestorePreviousAccountAsync(previousAccount, ct);
            return changed.Count;
        }
        finally
        {
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
            _rememberRepairGate.Release();
        }
    }

    private async Task RestorePreviousAccountAsync(SteamAccount? previousAccount, CancellationToken ct)
    {
        if (previousAccount is null) return;
        try
        {
            var refreshed = (await accountService.GetAllAccountsAsync(ct))
                .FirstOrDefault(account => account.UniqueKey == previousAccount.UniqueKey)
                ?? throw new InvalidOperationException("A conta anterior não está mais disponível.");
            await accountService.SwitchAccountAsync(refreshed,
                previousAccount.WantsOfflineMode ? LoginState.Offline : LoginState.Online, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("A correção foi salva, mas não foi possível reabrir a conta anterior. " + ex.Message, ex);
        }
    }

    private async Task CloseSteamForRepairAsync(CancellationToken ct)
    {
        var games = await gameService.GetInstalledGamesAsync([], ct);
        var states = await runtimeService.InspectAsync(games, ct);
        if (states.Values.Any(state => state.IsRunning || state.IsUncertain))
            throw new InvalidOperationException("Pare o jogo antes de corrigir. A Steam não foi fechada.");

        var processes = Process.GetProcessesByName("steam");
        try
        {
            var paths = processes.Select(TryGetPath).ToArray();
            if (paths.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Não foi possível identificar a Steam com segurança. A correção foi cancelada.");
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                using var request = Process.Start(new ProcessStartInfo(path, "-shutdown")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                var remaining = Process.GetProcessesByName("steam");
                try { if (remaining.Length == 0) return; }
                finally { foreach (var process in remaining) process.Dispose(); }
                await Task.Delay(500, ct);
            }
            throw new InvalidOperationException("A Steam não concluiu o encerramento. Nenhum arquivo foi alterado; tente novamente após verificar os avisos da Steam.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static void EnsureSteamClosed()
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            if (processes.Length > 0)
                throw new InvalidOperationException("A Steam foi reaberta durante a correção. Nenhum arquivo foi alterado; tente novamente.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public async Task<SteamDiagnosticReport> CheckAsync(CancellationToken ct = default)
    {
        var runningPaths = GetRunningSteamPaths();
        var reports = new List<SteamInstallationDiagnosticReport>();
        foreach (var installation in installationService.Installations)
        {
            ct.ThrowIfCancellationRequested();
            reports.Add(await BuildInstallationReportAsync(
                installation,
                runningPaths,
                ct));
        }

        if (reports.Count == 0)
        {
            reports.Add(new SteamInstallationDiagnosticReport
            {
                InstallationName = "Nenhuma instalação",
                Items = [Error("Instalação", "Nenhuma instalação Steam foi cadastrada.")],
            });
        }

        return new SteamDiagnosticReport
        {
            CheckedAt = DateTime.Now,
            Installations = reports,
        };
    }

    public Task<SteamInstallationDiagnosticReport> CheckInstallationAsync(
        string installationId,
        CancellationToken ct = default) =>
        BuildInstallationReportAsync(RequireInstallation(installationId), GetRunningSteamPaths(), ct);

    public async Task DisableAccountChooserAsync(
        string installationId,
        CancellationToken ct = default)
    {
        var installation = RequireInstallation(installationId);
        var path = Path.Combine(installation.RootPath, "config", "config.vdf");
        var content = await File.ReadAllTextAsync(path, ct);
        var updated = SteamConfigEditor.DisableAccountChooser(content, out var changed);
        if (changed) await WriteAtomicallyAsync(path, updated, ct);
    }

    public async Task RepairRegistryAsync(
        string installationId,
        CancellationToken ct = default)
    {
        var installation = RequireInstallation(installationId);
        await using var stream = new FileStream(
            installation.LoginUsersPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, true);
        var active = SteamAccountSnapshotParser.Parse(stream).ActiveAccount
            ?? throw new InvalidOperationException("Não existe uma conta ativa inequívoca no VDF.");
        ct.ThrowIfCancellationRequested();
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", active.AccountName);
    }

    private async Task<SteamInstallationDiagnosticReport> BuildInstallationReportAsync(
        SteamInstallation installation,
        IReadOnlyList<string> runningPaths,
        CancellationToken ct)
    {
        var items = new List<SteamDiagnosticItem>();
        items.Add(File.Exists(installation.SteamExePath)
            ? Success("Executável", installation.SteamExePath)
            : Error("Executável", $"Não encontrado: {installation.SteamExePath}"));

        SteamAccountsSnapshot snapshot = SteamAccountsSnapshot.Empty;
        IReadOnlyList<RememberPasswordAccount> unremembered = [];
        if (File.Exists(installation.LoginUsersPath))
        {
            try
            {
                await using var stream = new FileStream(
                    installation.LoginUsersPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, true);
                snapshot = SteamAccountSnapshotParser.Parse(stream);
                stream.Position = 0;
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, ct);
                unremembered = FindUnremembered(copy.ToArray());
                items.Add(Success("loginusers.vdf", $"Arquivo válido com {snapshot.Accounts.Count} conta(s)."));
                items.Add(unremembered.Count > 0
                    ? Warning("Lembrar senha", $"{unremembered.Count} conta(s) com RememberPassword=0. Correção manual disponível.") with { CanRepairRememberPassword = true }
                    : Success("Lembrar senha", "Nenhuma conta com RememberPassword=0."));
            }
            catch (Exception ex)
            {
                items.Add(Error("loginusers.vdf", $"Arquivo inválido ou inacessível: {ex.Message}"));
            }
        }
        else
        {
            items.Add(Error("loginusers.vdf", "Arquivo ausente. Contas arquivadas podem recuperá-lo."));
        }

        var pathsForInstallation = runningPaths
            .Where(path => IsUnder(path, installation.RootPath)).ToList();
        var isRunning = pathsForInstallation.Count > 0;

        var active = snapshot.ActiveAccount;
        if (snapshot.Accounts.Count > 0)
            items.Add(active is null
                ? Error("Conta ativa", "O VDF não possui exatamente uma conta ativa.")
                : Success("Conta ativa", $"{active.PersonaName} (@{active.AccountName})"));

        var shouldCheckGlobalRegistry = isRunning
            || (runningPaths.Count == 0 && installation.IsSelected);
        var registryMatches = false;
        if (shouldCheckGlobalRegistry)
        {
            var registryName = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", null)?.ToString() ?? string.Empty;
            registryMatches = active is not null
                && string.Equals(registryName, active.AccountName, StringComparison.OrdinalIgnoreCase);
            items.Add(registryMatches
                ? Success("Registro", $"Autologin configurado para @{registryName}.")
                : Warning("Registro", "O autologin global não corresponde à conta ativa desta instalação."));
        }
        else
        {
            items.Add(Success("Registro", "Não se aplica enquanto outra instalação estiver ativa."));
        }

        var chooserEnabled = false;
        var configPath = Path.Combine(installation.RootPath, "config", "config.vdf");
        if (File.Exists(configPath))
        {
            try
            {
                chooserEnabled = SteamConfigEditor.IsAccountChooserEnabled(
                    await File.ReadAllTextAsync(configPath, ct));
                items.Add(chooserEnabled
                    ? Warning("Seletor de contas", "AlwaysShowUserChooser está ativado.")
                    : Success("Seletor de contas", "O seletor obrigatório está desativado."));
            }
            catch (Exception ex)
            {
                items.Add(Warning("Seletor de contas", $"Não foi possível verificar: {ex.Message}"));
            }
        }

        return new SteamInstallationDiagnosticReport
        {
            InstallationId = installation.Id,
            InstallationName = installation.DisplayName,
            InstallationPath = installation.RootPath,
            RunningSteamPath = pathsForInstallation.FirstOrDefault() ?? string.Empty,
            ActiveAccountName = active?.AccountName ?? string.Empty,
            IsSelected = installation.IsSelected,
            IsRunning = isRunning,
            CanDisableChooser = chooserEnabled,
            AccountsWithoutRememberPassword = unremembered,
            CanRepairRegistry = shouldCheckGlobalRegistry && active is not null && !registryMatches,
            Items = items,
        };
    }

    private SteamInstallation RequireInstallation(string installationId) =>
        installationService.Installations.FirstOrDefault(item => item.Id == installationId)
        ?? throw new InvalidOperationException("A instalação Steam não foi encontrada.");

    private static List<string> GetRunningSteamPaths()
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            return processes.Select(TryGetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static async Task WriteAtomicallyAsync(string path, string content, CancellationToken ct)
    {
        var temp = path + ".diagnostic.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, ct);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string TryGetPath(Process process)
    {
        try { return process.MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool IsUnder(string path, string root)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static SteamDiagnosticItem Success(string title, string detail) =>
        new(title, detail, DiagnosticSeverity.Success);
    private static SteamDiagnosticItem Warning(string title, string detail) =>
        new(title, detail, DiagnosticSeverity.Warning);
    private static SteamDiagnosticItem Error(string title, string detail) =>
        new(title, detail, DiagnosticSeverity.Error);
}
