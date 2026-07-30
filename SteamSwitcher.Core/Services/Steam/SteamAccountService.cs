using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Text.RegularExpressions;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public class SteamAccountService(
    ISteamLocatorService locator,
    IAppSettingsService settingsService,
    ILogger<SteamAccountService> logger) : ISteamAccountService
{
    private readonly string _steamPath = locator.FindSteamInstallPath() ?? string.Empty;

    // Regex pre-compiladas (热心 usado em cada linha do VDF) — evita compilar a
    // cada iteracao de SwitchAccountAsync (potencialmente centenas de linhas).
    private static readonly Regex SteamIdLineRegex =
        new(@"""7656\d{13}""", RegexOptions.Compiled);
    private static readonly Regex MostRecentRegex =
        new(@"""MostRecent""\s+""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex RememberPasswordRegex =
        new(@"""RememberPassword""\s+""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex WantsOfflineModeRegex =
        new(@"""WantsOfflineMode""\s+""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex SkipOfflineModeWarningRegex =
        new(@"""SkipOfflineModeWarning""\s+""[^""]*""", RegexOptions.Compiled);

    public async Task<IReadOnlyList<SteamAccount>> GetAccountsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_steamPath))
        {
            logger.LogWarning("Steam não encontrado");
            return [];
        }

        var vdfPath = locator.GetLoginUsersVdfPath(_steamPath);
        if (!File.Exists(vdfPath))
        {
            logger.LogWarning("loginusers.vdf não encontrado em {Path}", vdfPath);
            return [];
        }

        return await Task.Run(() =>
        {
            var accounts = new List<SteamAccount>();
            try
            {
                using var stream = File.OpenRead(vdfPath);
                var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
                var data = kv.Deserialize(stream);

                foreach (var user in data)
                {
                    var steamId64 = user.Name;
                    var account = new SteamAccount
                    {
                        SteamId64 = steamId64,
                        AccountName = user["AccountName"]?.ToString() ?? string.Empty,
                        PersonaName = user["PersonaName"]?.ToString() ?? string.Empty,
                        RememberPassword = user["RememberPassword"]?.ToString() == "1",
                        MostRecent = user["MostRecent"]?.ToString() == "1",
                        WantsOfflineMode = user["WantsOfflineMode"]?.ToString() == "1",
                    };

                    var tsStr = user["Timestamp"]?.ToString() ?? string.Empty;
                    if (long.TryParse(tsStr, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out long ts))
                        account.Timestamp = ts;

                    account.IsActive = account.MostRecent;
                    accounts.Add(account);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao ler loginusers.vdf");
            }

            return (IReadOnlyList<SteamAccount>)accounts;
        }, ct);
    }

    public async Task SwitchAccountAsync(
        SteamAccount account,
        LoginState? stateOverride = null,
        CancellationToken ct = default)
    {
        var settings = settingsService.Current;
        LoginState? targetState = stateOverride
            ?? account.LoginStateOverride
            ?? settings.DefaultLoginStateOverride;

        logger.LogInformation(
            "Trocando para {Account} com estado {State}",
            account.AccountName, targetState);

        // 1. Fecha Steam
        await CloseSteamAsync(SteamCloseMethod.Graceful, ct);

        // 2. Edita loginusers.vdf
        await UpdateLoginUsersVdfAsync(account, targetState, ct);

        // 3. Atualiza registro
        UpdateRegistry(account);

        // 4. Abre Steam
        await StartSteamAsync(settings, targetState, ct);
    }

    public async Task<SteamAccount?> GetActiveAccountAsync(CancellationToken ct = default)
    {
        var accounts = await GetAccountsAsync(ct);

        try
        {
            var activeUserValue = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess",
                "ActiveUser",
                null);

            if (activeUserValue is not null &&
                uint.TryParse(activeUserValue.ToString(), out var activeSteamId32) &&
                activeSteamId32 != 0)
            {
                var activeAccount = accounts.FirstOrDefault(account =>
                    uint.TryParse(account.SteamId32, out var accountSteamId32) &&
                    accountSteamId32 == activeSteamId32);

                if (activeAccount is not null)
                    return activeAccount;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Não foi possível obter a conta Steam ativa pelo Registro");
        }

        return accounts.FirstOrDefault(account => account.MostRecent);
    }

    public void ForgetAccount(SteamAccount account)
    {
        // Remove do loginusers.vdf e salva backup
        var vdfPath = locator.GetLoginUsersVdfPath(_steamPath);
        var backupPath = vdfPath + ".bak";

        File.Copy(vdfPath, backupPath, overwrite: true);

        // TODO: implementar remoção do VDF e backup do account
        logger.LogInformation("Conta {Account} esquecida, backup em {Backup}",
            account.AccountName, backupPath);
    }

    // --- Privados ---

    private async Task CloseSteamAsync(SteamCloseMethod method, CancellationToken ct)
    {
        var processes = System.Diagnostics.Process
            .GetProcesses()
            .Where(p => p.ProcessName.StartsWith("steam", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!processes.Any()) return;

        // Sempre tenta gracioso primeiro (-shutdown eh o proprio metodo de fechamento do Steam).
        var steamExe = locator.GetSteamExePath(_steamPath);
        if (File.Exists(steamExe))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = "-shutdown",
                UseShellExecute = true
            });

            // Aguarda ate 8s o Steam fechar voluntariamente (sem enumerar/matando processos).
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(400, ct);
                var still = System.Diagnostics.Process
                    .GetProcessesByName("steam")
                    .Length > 0;
                if (!still) goto done;
            }
        }

        // Fallback: pede fechamento gracioso da janela principal de cada processo Steam,
        // evitando Kill (que eleva heuristica de antivirrus e pode corromper arquivos VDF).
        foreach (var proc in System.Diagnostics.Process
            .GetProcesses()
            .Where(p => p.ProcessName.StartsWith("steam", StringComparison.OrdinalIgnoreCase)))
        {
            try { proc.CloseMainWindow(); } catch { }
            proc.Dispose();
        }

        // Da tempo pro Steam processar o WM_CLOSE.
        await Task.Delay(1500, ct);

        // Se ainda houver Steam rodando, encerra forcando - ultima recurso.
        foreach (var proc in System.Diagnostics.Process
            .GetProcessesByName("steam"))
        {
            try { if (!proc.HasExited) proc.Close(); } catch { }
            proc.Dispose();
        }

    done:
        // Aguarda OS liberar handles dos arquivos.
        await Task.Delay(800, ct);

        // Dispose do snapshot original.
        foreach (var p in processes) p.Dispose();
    }

    private async Task UpdateLoginUsersVdfAsync(
    SteamAccount target,
    LoginState? state,
    CancellationToken ct)
{
    await Task.Run(() =>
    {
        var vdfPath = locator.GetLoginUsersVdfPath(_steamPath);
        File.Copy(vdfPath, vdfPath + "_last", overwrite: true);

        var lines = File.ReadAllLines(vdfPath).ToList();
        string? currentSteamId = null;

        // Interpreta null como Online: garante reset determinístico do modo offline
        // em todo o loginusers.vdf, evitando resíduos de sessões anteriores offline.
        var effectiveState = state ?? LoginState.Online;
        var wantsOffline = effectiveState == LoginState.Offline;

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim().Trim('"');

            if (trimmed == "}" && currentSteamId is not null)
            {
                currentSteamId = null;
                continue;
            }

            // Detecta linha de SteamID (linha com só número de 17 dígitos entre aspas)
            if (SteamIdLineRegex.IsMatch(lines[i].Trim()))
            {
                currentSteamId = trimmed;
                continue;
            }

            if (currentSteamId is null) continue;

            var isTarget = currentSteamId == target.SteamId64;

            // MostRecent
            if (lines[i].Contains("\"MostRecent\""))
            {
                lines[i] = MostRecentRegex.Replace(
                    lines[i], $"\"MostRecent\"\t\t\"{(isTarget ? "1" : "0")}\"");
            }

            // RememberPassword — garante 1 no target
            if (isTarget && lines[i].Contains("\"RememberPassword\""))
            {
                lines[i] = RememberPasswordRegex.Replace(
                    lines[i], "\"RememberPassword\"\t\t\"1\"");
            }

            // WantsOfflineMode — escreve em TODOS os usuários quando Online,
            // e apenas no target quando Offline.
            if (lines[i].Contains("\"WantsOfflineMode\""))
            {
                var newValue = (isTarget && wantsOffline) ? "1" : "0";
                lines[i] = WantsOfflineModeRegex.Replace(
                    lines[i], $"\"WantsOfflineMode\"\t\t\"{newValue}\"");
            }

            if (lines[i].Contains("\"SkipOfflineModeWarning\""))
            {
                var newValue = (isTarget && wantsOffline) ? "1" : "0";
                lines[i] = SkipOfflineModeWarningRegex.Replace(
                    lines[i], $"\"SkipOfflineModeWarning\"\t\t\"{newValue}\"");
            }
        }

        File.WriteAllLines(vdfPath, lines);
    }, ct);
}

    private static void UpdateRegistry(SteamAccount account)
    {
        Registry.SetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "AutoLoginUser",
            account.AccountName);

        Registry.SetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "RememberPassword",
            1,
            RegistryValueKind.DWord);
    }

    private async Task StartSteamAsync(AppSettings settings, LoginState? state, CancellationToken ct)
    {
        var steamExe = locator.GetSteamExePath(_steamPath);
        if (!File.Exists(steamExe)) return;

        var args = new List<string>();

        if (settings.StartSilent) args.Add("-silent");
        if (state == LoginState.Offline) args.Add("-offline");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = steamExe,
            Arguments = string.Join(" ", args),
            UseShellExecute = settings.StartAsAdmin,
            Verb = settings.StartAsAdmin ? "runas" : string.Empty,
        };

        System.Diagnostics.Process.Start(psi);
        await Task.Delay(1000, ct); // aguarda Steam inicializar
    }

    public async Task AddAccountAsync(CancellationToken ct = default)
    {
        // 1. Fecha Steam
        await CloseSteamAsync(SteamCloseMethod.Graceful, ct);

        // 2. Limpa autologin do registro
        Registry.SetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "AutoLoginUser",
            string.Empty);

        Registry.SetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "RememberPassword",
            0,
            RegistryValueKind.DWord);

        // 3. Abre Steam sem argumentos (cai na tela de login)
        var steamExe = locator.GetSteamExePath(_steamPath);
        if (!File.Exists(steamExe)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = steamExe,
            UseShellExecute = true
        });
    }
}