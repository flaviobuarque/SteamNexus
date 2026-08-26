using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Diagnostics;

namespace SteamSwitcher.Core.Services;

public sealed class SteamDiagnosticsService(
    ISteamInstallationService installationService) : ISteamDiagnosticsService
{
    public async Task<SteamDiagnosticReport> CheckAsync(CancellationToken ct = default)
    {
        var installation = installationService.SelectedInstallation;
        var items = new List<SteamDiagnosticItem>();
        if (installation is null)
        {
            items.Add(Error("Instalação", "Nenhuma instalação Steam está selecionada."));
            return new SteamDiagnosticReport { Items = items };
        }

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
            items.Add(Error("loginusers.vdf", "Arquivo ausente. A recuperação será necessária."));
        }

        var steamProcesses = Process.GetProcessesByName("steam");
        var runningPath = string.Empty;
        try
        {
            var paths = steamProcesses.Select(TryGetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            runningPath = paths.FirstOrDefault() ?? string.Empty;
            if (paths.Count == 0)
                items.Add(Warning("Processos", "A Steam não está em execução."));
            else if (paths.Any(path => !IsUnder(path, installation.RootPath)))
                items.Add(Error("Processos", $"Outra instalação está em execução: {string.Join(", ", paths)}"));
            else
                items.Add(Success("Processos", $"Instalação correta em execução: {runningPath}"));
        }
        finally
        {
            foreach (var process in steamProcesses) process.Dispose();
        }

        var active = snapshot.ActiveAccount;
        if (snapshot.Accounts.Count > 0)
            items.Add(active is null
                ? Error("Conta ativa", "O VDF não possui exatamente uma conta ativa.")
                : Success("Conta ativa", $"{active.PersonaName} (@{active.AccountName})"));

        var registryName = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", null)?.ToString() ?? string.Empty;
        var registryRemember = Convert.ToInt32(Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam", "RememberPassword", 0)) == 1;
        var registryMatches = active is not null && registryRemember
            && string.Equals(registryName, active.AccountName, StringComparison.OrdinalIgnoreCase);
        items.Add(registryMatches
            ? Success("Registro", $"Autologin configurado para @{registryName}.")
            : Warning("Registro", "O autologin do Registro não corresponde à conta ativa do VDF."));

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

        return new SteamDiagnosticReport
        {
            InstallationName = installation.DisplayName,
            InstallationPath = installation.RootPath,
            RunningSteamPath = runningPath,
            ActiveAccountName = active?.AccountName ?? string.Empty,
            CanDisableChooser = chooserEnabled,
            CanRepairRegistry = active is not null && !registryMatches,
            Items = items,
        };
    }

    public async Task DisableAccountChooserAsync(CancellationToken ct = default)
    {
        var installation = RequireInstallation();
        var path = Path.Combine(installation.RootPath, "config", "config.vdf");
        var content = await File.ReadAllTextAsync(path, ct);
        var updated = SteamConfigEditor.DisableAccountChooser(content, out var changed);
        if (changed) await WriteAtomicallyAsync(path, updated, ct);
    }

    public async Task RepairRegistryAsync(CancellationToken ct = default)
    {
        var installation = RequireInstallation();
        await using var stream = new FileStream(
            installation.LoginUsersPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, true);
        var active = SteamAccountSnapshotParser.Parse(stream).ActiveAccount
            ?? throw new InvalidOperationException("Não existe uma conta ativa inequívoca no VDF.");
        ct.ThrowIfCancellationRequested();
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", active.AccountName);
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RememberPassword", 1, RegistryValueKind.DWord);
    }

    private SteamInstallation RequireInstallation() => installationService.SelectedInstallation
        ?? throw new InvalidOperationException("Nenhuma instalação Steam está selecionada.");

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
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static SteamDiagnosticItem Success(string title, string detail) =>
        new(title, detail, DiagnosticSeverity.Success);
    private static SteamDiagnosticItem Warning(string title, string detail) =>
        new(title, detail, DiagnosticSeverity.Warning);
    private static SteamDiagnosticItem Error(string title, string detail) =>
        new(title, detail, DiagnosticSeverity.Error);
}
