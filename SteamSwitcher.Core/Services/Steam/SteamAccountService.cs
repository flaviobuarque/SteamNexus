using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public class SteamAccountService(
    ISteamLocatorService locator,
    IAppSettingsService settingsService,
    ILogger<SteamAccountService> logger) : ISteamAccountService
{
    private readonly string _steamPath = locator.FindSteamInstallPath() ?? string.Empty;
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private SteamAccountsSnapshot? _cachedSnapshot;
    private long _cachedVdfLength = -1;
    private long _cachedVdfWriteTicks = -1;

    // Regex pre-compiladas (热心 usado em cada linha do VDF) — evita compilar a
    // cada iteracao de SwitchAccountAsync (potencialmente centenas de linhas).
    private static readonly Regex SteamIdLineRegex =
        new(@"""7656\d{13}""", RegexOptions.Compiled);
    private static readonly Regex MostRecentRegex =
        new(@"""MostRecent""\s+""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex AutoLoginRegex =
        new(@"""AutoLogin""\s+""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex RememberPasswordRegex =
        new(@"""RememberPassword""\s+""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex WantsOfflineModeRegex =
        new(@"""WantsOfflineMode""\s+""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex SkipOfflineModeWarningRegex =
        new(@"""SkipOfflineModeWarning""\s+""[^""]*""", RegexOptions.Compiled);

    public async Task<SteamAccountsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_steamPath))
        {
            logger.LogWarning("Steam não encontrado");
            return SteamAccountsSnapshot.Empty;
        }

        var vdfPath = locator.GetLoginUsersVdfPath(_steamPath);
        if (!File.Exists(vdfPath))
        {
            logger.LogWarning("loginusers.vdf não encontrado em {Path}", vdfPath);
            return SteamAccountsSnapshot.Empty;
        }

        var fileInfo = new FileInfo(vdfPath);
        if (_cachedSnapshot is not null
            && _cachedVdfLength == fileInfo.Length
            && _cachedVdfWriteTicks == fileInfo.LastWriteTimeUtc.Ticks)
        {
            return _cachedSnapshot;
        }

        await _snapshotGate.WaitAsync(ct);
        try
        {
            fileInfo.Refresh();
            if (_cachedSnapshot is not null
                && _cachedVdfLength == fileInfo.Length
                && _cachedVdfWriteTicks == fileInfo.LastWriteTimeUtc.Ticks)
            {
                return _cachedSnapshot;
            }

            var readLength = fileInfo.Length;
            var readWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;

            var snapshot = await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(vdfPath);
                    return SteamAccountSnapshotParser.Parse(stream);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao ler loginusers.vdf");
                    return SteamAccountsSnapshot.Empty;
                }
            }, ct);

            // Guarda a assinatura observada antes da leitura. Se a Steam alterar o
            // arquivo durante o parsing, a próxima chamada detectará a divergência.
            _cachedVdfLength = readLength;
            _cachedVdfWriteTicks = readWriteTicks;
            _cachedSnapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    public async Task<IReadOnlyList<SteamAccount>> GetAccountsAsync(CancellationToken ct = default)
        => (await GetSnapshotAsync(ct)).Accounts;

    public async Task SwitchAccountAsync(
        SteamAccount account,
        LoginState? stateOverride = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var operation = Stopwatch.StartNew();
        var settings = settingsService.Current;
        LoginState? targetState = stateOverride
            ?? account.LoginStateOverride
            ?? settings.DefaultLoginStateOverride;

        logger.LogInformation(
            "Troca Steam iniciada. Target={Target}, State={State}",
            MaskSteamId(account.SteamId64), targetState ?? LoginState.Online);

        if (await PreflightSwitchAsync(account, ct))
        {
            logger.LogInformation(
                "Troca Steam ignorada: a conta solicitada já está ativa. Target={Target}, ElapsedMs={ElapsedMs}",
                MaskSteamId(account.SteamId64), operation.ElapsedMilliseconds);
            return;
        }

        LogSwitchPhase("preflight", operation, account.SteamId64);

        // 1. Fecha Steam
        await CloseSteamAsync(SteamCloseMethod.Graceful, ct);
        LogSwitchPhase("steam-closed", operation, account.SteamId64);

        // 2. Edita loginusers.vdf
        await UpdateLoginUsersVdfAsync(account, targetState, ct);
        LogSwitchPhase("vdf-updated", operation, account.SteamId64);

        // 3. Atualiza registro
        UpdateRegistry(account);
        LogSwitchPhase("registry-updated", operation, account.SteamId64);

        // 4. Abre Steam
        await StartSteamAsync(settings, targetState, ct);
        LogSwitchPhase("steam-started", operation, account.SteamId64);

        logger.LogInformation(
            "Troca Steam concluída. Target={Target}, ElapsedMs={ElapsedMs}",
            MaskSteamId(account.SteamId64), operation.ElapsedMilliseconds);
    }

    private async Task<bool> PreflightSwitchAsync(
        SteamAccount target,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_steamPath) || !Directory.Exists(_steamPath))
            throw new InvalidOperationException("A instalação da Steam não foi encontrada.");

        var steamExe = locator.GetSteamExePath(_steamPath);
        if (!File.Exists(steamExe))
            throw new FileNotFoundException("O executável da Steam não foi encontrado.", steamExe);

        var vdfPath = locator.GetLoginUsersVdfPath(_steamPath);
        if (!File.Exists(vdfPath))
            throw new FileNotFoundException("O arquivo loginusers.vdf não foi encontrado.", vdfPath);

        if ((File.GetAttributes(vdfPath) & FileAttributes.ReadOnly) != 0)
            throw new IOException("O arquivo loginusers.vdf está marcado como somente leitura.");

        if (string.IsNullOrWhiteSpace(target.SteamId64))
            throw new InvalidOperationException("A conta selecionada não possui um SteamID válido.");

        SteamAccountsSnapshot snapshot;
        try
        {
            await using var stream = new FileStream(
                vdfPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            snapshot = SteamAccountSnapshotParser.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Não foi possível ler o loginusers.vdf antes da troca.", ex);
        }

        var persisted = snapshot.Accounts.FirstOrDefault(a =>
            string.Equals(a.SteamId64, target.SteamId64, StringComparison.Ordinal));

        if (persisted is null)
            throw new InvalidOperationException("A conta selecionada não existe mais no loginusers.vdf.");

        if (string.IsNullOrWhiteSpace(persisted.AccountName))
            throw new InvalidOperationException("A conta selecionada não possui um nome de login válido.");

        if (!persisted.RememberPassword)
            throw new InvalidOperationException(
                "A Steam não possui uma sessão lembrada para esta conta. Entre nela novamente e marque a opção para lembrar a conta.");

        return string.Equals(
            snapshot.ActiveAccount?.SteamId64,
            target.SteamId64,
            StringComparison.Ordinal);
    }

    private void LogSwitchPhase(string phase, Stopwatch operation, string steamId64) =>
        logger.LogInformation(
            "Troca Steam: {Phase}. Target={Target}, ElapsedMs={ElapsedMs}",
            phase,
            MaskSteamId(steamId64),
            operation.ElapsedMilliseconds);

    private static string MaskSteamId(string steamId64)
    {
        var value = steamId64?.Trim() ?? string.Empty;
        return value.Length <= 4 ? "****" : $"***{value[^4..]}";
    }

    public async Task<SteamAccount?> GetActiveAccountAsync(CancellationToken ct = default)
    {
        // Padrao TcNo-Acc-Switcher: o source of truth e o loginusers.vdf.
        // O registry "ActiveProcess\ActiveUser" so e escrito pela Steam em execucao
        // — useless em startup apos troca de conta (Steam ainda fechando/abrindo).
        //
        // Preferimos o campo "AutoLogin" (atual Steam); fallback "MostRecent" (legado).
        // Exigimos EXATAMENTE UM usuario com a flag — retorna null se 0 ou 2+.
        // Preferimos null em vez de adivinhar errado.

        return (await GetSnapshotAsync(ct)).ActiveAccount;
    }

    public async Task ForgetAccountAsync(
        SteamAccount account,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_steamPath))
            throw new InvalidOperationException("Steam não encontrada.");

        var vdfPath = locator.GetLoginUsersVdfPath(_steamPath);
        if (!File.Exists(vdfPath))
            throw new FileNotFoundException("loginusers.vdf não encontrado.", vdfPath);

        await CloseSteamAsync(SteamCloseMethod.Graceful, ct);

        var backupPath = vdfPath + ".bak";
        var tempPath = vdfPath + ".tmp";

        await Task.Run(() =>
        {
            var lines = File.ReadAllLines(vdfPath).ToList();
            var start = FindAccountBlockStart(lines, account.SteamId64);
            if (start < 0)
                throw new InvalidOperationException(
                    $"A conta {account.AccountName} não foi encontrada no loginusers.vdf.");

            var openBrace = -1;
            for (var i = start + 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                if (lines[i].Trim() == "{") openBrace = i;
                break;
            }

            if (openBrace < 0)
                throw new InvalidDataException("Bloco da conta está incompleto no loginusers.vdf.");

            var depth = 0;
            var end = -1;
            for (var i = openBrace; i < lines.Count; i++)
            {
                depth += CountStructuralBraces(lines[i]);
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }

            if (end < openBrace)
                throw new InvalidDataException("Bloco da conta não possui fechamento válido.");

            File.Copy(vdfPath, backupPath, overwrite: true);
            lines.RemoveRange(start, end - start + 1);

            try
            {
                File.WriteAllLines(tempPath, lines);
                File.Move(tempPath, vdfPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }, ct);

        var autoLoginUser = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "AutoLoginUser",
            string.Empty)?.ToString();

        if (account.IsActive || account.MostRecent || account.AutoLogin
            || string.Equals(autoLoginUser, account.AccountName,
                StringComparison.OrdinalIgnoreCase))
        {
            Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                "AutoLoginUser",
                string.Empty);
            Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                "RememberPassword",
                0,
                RegistryValueKind.DWord);
        }

        InvalidateSnapshot();

        logger.LogInformation("Conta {Account} esquecida, backup em {Backup}",
            account.AccountName, backupPath);
    }

    public async Task<IReadOnlyList<string>> ForgetAccountsAsync(
        IReadOnlyCollection<string> steamIds64,
        CancellationToken ct = default)
    {
        if (steamIds64.Count == 0) return [];
        if (string.IsNullOrEmpty(_steamPath))
            throw new InvalidOperationException("Steam não encontrada.");

        var vdfPath = locator.GetLoginUsersVdfPath(_steamPath);
        if (!File.Exists(vdfPath))
            throw new FileNotFoundException("loginusers.vdf não encontrado.", vdfPath);

        var snapshot = await GetSnapshotAsync(ct);
        var protectedIds = new HashSet<string>(StringComparer.Ordinal);
        if (snapshot.ActiveAccount is not null)
            protectedIds.Add(snapshot.ActiveAccount.SteamId64);

        var autoLoginUser = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "AutoLoginUser",
            string.Empty)?.ToString();
        var registryAccount = snapshot.Accounts.FirstOrDefault(account =>
            string.Equals(
                account.AccountName,
                autoLoginUser,
                StringComparison.OrdinalIgnoreCase));
        if (registryAccount is not null)
            protectedIds.Add(registryAccount.SteamId64);

        var targets = steamIds64
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => !protectedIds.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        if (targets.Count == 0) return [];

        await CloseSteamAsync(SteamCloseMethod.Graceful, ct);

        var backupPath = vdfPath + ".cleanup.bak";
        var tempPath = vdfPath + ".cleanup.tmp";
        var removedIds = await Task.Run<IReadOnlyList<string>>(() =>
        {
            var lines = File.ReadAllLines(vdfPath).ToList();
            var ranges = new List<(string SteamId64, int Start, int End)>();

            foreach (var steamId64 in targets)
            {
                var start = FindAccountBlockStart(lines, steamId64);
                if (start < 0) continue;

                var openBrace = -1;
                for (var index = start + 1; index < lines.Count; index++)
                {
                    if (string.IsNullOrWhiteSpace(lines[index])) continue;
                    if (lines[index].Trim() == "{") openBrace = index;
                    break;
                }
                if (openBrace < 0) continue;

                var depth = 0;
                var end = -1;
                for (var index = openBrace; index < lines.Count; index++)
                {
                    depth += CountStructuralBraces(lines[index]);
                    if (depth == 0)
                    {
                        end = index;
                        break;
                    }
                }
                if (end >= openBrace) ranges.Add((steamId64, start, end));
            }

            if (ranges.Count == 0) return [];

            File.Copy(vdfPath, backupPath, overwrite: true);
            foreach (var range in ranges.OrderByDescending(range => range.Start))
                lines.RemoveRange(range.Start, range.End - range.Start + 1);

            try
            {
                File.WriteAllLines(tempPath, lines);
                File.Move(tempPath, vdfPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            return ranges.Select(range => range.SteamId64).ToList();
        }, ct);

        if (removedIds.Count > 0)
        {
            InvalidateSnapshot();
            logger.LogInformation(
                "{Count} contas antigas removidas; backup em {Backup}",
                removedIds.Count,
                backupPath);
        }

        return removedIds;
    }

    private static int FindAccountBlockStart(List<string> lines, string steamId64)
    {
        var pattern = $@"^\s*""{Regex.Escape(steamId64)}""\s*$";
        for (var i = 0; i < lines.Count; i++)
        {
            if (Regex.IsMatch(lines[i], pattern)) return i;
        }
        return -1;
    }

    private static int CountStructuralBraces(string line)
    {
        var delta = 0;
        var insideQuotes = false;
        var escaped = false;

        foreach (var character in line)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (character == '\\' && insideQuotes)
            {
                escaped = true;
                continue;
            }
            if (character == '"')
            {
                insideQuotes = !insideQuotes;
                continue;
            }
            if (insideQuotes) continue;
            if (character == '{') delta++;
            else if (character == '}') delta--;
        }

        return delta;
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
            InvalidateSnapshot();
        }, ct);
    }

    private void InvalidateSnapshot()
    {
        _cachedSnapshot = null;
        _cachedVdfLength = -1;
        _cachedVdfWriteTicks = -1;
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
