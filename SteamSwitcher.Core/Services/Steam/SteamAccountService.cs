using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace SteamSwitcher.Core.Services;

public class SteamAccountService(
    ISteamLocatorService locator,
    ISteamInstallationService installationService,
    IAppSettingsService settingsService,
    ISteamKnownAccountStore knownAccountStore,
    ILogger<SteamAccountService> logger) : ISteamAccountService
{
    public bool IsOperationInProgress => _steamMutationGate.CurrentCount == 0;
    private string SteamPath => _operationContext.Value?.RootPath
        ?? installationService.SelectedInstallation?.RootPath
        ?? string.Empty;
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly SemaphoreSlim _steamMutationGate = new(1, 1);
    private readonly AsyncLocal<SteamOperationContext?> _operationContext = new();
    private SteamAccountsSnapshot? _cachedSnapshot;
    private string? _cachedInstallationId;
    private long _cachedVdfLength = -1;
    private long _cachedVdfWriteTicks = -1;

    public async Task<SteamAccountsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var installationId = _operationContext.Value?.InstallationId
            ?? installationService.SelectedInstallation?.Id;
        if (!string.Equals(_cachedInstallationId, installationId, StringComparison.Ordinal))
        {
            InvalidateSnapshot();
            _cachedInstallationId = installationId;
        }

        if (string.IsNullOrEmpty(SteamPath))
        {
            logger.LogWarning("Steam não encontrado");
            return SteamAccountsSnapshot.Empty;
        }

        var vdfPath = locator.GetLoginUsersVdfPath(SteamPath);
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

    public async Task<IReadOnlyList<SteamAccount>> GetAllAccountsAsync(
        CancellationToken ct = default)
    {
        var installations = installationService.Installations
            .Where(i => i.IsValid)
            .ToList();
        if (installations.Count == 0) return [];

        var activeInstallationId = FindRunningInstallationId(installations);
        var tasks = installations.Select(async installation =>
        {
            if (!File.Exists(installation.LoginUsersPath))
                return (IReadOnlyList<SteamAccount>)[];

            try
            {
                await using var stream = new FileStream(
                    installation.LoginUsersPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    useAsync: true);
                var snapshot = SteamAccountSnapshotParser.Parse(stream);
                var activeId = installation.Id == activeInstallationId
                    ? snapshot.ActiveAccount?.SteamId64
                    : null;

                foreach (var account in snapshot.Accounts)
                {
                    account.InstallationId = installation.Id;
                    account.InstallationName = installation.DisplayName;
                    account.InstallationRootPath = installation.RootPath;
                    account.IsActive = account.SteamId64 == activeId;
                }

                return snapshot.Accounts;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Não foi possível ler contas da instalação {Installation}",
                    installation.RootPath);
                return (IReadOnlyList<SteamAccount>)[];
            }
        });

        var accountGroups = await Task.WhenAll(tasks);
        var accounts = accountGroups.SelectMany(group => group).ToList();
        var stored = await knownAccountStore.LoadAsync(ct);
        var installationsById = installations.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var presentKeys = accounts.Select(a => a.UniqueKey).ToHashSet(StringComparer.Ordinal);

        foreach (var record in stored.Values)
        {
            var uniqueKey = $"{record.InstallationId}:{record.SteamId64}";
            if (presentKeys.Contains(uniqueKey)
                || !installationsById.TryGetValue(record.InstallationId, out var installation)
                || string.IsNullOrWhiteSpace(record.AccountName))
                continue;

            accounts.Add(new SteamAccount
            {
                InstallationId = installation.Id,
                InstallationName = installation.DisplayName,
                InstallationRootPath = installation.RootPath,
                SteamId64 = record.SteamId64,
                AccountName = record.AccountName,
                PersonaName = record.PersonaName,
                Timestamp = record.Timestamp,
                IsArchived = true,
            });
            presentKeys.Add(uniqueKey);
        }

        await knownAccountStore.RememberAsync(accounts, ct);
        return accounts;
    }

    public async Task SwitchAccountAsync(
        SteamAccount account,
        LoginState? stateOverride = null,
        CancellationToken ct = default)
        => await RunSteamMutationAsync(
            () => SwitchAccountCoreAsync(account, stateOverride, ct),
            ct,
            account.InstallationId);

    private async Task SwitchAccountCoreAsync(
        SteamAccount account,
        LoginState? stateOverride,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);

        var operation = Stopwatch.StartNew();
        var settings = settingsService.Current;
        var requestedState = stateOverride
            ?? account.LoginStateOverride
            ?? settings.DefaultLoginStateOverride;
        var targetState = requestedState == LoginState.Offline
            ? LoginState.Offline
            : LoginState.Online;

        logger.LogInformation(
            "Troca Steam iniciada. Target={Target}, State={State}",
            MaskSteamId(account.SteamId64), targetState);

        if (await PreflightSwitchAsync(
                account,
                targetState,
                ct))
        {
            logger.LogInformation(
                "Troca Steam ignorada: a conta solicitada já está ativa. Target={Target}, ElapsedMs={ElapsedMs}",
                MaskSteamId(account.SteamId64), operation.ElapsedMilliseconds);
            return;
        }

        LogSwitchPhase("preflight", operation, account.SteamId64);

        // 1. Fecha Steam
        await CloseSteamCombinedAsync(ct);
        LogSwitchPhase("steam-closed", operation, account.SteamId64);

        SteamSwitchBackup? backup = null;
        try
        {
            // 2. Edita loginusers.vdf
            backup = await UpdateLoginUsersVdfAsync(account, targetState, ct);
            LogSwitchPhase("vdf-updated", operation, account.SteamId64);

            // 3. Atualiza registro
            UpdateRegistry(account);
            LogSwitchPhase("registry-updated", operation, account.SteamId64);

            try
            {
                await DisableAccountChooserAsync(ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Esta preferência é auxiliar. Uma falha ao alterar config.vdf
                // não deve impedir uma troca cujo VDF de login já está válido.
                logger.LogWarning(ex, "Não foi possível desativar o seletor de contas da Steam");
            }
            LogSwitchPhase("chooser-disabled", operation, account.SteamId64);

            await ValidateSwitchStateAsync(
                account,
                targetState,
                validateRegistry: true,
                ct);
            LogSwitchPhase("state-validated", operation, account.SteamId64);

            // 4. Abre Steam
            await StartSteamAsync(settings, ct);
            LogSwitchPhase("steam-started", operation, account.SteamId64);
        }
        catch
        {
            if (backup is not null)
            {
                try
                {
                    await CloseSteamCombinedAsync(CancellationToken.None);
                    await RestoreSwitchBackupAsync(backup);
                    logger.LogWarning(
                        "Troca Steam revertida após falha. Target={Target}",
                        MaskSteamId(account.SteamId64));
                }
                catch (Exception rollbackError)
                {
                    logger.LogError(
                        rollbackError,
                        "Falha ao restaurar estado anterior da Steam. Target={Target}",
                        MaskSteamId(account.SteamId64));
                }
            }

            throw;
        }

        logger.LogInformation(
            "Troca Steam concluída. Target={Target}, ElapsedMs={ElapsedMs}",
            MaskSteamId(account.SteamId64), operation.ElapsedMilliseconds);
    }

    private async Task<bool> PreflightSwitchAsync(
        SteamAccount target,
        LoginState targetState,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(SteamPath) || !Directory.Exists(SteamPath))
            throw new InvalidOperationException("A instalação da Steam não foi encontrada.");

        var steamExe = locator.GetSteamExePath(SteamPath);
        if (!File.Exists(steamExe))
            throw new FileNotFoundException("O executável da Steam não foi encontrado.", steamExe);

        var vdfPath = locator.GetLoginUsersVdfPath(SteamPath);
        if (!File.Exists(vdfPath))
        {
            if (string.IsNullOrWhiteSpace(target.AccountName))
                throw new InvalidOperationException(
                    "O loginusers.vdf está ausente e a conta não possui nome de login para recuperação.");
            logger.LogWarning(
                "loginusers.vdf ausente; será reconstruído para a conta selecionada. Target={Target}",
                MaskSteamId(target.SteamId64));
            return false;
        }

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
        {
            if (string.IsNullOrWhiteSpace(target.AccountName))
                throw new InvalidOperationException(
                    "A conta selecionada não existe mais no loginusers.vdf e não possui nome de login para recuperação.");

            persisted = target;
            logger.LogInformation(
                "Conta será restaurada no loginusers.vdf antes da troca. Target={Target}",
                MaskSteamId(target.SteamId64));
        }

        if (string.IsNullOrWhiteSpace(persisted.AccountName))
            throw new InvalidOperationException("A conta selecionada não possui um nome de login válido.");

        if (!persisted.RememberPassword)
        {
            logger.LogWarning(
                "A conta de destino não está marcada como lembrada; a Steam poderá solicitar autenticação. Target={Target}",
                MaskSteamId(target.SteamId64));
        }

        var targetIsAlreadyActive = string.Equals(
            snapshot.ActiveAccount?.SteamId64,
            target.SteamId64,
            StringComparison.Ordinal);

        // O VDF sozinho não basta: a conta pode estar marcada como ativa na
        // instalação selecionada enquanto outra cópia da Steam está executando.
        // Só ignoramos a troca quando a instância correta já é a única aberta.
        var persistedStateMatches = persisted.WantsOfflineMode
            == (targetState == LoginState.Offline);

        return targetIsAlreadyActive
            && persistedStateMatches
            && IsOnlySelectedSteamMainProcessRunning();
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

        return (await GetAllAccountsAsync(ct)).FirstOrDefault(account => account.IsActive);
    }

    private static string? FindRunningInstallationId(
        IReadOnlyList<SteamInstallation> installations)
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var executablePath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executablePath)) continue;
                    var installation = installations.FirstOrDefault(item =>
                        string.Equals(
                            Path.GetFullPath(item.SteamExePath),
                            Path.GetFullPath(executablePath),
                            StringComparison.OrdinalIgnoreCase));
                    if (installation is not null)
                        return installation.Id;
                }
                catch
                {
                    // Processos protegidos não impedem o fallback para a seleção padrão.
                }
            }

            return null;
        }
        finally
        {
            DisposeProcesses(processes);
        }
    }

    public async Task ForgetAccountAsync(
        SteamAccount account,
        CancellationToken ct = default)
        => await RunSteamMutationAsync(
            () => ForgetAccountCoreAsync(account, ct),
            ct,
            account.InstallationId);

    private async Task ForgetAccountCoreAsync(
        SteamAccount account,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(SteamPath))
            throw new InvalidOperationException("Steam não encontrada.");

        var vdfPath = locator.GetLoginUsersVdfPath(SteamPath);
        if (!File.Exists(vdfPath))
        {
            await knownAccountStore.RemoveAsync([account.UniqueKey], ct);
            logger.LogInformation(
                "Conta arquivada removida sem VDF presente. Target={Target}",
                MaskSteamId(account.SteamId64));
            return;
        }

        var currentSnapshot = await GetSnapshotAsync(ct);
        if (!currentSnapshot.Accounts.Any(item =>
                string.Equals(item.SteamId64, account.SteamId64, StringComparison.Ordinal)))
        {
            await knownAccountStore.RemoveAsync([account.UniqueKey], ct);
            logger.LogInformation(
                "Conta arquivada removida do armazenamento local. Target={Target}",
                MaskSteamId(account.SteamId64));
            return;
        }

        await CloseSteamCombinedAsync(ct);

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
        await knownAccountStore.RemoveAsync([account.UniqueKey], ct);
    }

    public async Task<IReadOnlyList<string>> ForgetAccountsAsync(
        IReadOnlyCollection<string> steamIds64,
        CancellationToken ct = default)
        => await RunSteamMutationAsync(
            () => ForgetAccountsCoreAsync(steamIds64, ct),
            ct);

    public async Task<IReadOnlyList<string>> ForgetAccountsAsync(
        IReadOnlyCollection<SteamAccount> accounts,
        CancellationToken ct = default)
    {
        var removedKeys = new List<string>();
        foreach (var group in accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.InstallationId))
            .GroupBy(account => account.InstallationId))
        {
            var groupAccounts = group.ToList();
            var removedIds = await RunSteamMutationAsync(
                () => ForgetAccountsCoreAsync(
                    groupAccounts.Select(account => account.SteamId64).ToList(),
                    ct),
                ct,
                group.Key);
            removedKeys.AddRange(groupAccounts
                .Where(account => removedIds.Contains(account.SteamId64, StringComparer.Ordinal))
                .Select(account => account.UniqueKey));
        }

        await knownAccountStore.RemoveAsync(removedKeys, ct);
        return removedKeys;
    }

    private async Task<IReadOnlyList<string>> ForgetAccountsCoreAsync(
        IReadOnlyCollection<string> steamIds64,
        CancellationToken ct)
    {
        if (steamIds64.Count == 0) return [];
        if (string.IsNullOrEmpty(SteamPath))
            throw new InvalidOperationException("Steam não encontrada.");

        var vdfPath = locator.GetLoginUsersVdfPath(SteamPath);
        if (!File.Exists(vdfPath))
            return steamIds64
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

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

        await CloseSteamCombinedAsync(ct);

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

            var archivedOnlyIds = targets
                .Where(id => snapshot.Accounts.All(account => account.SteamId64 != id))
                .ToList();
            if (ranges.Count == 0) return archivedOnlyIds;

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

            return ranges.Select(range => range.SteamId64)
                .Concat(archivedOnlyIds)
                .Distinct(StringComparer.Ordinal)
                .ToList();
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

    private static readonly string[] SteamProcessNames =
        ["steam", "steamwebhelper", "GameOverlayUI"];

    private async Task CloseSteamCombinedAsync(CancellationToken ct)
    {
        var initialProcesses = GetAllSteamProcesses();
        if (initialProcesses.Count == 0)
        {
            await WaitForVdfReleaseAsync(ct);
            return;
        }

        var conflictingCount = initialProcesses.Count(process =>
            !ProcessBelongsToSelectedInstallation(process));

        logger.LogInformation(
            "Encerrando Steam pelo fluxo combinado. Processes={ProcessCount}, ConflictingInstallations={ConflictingCount}, Details={ProcessDetails}, ClientServiceRunning={ClientServiceRunning}",
            initialProcesses.Count,
            conflictingCount,
            DescribeProcesses(initialProcesses),
            IsSteamClientServiceRunning());
        DisposeProcesses(initialProcesses);

        var steamExe = locator.GetSteamExePath(SteamPath);
        if (File.Exists(steamExe))
        {
            try
            {
                using var shutdown = Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = "-shutdown",
                    WorkingDirectory = SteamPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao solicitar shutdown nativo da Steam");
            }

            if (await WaitForSteamExitAsync(TimeSpan.FromSeconds(6), ct))
            {
                logger.LogInformation("Steam encerrada pelo comando nativo -shutdown");
                await WaitForVdfReleaseAsync(ct);
                return;
            }

            logger.LogWarning(
                "O comando nativo -shutdown não encerrou todos os processos; tentando fechamento de janelas");
        }

        var gracefulProcesses = GetAllSteamProcesses();
        foreach (var process in gracefulProcesses)
        {
            try
            {
                if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Falha ao fechar janela Steam. Pid={Pid}", SafeProcessId(process));
            }
        }
        DisposeProcesses(gracefulProcesses);

        if (await WaitForSteamExitAsync(TimeSpan.FromSeconds(2), ct))
        {
            logger.LogInformation("Steam encerrada pelo fechamento das janelas");
            await WaitForVdfReleaseAsync(ct);
            return;
        }

        var remaining = GetAllSteamProcesses();
        logger.LogWarning(
            "Steam não encerrou no prazo; aplicando fallback forçado. Processes={ProcessCount}",
            remaining.Count);
        foreach (var process in remaining)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Não foi possível encerrar processo Steam. Pid={Pid}", SafeProcessId(process));
            }
        }
        DisposeProcesses(remaining);

        if (!await WaitForSteamExitAsync(TimeSpan.FromSeconds(5), ct))
        {
            var blockedProcesses = GetAllSteamProcesses();
            var details = DescribeProcesses(blockedProcesses);
            DisposeProcesses(blockedProcesses);
            throw new InvalidOperationException(
                $"A Steam não pôde ser encerrada. Processos restantes: {details}. " +
                "Feche-os manualmente e tente novamente.");
        }

        logger.LogInformation("Steam encerrada pelo fallback forçado");
        await WaitForVdfReleaseAsync(ct);
    }

    private static List<Process> GetAllSteamProcesses()
    {
        var result = new List<Process>();
        foreach (var processName in SteamProcessNames)
            result.AddRange(Process.GetProcessesByName(processName));
        return result;
    }

    private List<Process> GetSelectedSteamProcesses()
    {
        var result = new List<Process>();
        foreach (var processName in SteamProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (ProcessBelongsToSelectedInstallation(process))
                    result.Add(process);
                else
                    process.Dispose();
            }
        }
        return result;
    }

    private bool ProcessBelongsToSelectedInstallation(Process process)
    {
        try
        {
            var executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath)) return false;
            var root = Path.GetFullPath(SteamPath)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(executablePath)
                .StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
            process.Dispose();
    }

    private static string DescribeProcesses(IEnumerable<Process> processes)
    {
        var descriptions = new List<string>();
        foreach (var process in processes)
        {
            try
            {
                var path = process.MainModule?.FileName ?? "caminho indisponível";
                descriptions.Add($"{process.ProcessName} (PID {process.Id}, {path})");
            }
            catch
            {
                descriptions.Add($"{process.ProcessName} (PID {SafeProcessId(process)})");
            }
        }

        return descriptions.Count == 0
            ? "nenhum"
            : string.Join("; ", descriptions);
    }

    private static bool IsSteamClientServiceRunning()
    {
        var processes = Process.GetProcessesByName("SteamService");
        try
        {
            return processes.Any(process => !process.HasExited);
        }
        finally
        {
            DisposeProcesses(processes);
        }
    }

    private static int SafeProcessId(Process process)
    {
        try { return process.Id; }
        catch { return -1; }
    }

    private async Task<bool> WaitForSteamExitAsync(
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var processes = GetAllSteamProcesses();
            var hasProcesses = processes.Count > 0;
            DisposeProcesses(processes);
            if (!hasProcesses) return true;
            await Task.Delay(200, ct);
        }

        var remaining = GetAllSteamProcesses();
        var exited = remaining.Count == 0;
        DisposeProcesses(remaining);
        return exited;
    }

    private async Task WaitForVdfReleaseAsync(CancellationToken ct)
    {
        var vdfPath = locator.GetLoginUsersVdfPath(SteamPath);
        if (!File.Exists(vdfPath)) return;
        var firstAttempt = await TryWaitForVdfReleaseAsync(
            vdfPath, TimeSpan.FromSeconds(5), ct);
        if (firstAttempt is null)
            return;

        if (IsSteamClientServiceRunning())
        {
            logger.LogWarning(
                "O loginusers.vdf continua bloqueado; tentando parar o Steam Client Service como último recurso");
            if (await TryStopSteamClientServiceAsync(ct))
            {
                var retry = await TryWaitForVdfReleaseAsync(
                    vdfPath, TimeSpan.FromSeconds(3), ct);
                if (retry is null)
                {
                    logger.LogInformation(
                        "loginusers.vdf liberado após parar o Steam Client Service");
                    return;
                }

                firstAttempt = retry;
            }
        }

        throw new IOException(
            $"A Steam foi encerrada, mas o arquivo '{vdfPath}' continua em uso. " +
            $"Steam Client Service ativo: {IsSteamClientServiceRunning()}.",
            firstAttempt);
    }

    private static async Task<Exception?> TryWaitForVdfReleaseAsync(
        string vdfPath,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(
                    vdfPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                await Task.Delay(200, ct);
            }
        }

        return lastError ?? new IOException("O arquivo não foi liberado no prazo esperado.");
    }

    private async Task<bool> TryStopSteamClientServiceAsync(CancellationToken ct)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "stop \"Steam Client Service\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return false;

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                logger.LogWarning(
                    "Não foi possível parar o Steam Client Service. ExitCode={ExitCode}",
                    process.ExitCode);
                return false;
            }

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && IsSteamClientServiceRunning())
                await Task.Delay(200, ct);
            return !IsSteamClientServiceRunning();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao parar o Steam Client Service");
            return false;
        }
    }

    private async Task<SteamSwitchBackup> UpdateLoginUsersVdfAsync(
        SteamAccount target,
        LoginState? state,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var vdfPath = locator.GetLoginUsersVdfPath(SteamPath);
            var vdfExisted = File.Exists(vdfPath);
            var originalVdf = vdfExisted ? File.ReadAllBytes(vdfPath) : [];
            var previousAutoLoginUser = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                "AutoLoginUser",
                null);
            var previousRememberPassword = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                "RememberPassword",
                null);

            using var output = new MemoryStream();
            if (vdfExisted)
            {
                using var input = new MemoryStream(originalVdf, writable: false);
                SteamLoginUsersEditor.Rewrite(
                    input, output, target.SteamId64,
                    state ?? LoginState.Online, target);
            }
            else
            {
                SteamLoginUsersEditor.Create(
                    output, target, state ?? LoginState.Online);
            }

            WriteVdfAtomically(vdfPath, output.ToArray(), createBackup: true);
            InvalidateSnapshot();
            return new SteamSwitchBackup(
                vdfPath,
                originalVdf,
                vdfExisted,
                previousAutoLoginUser,
                previousRememberPassword);
        }, ct);
    }

    private static void WriteVdfAtomically(
        string vdfPath,
        byte[] contents,
        bool createBackup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(vdfPath)!);
        var tempPath = vdfPath + ".tmp";
        var backupPath = createBackup ? vdfPath + "_last" : null;

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(vdfPath))
                File.Replace(tempPath, vdfPath, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, vdfPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private async Task RestoreSwitchBackupAsync(SteamSwitchBackup backup)
    {
        await Task.Run(() =>
        {
            if (backup.VdfExisted)
                WriteVdfAtomically(backup.VdfPath, backup.OriginalVdf, createBackup: false);
            else if (File.Exists(backup.VdfPath))
                File.Delete(backup.VdfPath);
            RestoreRegistryValue("AutoLoginUser", backup.AutoLoginUser);
            RestoreRegistryValue("RememberPassword", backup.RememberPassword);
            InvalidateSnapshot();
        });
    }

    private static void RestoreRegistryValue(string name, object? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam", writable: true);
        if (value is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value);
    }

    private sealed record SteamSwitchBackup(
        string VdfPath,
        byte[] OriginalVdf,
        bool VdfExisted,
        object? AutoLoginUser,
        object? RememberPassword);

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

    private async Task ValidateSwitchStateAsync(
        SteamAccount target,
        LoginState state,
        bool validateRegistry,
        CancellationToken ct)
    {
        var vdfPath = locator.GetLoginUsersVdfPath(SteamPath);
        SteamAccountsSnapshot? snapshot = null;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            ct.ThrowIfCancellationRequested();
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
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                await Task.Delay(250, ct);
            }
        }

        if (snapshot is null)
            throw new IOException("Não foi possível validar o loginusers.vdf após a troca.", lastError);

        var autoLoginAccounts = snapshot.Accounts.Where(a => a.AutoLogin).ToList();
        var mostRecentAccounts = snapshot.Accounts.Where(a => a.MostRecent).ToList();
        var selected = snapshot.Accounts.FirstOrDefault(a =>
            string.Equals(a.SteamId64, target.SteamId64, StringComparison.Ordinal));

        if (selected is null
            || autoLoginAccounts.Count != 1
            || mostRecentAccounts.Count != 1
            || !string.Equals(autoLoginAccounts[0].SteamId64, target.SteamId64, StringComparison.Ordinal)
            || !string.Equals(mostRecentAccounts[0].SteamId64, target.SteamId64, StringComparison.Ordinal)
            || !selected.RememberPassword)
        {
            throw new InvalidDataException(
                "A Steam não confirmou a conta selecionada no loginusers.vdf.");
        }

        var expectsOffline = state == LoginState.Offline;
        if (selected.WantsOfflineMode != expectsOffline
            || snapshot.Accounts.Any(a =>
                a.SteamId64 != target.SteamId64 && a.WantsOfflineMode))
        {
            throw new InvalidDataException(
                "O modo de entrada da conta não foi persistido corretamente.");
        }

        if (!validateRegistry) return;

        var registryAccount = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "AutoLoginUser",
            null)?.ToString();
        var rememberPassword = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "RememberPassword",
            null);

        if (!string.Equals(registryAccount, target.AccountName, StringComparison.OrdinalIgnoreCase)
            || Convert.ToInt32(rememberPassword ?? 0) != 1)
        {
            throw new InvalidDataException(
                "O Registro da Steam não confirmou a conta selecionada.");
        }
    }

    private async Task StartSteamAsync(AppSettings settings, CancellationToken ct)
    {
        var steamExe = locator.GetSteamExePath(SteamPath);
        if (!File.Exists(steamExe)) return;

        var args = new List<string>();

        if (settings.StartSilent) args.Add("-silent");

        if (SteamLaunchPolicy.ShouldLaunchAsDesktopUser(
                IsCurrentProcessElevated(), settings.StartAsAdmin))
        {
            StartAsDesktopUser(steamExe, string.Join(" ", args), SteamPath);
            await Task.Delay(1500, ct);
            if (!IsSteamMainProcessRunning())
                throw new InvalidOperationException(
                    "A Steam não pôde ser iniciada no usuário da área de trabalho.");
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = steamExe,
            Arguments = string.Join(" ", args),
            WorkingDirectory = SteamPath,
            UseShellExecute = settings.StartAsAdmin,
            Verb = settings.StartAsAdmin ? "runas" : string.Empty,
        })
            ?? throw new InvalidOperationException("A Steam não pôde ser iniciada.");
        await Task.Delay(1500, ct);

        try
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    "A Steam foi iniciada, mas encerrou antes de concluir a troca.");
        }
        catch (InvalidOperationException) when (IsSteamMainProcessRunning())
        {
            // UseShellExecute pode entregar um processo intermediário enquanto a
            // instância real da Steam já está em execução.
        }
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void StartAsDesktopUser(
        string executable,
        string arguments,
        string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException(
                "O shell da área de trabalho não está disponível.");
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException(
                    "Não foi possível acessar o shell da área de trabalho.");
            shellType.InvokeMember(
                "ShellExecute",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [executable, arguments, workingDirectory, string.Empty, 1]);
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private async Task DisableAccountChooserAsync(CancellationToken ct)
    {
        var configPath = Path.Combine(SteamPath, "config", "config.vdf");
        if (!File.Exists(configPath))
            return;

        var content = await File.ReadAllTextAsync(configPath, ct);
        var updated = SteamConfigEditor.DisableAccountChooser(content, out var changed);
        if (!changed)
            return;

        var tempPath = configPath + ".steam-nexus.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, updated, ct);
            File.Move(tempPath, configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private bool IsSteamMainProcessRunning()
    {
        var processes = GetSelectedSteamProcesses()
            .Where(process => process.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var running = processes.Count > 0;
        DisposeProcesses(processes);
        return running;
    }

    private bool IsOnlySelectedSteamMainProcessRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            var selectedIsRunning = false;
            foreach (var process in processes)
            {
                if (ProcessBelongsToSelectedInstallation(process))
                    selectedIsRunning = true;
                else
                    return false;
            }

            return selectedIsRunning;
        }
        finally
        {
            DisposeProcesses(processes);
        }
    }

    public async Task AddAccountAsync(CancellationToken ct = default)
        => await RunSteamMutationAsync(
            () => AddAccountCoreAsync(ct),
            ct);

    private async Task AddAccountCoreAsync(CancellationToken ct)
    {
        // 1. Fecha Steam
        await CloseSteamCombinedAsync(ct);

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
        var steamExe = locator.GetSteamExePath(SteamPath);
        if (!File.Exists(steamExe)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = steamExe,
            UseShellExecute = true
        });
    }

    private async Task RunSteamMutationAsync(
        Func<Task> operation,
        CancellationToken ct,
        string? installationId = null)
    {
        if (!await _steamMutationGate.WaitAsync(0, ct))
            throw new InvalidOperationException(
                "Outra operação da Steam já está em andamento. Aguarde a conclusão e tente novamente.");

        try
        {
            _operationContext.Value = string.IsNullOrWhiteSpace(installationId)
                ? installationService.CaptureContext()
                : installationService.CaptureContext(installationId);
            await operation();
        }
        finally
        {
            _operationContext.Value = null;
            _steamMutationGate.Release();
        }
    }

    private async Task<T> RunSteamMutationAsync<T>(
        Func<Task<T>> operation,
        CancellationToken ct,
        string? installationId = null)
    {
        if (!await _steamMutationGate.WaitAsync(0, ct))
            throw new InvalidOperationException(
                "Outra operação da Steam já está em andamento. Aguarde a conclusão e tente novamente.");

        try
        {
            _operationContext.Value = string.IsNullOrWhiteSpace(installationId)
                ? installationService.CaptureContext()
                : installationService.CaptureContext(installationId);
            return await operation();
        }
        finally
        {
            _operationContext.Value = null;
            _steamMutationGate.Release();
        }
    }
}
