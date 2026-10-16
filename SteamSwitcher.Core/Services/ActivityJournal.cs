using System.Text.Json;

namespace SteamSwitcher.Core.Services;

public enum ActivityKind
{
    AccountSwitched, AccountSwitchFailed, GameStarted, GameLaunchFailed,
    PreferenceRepaired, RepairFailed, UpdateReady, UpdateFailed, SettingsRecovered
}

public sealed record ActivityEntry(DateTimeOffset Timestamp, ActivityKind Kind,
    string? SourceAccount = null, string? TargetAccount = null, string? Subject = null,
    string? Operation = null, string? Installation = null, string[]? Accounts = null)
{
    public bool NeedsAttention => Kind is ActivityKind.AccountSwitchFailed or ActivityKind.GameLaunchFailed
        or ActivityKind.RepairFailed or ActivityKind.UpdateFailed;
    public string TimeText => Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    [System.Text.Json.Serialization.JsonIgnore]
    public string RelativeTime
    {
        get
        {
            var local = Timestamp.ToLocalTime();
            var diff = DateTime.Now - local.DateTime;
            if (diff.TotalSeconds < 60 && diff.TotalSeconds >= 0) return "Agora mesmo";
            if (diff.TotalMinutes < 60 && diff.TotalMinutes >= 0)
            {
                var m = Math.Max(1, (int)diff.TotalMinutes);
                return m == 1 ? "1 min atrás" : $"{m} min atrás";
            }
            if (local.Date == DateTime.Today) return $"Hoje às {local:HH:mm}";
            if (local.Date == DateTime.Today.AddDays(-1)) return $"Ontem às {local:HH:mm}";
            if (diff.TotalDays < 7 && diff.TotalDays >= 0)
            {
                var d = (int)diff.TotalDays;
                return d == 1 ? "1 dia atrás" : $"{d} dias atrás";
            }
            return local.ToString("dd/MM/yyyy HH:mm");
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string CategoryName => Kind switch
    {
        ActivityKind.AccountSwitched or ActivityKind.AccountSwitchFailed => "Troca de Conta",
        ActivityKind.GameStarted or ActivityKind.GameLaunchFailed => "Jogo",
        ActivityKind.PreferenceRepaired or ActivityKind.RepairFailed => "Correção",
        ActivityKind.UpdateReady or ActivityKind.UpdateFailed => "Atualização",
        ActivityKind.SettingsRecovered => "Configurações",
        _ => "Geral"
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusName => NeedsAttention ? "Falha" : "Sucesso";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasTransition => Kind is ActivityKind.AccountSwitched or ActivityKind.AccountSwitchFailed
        && (!string.IsNullOrWhiteSpace(SourceAccount) || !string.IsNullOrWhiteSpace(TargetAccount));

    [System.Text.Json.Serialization.JsonIgnore]
    public string TransitionSummary => $"{SourceAccount ?? "Conta anterior"}  ➔  {TargetAccount ?? "Destino"}";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSubject => !string.IsNullOrWhiteSpace(Subject);

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Subtitle => Kind switch
    {
        ActivityKind.AccountSwitched or ActivityKind.AccountSwitchFailed => HasTransition ? TransitionSummary : null,
        ActivityKind.GameStarted or ActivityKind.GameLaunchFailed => !string.IsNullOrWhiteSpace(Subject) ? $"🎮 {Subject}" : null,
        ActivityKind.PreferenceRepaired or ActivityKind.RepairFailed => Operation != null ? $"🔧 {Operation}" : null,
        _ => null
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAccountSwitch => Kind is ActivityKind.AccountSwitched or ActivityKind.AccountSwitchFailed;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsGame => Kind is ActivityKind.GameStarted or ActivityKind.GameLaunchFailed;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsRepair => Kind is ActivityKind.PreferenceRepaired or ActivityKind.RepairFailed;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsGeneral => !IsAccountSwitch && !IsGame && !IsRepair;

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplaySourceAccount => SourceAccount ?? "Conta anterior não identificada";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayTargetAccount => TargetAccount ?? "Conta de destino não identificada";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasInstallation => !string.IsNullOrWhiteSpace(Installation);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAccounts => Accounts is { Length: > 0 };

    [System.Text.Json.Serialization.JsonIgnore]
    public string? FailureReason => NeedsAttention ? Kind switch
    {
        ActivityKind.AccountSwitchFailed => "A troca de contas não pôde ser concluída. A Steam pode estar em execução travada ou os arquivos de configuração estão bloqueados.",
        ActivityKind.GameLaunchFailed => "O jogo não pôde ser iniciado. Verifique se o executável existe e se os arquivos do jogo estão íntegros.",
        ActivityKind.RepairFailed => "Não foi possível concluir a correção automática das preferências da Steam.",
        ActivityKind.UpdateFailed => "O download do pacote de atualização falhou. Verifique sua conexão de rede.",
        _ => "A operação encontrou um erro. Consulte o diagnóstico para mais informações."
    } : null;
    public string Title => Kind is ActivityKind.PreferenceRepaired or ActivityKind.RepairFailed && Operation is not null
        ? $"{Operation} · {(NeedsAttention ? "falha na correção" : "corrigido")}"
        : Kind switch
    {
        ActivityKind.AccountSwitched => "Troca de conta concluída",
        ActivityKind.AccountSwitchFailed => "Não foi possível trocar de conta",
        ActivityKind.GameStarted => "Início do jogo confirmado",
        ActivityKind.GameLaunchFailed => "Não foi possível iniciar o jogo",
        ActivityKind.PreferenceRepaired => "Configuração da Steam corrigida",
        ActivityKind.RepairFailed => "Não foi possível concluir a correção",
        ActivityKind.UpdateReady => "Atualização pronta para instalar",
        ActivityKind.SettingsRecovered => "Configurações recuperadas do backup",
        _ => "Não foi possível concluir a atualização"
    };
    public string Detail => Kind is ActivityKind.AccountSwitched or ActivityKind.AccountSwitchFailed
        ? $"De: {SourceAccount ?? "Conta anterior não identificada"}\nPara: {TargetAccount ?? "Conta de destino não identificada"}"
            + (NeedsAttention ? "\nTroca não concluída. Consulte o diagnóstico." : "")
        : Kind is ActivityKind.PreferenceRepaired or ActivityKind.RepairFailed
        ? $"Steam: {Installation ?? "instalação não registrada"}\n"
            + (Accounts is { Length: > 0 } ? "Contas: " + string.Join("; ", Accounts) : $"Conta: {TargetAccount ?? "não registrada"}")
            + (Operation is null ? "\nConfiguração não registrada neste histórico antigo." : "")
        : Kind is ActivityKind.GameStarted or ActivityKind.GameLaunchFailed && !string.IsNullOrWhiteSpace(Subject)
        ? $"Jogo: {Subject}" + (TargetAccount is null ? "" : $"\nConta: {TargetAccount}")
        : Kind == ActivityKind.SettingsRecovered
        ? "O arquivo principal não pôde ser lido. Revise suas preferências."
        : Kind == ActivityKind.UpdateReady
        ? "Download do pacote de atualização concluído; instalação ainda pendente."
        : Kind == ActivityKind.UpdateFailed
        ? "O download do pacote de atualização não foi concluído."
        : NeedsAttention
        ? "Consulte o diagnóstico antes de repetir a operação."
        : "Operação concluída pelo SteamNexus.";
}

/// <summary>Bounded local journal with display labels, never credentials or raw exceptions.</summary>
public sealed class ActivityJournal
{
    public static ActivityJournal Shared { get; private set; } = new();
    public static void InitializePersistent()
    {
        if (Shared._path is not null) return;
        Shared = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamSwitcher", "activity.json"));
    }
    private readonly object _gate = new();
    private readonly Queue<ActivityEntry> _entries = new();
    private readonly string? _path;
    public string? StorageError { get; private set; }

    // A null path is an isolated in-memory journal (used by unit tests).
    public ActivityJournal(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<List<ActivityEntry>>(File.ReadAllText(path)) ?? [];
            foreach (var entry in saved.Where(e => e is not null && Enum.IsDefined(e.Kind)).TakeLast(200))
                _entries.Enqueue(entry with { SourceAccount = Label(entry.SourceAccount), TargetAccount = Label(entry.TargetAccount), Subject = Label(entry.Subject),
                    Operation = Label(entry.Operation), Installation = Label(entry.Installation), Accounts = entry.Accounts?.Select(a => Label(a) ?? "Conta não identificada").ToArray() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { StorageError = "Não foi possível carregar o histórico salvo."; }
    }
    public IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (_gate) return _entries.Reverse().ToArray();
    }
    public void Record(ActivityKind kind, string? sourceAccount = null, string? targetAccount = null, string? subject = null,
        string? operation = null, string? installation = null, string[]? accounts = null)
    {
        lock (_gate)
        {
            _entries.Enqueue(new(DateTimeOffset.UtcNow, kind, Label(sourceAccount), Label(targetAccount), Label(subject),
                Label(operation), Label(installation), accounts?.Select(a => Label(a) ?? "Conta não identificada").ToArray()));
            while (_entries.Count > 200) _entries.Dequeue();
            Save();
        }
    }
    public void Clear() { lock (_gate) { _entries.Clear(); Save(); } }
    private void Save()
    {
        if (_path is null) return;
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var data = _entries.Select(e => new { e.Timestamp, e.Kind, e.SourceAccount, e.TargetAccount, e.Subject, e.Operation, e.Installation, e.Accounts });
            File.WriteAllText(temp, JsonSerializer.Serialize(data));
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.Flush(true);
            File.Move(temp, _path, overwrite: true);
            StorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StorageError = "Não foi possível salvar o histórico. Os registros atuais estão apenas nesta sessão."; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private static string? Label(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : new string(value.Where(c => !char.IsControl(c)).Take(200).ToArray()).Trim();
}
