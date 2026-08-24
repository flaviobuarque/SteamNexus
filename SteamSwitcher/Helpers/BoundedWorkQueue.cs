namespace SteamSwitcher.Helpers;

public static class BoundedWorkQueue
{
    public static async Task RunAsync<T>(
        IReadOnlyList<T> items,
        int workerCount,
        Func<T, CancellationToken, Task> processAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(processAsync);
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);

        var nextIndex = -1;

        async Task WorkerAsync()
        {
            while (!ct.IsCancellationRequested)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= items.Count) return;
                await processAsync(items[index], ct);
            }
        }

        var workers = new Task[Math.Min(workerCount, Math.Max(1, items.Count))];
        for (var i = 0; i < workers.Length; i++)
            workers[i] = WorkerAsync();

        await Task.WhenAll(workers);
    }
}
