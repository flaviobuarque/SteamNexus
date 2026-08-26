using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Diagnostics;

namespace SteamSwitcher.Core.Services;

public sealed class SteamDiagnosticsService(
    ISteamInstallationService installationService) : ISteamDiagnosticsService
{
    public async Task<SteamDiagnosticReport> CheckAsync(CancellationToken ct = default)
    {
        var runningPaths = GetRunningSteamPaths();
        var reports = new List<SteamInstallationDiagnosticReport>();
        foreach (var installation in installationService.Installations)
        {
            ct.ThrowIfCancellationRequested();
            reports.Add(await CheckInstallationAsync(
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
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RememberPassword", 1, RegistryValueKind.DWord);
    }

    private async Task<SteamInstallationDiagnosticReport> CheckInstallationAsync(
        SteamInstallation installation,
        IReadOnlyList<string> runningPaths,
        CancellationToken ct)
    {
        var items = new List<SteamDiagnosticItem>();
        items.Add(File.Exists(installation.SteamExePath)
            ? Success("Executável", installation.SteamExePath)
            : Error("Executável", $"Não encontrado: {installation.SteamExePath}"));

        SteamAccountsSnapshot snapshot = SteamAccountsSnapshot.Empty;
        if (File.Exists(installation.LoginUsersPath))
        {
            try
            {
                await using var stream = new FileStream(
                    installation.LoginUsersPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, true);
                snapshot = SteamAccountSnapshotParser.Parse(stream);
                items.Add(Success("loginusers.vdf", $"Arquivo válido com {snapshot.Accounts.Count} conta(s)."));
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
        items.Add(isRunning
            ? Success("Processos", $"Esta instalação está em execução: {pathsForInstallation[0]}")
            : Warning("Processos", "Esta instalação não está em execução."));

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
            var registryRemember = Convert.ToInt32(Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam", "RememberPassword", 0)) == 1;
            registryMatches = active is not null && registryRemember
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
