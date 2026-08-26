namespace SteamSwitcher.Core.Services;

public interface ISteamDiagnosticsService
{
    Task<SteamDiagnosticReport> CheckAsync(CancellationToken ct = default);
    Task DisableAccountChooserAsync(CancellationToken ct = default);
    Task RepairRegistryAsync(CancellationToken ct = default);
}

public sealed class SteamDiagnosticReport
{
    public DateTime CheckedAt { get; init; } = DateTime.Now;
    public string InstallationName { get; init; } = string.Empty;
    public string InstallationPath { get; init; } = string.Empty;
    public string RunningSteamPath { get; init; } = string.Empty;
    public string ActiveAccountName { get; init; } = string.Empty;
    public bool HasBlockingIssues => Items.Any(item => item.Severity == DiagnosticSeverity.Error);
    public bool CanDisableChooser { get; init; }
    public bool CanRepairRegistry { get; init; }
    public IReadOnlyList<SteamDiagnosticItem> Items { get; init; } = [];
}

public sealed record SteamDiagnosticItem(
    string Title,
    string Detail,
    DiagnosticSeverity Severity)
{
    public bool IsSuccess => Severity == DiagnosticSeverity.Success;
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;
    public bool IsError => Severity == DiagnosticSeverity.Error;
}

public enum DiagnosticSeverity { Success, Warning, Error }
