using System.Collections.Concurrent;
using System.Text.Json;

namespace SteamSwitcher.Core.Helpers;

/// <summary>Serialized atomic writes, retaining the last parseable version as a backup.</summary>
public static class RecoverableJsonFile
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<T?> ReadAsync<T>(string path, JsonSerializerOptions options, Action? onBackupRecovered = null) where T : class
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            var primary = await ReadValidAsync<T>(path, options);
            if (primary is not null) return primary;
            var backup = await ReadValidAsync<T>(path + ".bak", options);
            if (backup is not null) onBackupRecovered?.Invoke();
            return backup;
        }
        finally { gate.Release(); }
    }

    private static async Task<T?> ReadValidAsync<T>(string path, JsonSerializerOptions options) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), options); }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static Task WriteAsync<T>(string path, T value, JsonSerializerOptions options) where T : class
    {
        // Capture before awaiting: callers often mutate the shared settings instance.
        var json = JsonSerializer.Serialize(value, options);
        return WriteSnapshotAsync<T>(path, json, options);
    }

    private static async Task WriteSnapshotAsync<T>(string path, string json, JsonSerializerOptions options) where T : class
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));
        await gate.WaitAsync();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(temp, json);
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.Flush(flushToDisk: true);
            if (File.Exists(path))
            {
                // Never replace a good backup with a corrupt primary file.
                var backup = await ReadValidAsync<T>(path, options) is not null ? path + ".bak" : null;
                File.Replace(temp, path, backup);
            }
            else File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            finally { gate.Release(); }
        }
    }
}
