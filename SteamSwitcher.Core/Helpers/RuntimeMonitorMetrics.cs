namespace SteamSwitcher.Core.Helpers;

public static class RuntimePollingPolicy
{
    // Visible UI, uncertain processes and pending actions retain the original cadence.
    public static int DelayMilliseconds(bool visible, bool running, bool pendingAction, int idleCycles)
        => visible || running || pendingAction || idleCycles < 3 ? 2000 : 5000;
}

public sealed class RuntimeMonitorMetrics
{
    public static RuntimeMonitorMetrics Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Queue<double> _samples = new();
    private long _total;
    private int _failures;
    private int _delay = 2000;

    public void Record(double milliseconds, bool success)
    {
        lock (_gate)
        {
            _total++;
            if (!success) _failures++;
            _samples.Enqueue(milliseconds);
            while (_samples.Count > 300) _samples.Dequeue();
        }
    }

    public void SetDelay(int milliseconds) { lock (_gate) _delay = milliseconds; }

    public string Summary
    {
        get
        {
            lock (_gate)
            {
                var samples = _samples.Order().ToArray();
                var p95 = samples.Length == 0 ? 0 : samples[(int)Math.Ceiling(samples.Length * .95) - 1];
                var average = samples.Length == 0 ? 0 : samples.Average();
                return $"Monitoramento nesta sessão: {_total} leituras · {_failures} falhas\n"
                    + $"Últimas {samples.Length}: média {average:F1} ms · P95 {p95:F1} ms\n"
                    + $"Pausa entre leituras: {_delay / 1000d:F0} s (além do tempo da leitura).";
            }
        }
    }
}
