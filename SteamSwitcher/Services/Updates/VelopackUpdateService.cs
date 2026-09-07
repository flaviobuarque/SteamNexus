using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace SteamSwitcher.Services.Updates;

public partial class VelopackUpdateService : ObservableObject, IUpdateService
{
    private readonly UpdateManager? _manager;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private UpdateInfo? _pendingUpdate;
    private static readonly HttpClient ReleaseNotesClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string _notesVersion = string.Empty;

    [ObservableProperty] private string _availableVersion = string.Empty;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _downloadSpeedText = string.Empty;
    [ObservableProperty] private int _downloadProgress;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isUpdateReady;
    [ObservableProperty] private string _releaseNotes = "As novidades desta versão ainda não foram publicadas.";

    public VelopackUpdateService()
    {
        var updateUrl = ReadAssemblyMetadata("SteamNexusUpdateUrl");
        IsConfigured = Uri.TryCreate(updateUrl, UriKind.Absolute, out _);

        if (IsConfigured)
        {
            _manager = new UpdateManager(new GithubSource(
                updateUrl,
                accessToken: null,
                prerelease: false));
            IsInstalled = _manager.IsInstalled;
            CurrentVersion = _manager.CurrentVersion?.ToString()
                ?? GetAssemblyVersion();

            var pending = _manager.UpdatePendingRestart;
            if (pending is not null)
            {
                AvailableVersion = pending.Version.ToString();
                IsUpdateAvailable = true;
                IsUpdateReady = true;
                StatusText = $"Versão {AvailableVersion} pronta para instalar";
            }
            else
            {
                StatusText = IsInstalled
                    ? "Atualizações automáticas ativadas"
                    : "Instale o app pelo Setup para ativar atualizações";
            }
        }
        else
        {
            CurrentVersion = GetAssemblyVersion();
            StatusText = "Servidor de atualizações ainda não configurado";
        }
    }

    public string CurrentVersion { get; }
    public string UpdateActionText => IsUpdateReady
        ? $"Instalar {AvailableVersion}"
        : $"Atualizar para {AvailableVersion}";
    public bool IsConfigured { get; }
    public bool IsInstalled { get; }
    public bool CanCheckForUpdates =>
        IsConfigured && IsInstalled && !IsChecking && !IsDownloading;

    partial void OnIsCheckingChanged(bool value) =>
        OnPropertyChanged(nameof(CanCheckForUpdates));

    partial void OnIsDownloadingChanged(bool value) =>
        OnPropertyChanged(nameof(CanCheckForUpdates));

    partial void OnAvailableVersionChanged(string value) =>
        OnPropertyChanged(nameof(UpdateActionText));

    partial void OnIsUpdateReadyChanged(bool value) =>
        OnPropertyChanged(nameof(UpdateActionText));

    public async Task CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (_manager is null || !IsInstalled)
            return;

