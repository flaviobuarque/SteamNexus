namespace SteamSwitcher.Core.Services;

public class SteamVdfService : ISteamVdfService
{
    public void WriteAtomically(string vdfPath, byte[] contents, bool createBackup = true)
    {
        var dir = Path.GetDirectoryName(vdfPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

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
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public async Task WaitForReleaseAsync(string path, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return;

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100, ct);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(100, ct);
            }
        }
    }
}
