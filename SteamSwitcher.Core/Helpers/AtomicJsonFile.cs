using System.Collections.Concurrent;
using System.Text.Json;

namespace SteamSwitcher.Core.Helpers;

public static class AtomicJsonFile
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static async Task<T> ReadAsync<T>(
        string path, Func<T> createDefault, CancellationToken ct = default)
    {
        var gate = GetGate(path);
        await gate.WaitAsync(ct);
        try { return await ReadUnlockedAsync(path, createDefault, ct); }
        finally { gate.Release(); }
    }

    public static async Task UpdateAsync<T>(
        string path,
        Func<T> createDefault,
        Action<T> update,
        CancellationToken ct = default)
    {
        var gate = GetGate(path);
        await gate.WaitAsync(ct);
        try
        {
            var value = await ReadUnlockedAsync(path, createDefault, ct);
            update(value);
            await WriteUnlockedAsync(path, value, ct);
        }
        finally { gate.Release(); }
    }

    private static SemaphoreSlim GetGate(string path) =>
        _gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    private static async Task<T> ReadUnlockedAsync<T>(
        string path, Func<T> createDefault, CancellationToken ct)
    {
        if (!File.Exists(path)) return createDefault();

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<T>(stream, _options, ct)
                ?? createDefault();
        }
        catch (JsonException) { return createDefault(); }
        catch (IOException) { return createDefault(); }
    }

    private static async Task WriteUnlockedAsync<T>(
        string path, T value, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