        await _operationGate.WaitAsync(ct);
        try
        {
            IsChecking = true;
            ErrorText = string.Empty;
            StatusText = "Verificando atualizações...";

            _pendingUpdate = await _manager.CheckForUpdatesAsync();
            if (_pendingUpdate is null)
            {
                AvailableVersion = string.Empty;
                IsUpdateAvailable = false;
                IsUpdateReady = false;
                StatusText = "Você está usando a versão mais recente";
                return;
            }

            AvailableVersion = _pendingUpdate.TargetFullRelease.Version.ToString();
            IsUpdateAvailable = true;
            IsUpdateReady = false;
            DownloadProgress = 0;
            StatusText = $"Versão {AvailableVersion} disponível";
            await LoadReleaseNotesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = "Verificação cancelada";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = "Não foi possível verificar atualizações";
        }
        finally
        {
            IsChecking = false;
            _operationGate.Release();
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (_manager is null || _pendingUpdate is null || IsUpdateReady)
            return;

        await _operationGate.WaitAsync(ct);
        try
        {
            IsDownloading = true;
            ErrorText = string.Empty;
            DownloadProgress = 0;
            DownloadSpeedText = "Calculando velocidade...";
            StatusText = $"Baixando versão {AvailableVersion}...";

            var expectedBytes = GetExpectedDownloadSize(_pendingUpdate);
            var stopwatch = Stopwatch.StartNew();
            var lastBytes = 0d;
            var lastElapsed = TimeSpan.Zero;
            var smoothedBytesPerSecond = 0d;

            await _manager.DownloadUpdatesAsync(
                _pendingUpdate,
                progress =>
                {
                    DownloadProgress = progress;

                    var elapsed = stopwatch.Elapsed;
                    var estimatedBytes = expectedBytes * progress / 100d;
                    var intervalSeconds = (elapsed - lastElapsed).TotalSeconds;
                    if (intervalSeconds < 0.25 || estimatedBytes <= lastBytes)
                        return;

                    var instantaneous = (estimatedBytes - lastBytes) / intervalSeconds;
                    smoothedBytesPerSecond = smoothedBytesPerSecond <= 0
                        ? instantaneous
                        : (smoothedBytesPerSecond * 0.72) + (instantaneous * 0.28);

                    DownloadSpeedText = FormatSpeed(smoothedBytesPerSecond);
                    lastBytes = estimatedBytes;
                    lastElapsed = elapsed;
                },
                ct);

            DownloadProgress = 100;
            DownloadSpeedText = "Download concluído";
            IsUpdateReady = true;
            StatusText = $"Versão {AvailableVersion} pronta para instalar";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = "Download cancelado";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = "Não foi possível baixar a atualização";
        }
        finally
        {
            IsDownloading = false;
            _operationGate.Release();
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (_manager is null || !IsUpdateReady)
            return;

        var target = _pendingUpdate?.TargetFullRelease
            ?? _manager.UpdatePendingRestart;
        if (target is not null)
            _manager.ApplyUpdatesAndRestart(target, []);
    }

    private static long GetExpectedDownloadSize(UpdateInfo update)
    {
        var fullSize = Math.Max(1, update.TargetFullRelease.Size);
        var deltaSize = update.DeltasToTarget?.Sum(delta => delta.Size) ?? 0;
        return deltaSize > 0 && deltaSize < fullSize ? deltaSize : fullSize;
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    private static string ReadAssemblyMetadata(string key) =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)
            ?.Value
        ?? string.Empty;

    private static string GetAssemblyVersion() =>
        Assembly.GetExecutingAssembly()
            .GetName()
            .Version?
            .ToString(3)
        ?? "0.0.0";

    private async Task<string> GetReleaseNotesAsync(string version, CancellationToken ct)
    {
        if (!Uri.TryCreate(ReadAssemblyMetadata("SteamNexusUpdateUrl"), UriKind.Absolute, out var repository))
            return "As novidades desta versão ainda não foram publicadas.";

        var parts = repository.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!repository.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || parts.Length < 2)
            return "As novidades desta versão ainda não foram publicadas.";

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/tags/v{version}");
            request.Headers.UserAgent.ParseAdd("SteamNexus-Updater");
            using var response = await ReleaseNotesClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return "As novidades desta versão ainda não foram publicadas.";

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            return document.RootElement.TryGetProperty("body", out var body)
                && !string.IsNullOrWhiteSpace(body.GetString())
                ? body.GetString()!.Trim()
                : "As novidades desta versão ainda não foram publicadas.";
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return "Não foi possível carregar as novidades agora.";
        }
    }

    public async Task LoadReleaseNotesAsync(CancellationToken ct = default)
    {
        var version = AvailableVersion;
        if (string.IsNullOrWhiteSpace(version) || _notesVersion == version) return;
        var notes = await GetReleaseNotesAsync(version, ct);
        if (AvailableVersion != version) return;
        ReleaseNotes = notes;
        if (!notes.StartsWith("Não foi possível") && !notes.StartsWith("As novidades"))
            _notesVersion = version;
    }
}
