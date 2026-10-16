namespace SteamSwitcher.Core.Services;

public interface ISteamVdfService
{
    void WriteAtomically(string path, byte[] content, bool createBackup = true);
    Task WaitForReleaseAsync(string path, TimeSpan? timeout = null, CancellationToken ct = default);
}
