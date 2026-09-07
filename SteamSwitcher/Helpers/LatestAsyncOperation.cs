namespace SteamSwitcher.Helpers;

/// <summary>Each operation owns its cancellation source until completion.</summary>
public sealed class LatestAsyncOperation : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private bool _disposed;

    public void Start(Func<CancellationToken, Task> operation)
    {
        CancellationTokenSource source;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            source = new();
            token = source.Token;
            _current?.Cancel();
            _current = source;
        }
        _ = RunAsync(operation, source, token);
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationTokenSource source, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await operation(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LatestAsyncOperation] {ex}"); }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, source)) _current = null;
                source.Dispose();
            }
        }
    }

    public void Cancel()
    {
        lock (_gate) _current?.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _current?.Cancel();
        }
    }
}
