namespace SteamSwitcher.Core.Services;

public interface ISteamDiagnosticsService
{
    Task<SteamDiagnosticReport> CheckAsync(CancellationToken ct = default);
    Task<SteamInstallationDiagnosticReport> CheckInstallationAsync(string installationId, CancellationToken ct = default);
    Task DisableAccountChooserAsync(string installationId, CancellationToken ct = default);
    Task RepairRegistryAsync(string installationId, CancellationToken ct = default);
}

public sealed class SteamDiagnosticReport
{
    public DateTime CheckedAt { get; init; } = DateTime.Now;
    public bool HasBlockingIssues => Installations.Any(item => item.HasBlockingIssues);
    public IReadOnlyList<SteamInstallationDiagnosticReport> Installations { get; init; } = [];
}

public sealed class SteamInstallationDiagnosticReport
{
    public string InstallationId { get; init; } = string.Empty;
    public string InstallationName { get; init; } = string.Empty;
    public string InstallationPath { get; init; } = string.Empty;
    public string RunningSteamPath { get; init; } = string.Empty;
    public string ActiveAccountName { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
    public bool IsRunning { get; init; }
    public bool HasBlockingIssues => Items.Any(item => item.Severity == DiagnosticSeverity.Error);
    public int ErrorCount => Items.Count(item => item.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Items.Count(item => item.Severity == DiagnosticSeverity.Warning);
    public bool HasErrors => ErrorCount > 0;
    public bool HasWarnings => WarningCount > 0;
    public bool IsHealthy => !HasErrors && !HasWarnings;
    public string ErrorBadgeText => ErrorCount == 1 ? "1 problema" : $"{ErrorCount} problemas";
    public string WarningBadgeText => WarningCount == 1 ? "1 aviso" : $"{WarningCount} avisos";
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
