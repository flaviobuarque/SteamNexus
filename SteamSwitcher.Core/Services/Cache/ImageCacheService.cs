using Microsoft.Extensions.Logging;

namespace SteamSwitcher.Core.Services;

public class ImageCacheService(ILogger<ImageCacheService> logger) : IImageCacheService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "cache");
    private readonly string _metaDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "meta");

    public string GetCacheDirectory() => _cacheDir;

    public async Task<string?> GetCachedPathAsync(string url, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_cacheDir);

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(url)))[..16];

        var ext = Path.GetExtension(url).Split('?')[0];
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

        var localPath = Path.Combine(_cacheDir, hash + ext);

        // Arquivo existe e tem tamanho válido (> 1KB descarta páginas de erro)
        if (File.Exists(localPath) && new FileInfo(localPath).Length > 1024)
            return localPath;

        // Apaga arquivo inválido de tentativas anteriores
        if (File.Exists(localPath))
            File.Delete(localPath);

        try
        {
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (bytes.Length < 1024)
                return null;

            await File.WriteAllBytesAsync(localPath, bytes, ct);
            return localPath;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Erro ao baixar imagem {Url}", url);
            return null;
        }
    }

    public async Task<string?> GetStringAsync(string key)
    {
        Directory.CreateDirectory(_metaDir);
        var path = Path.Combine(_metaDir, SanitizeKey(key) + ".txt");

        if (!File.Exists(path)) return null;

        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length < 2) return null;

        // Linha 0: expiry (ticks), Linha 1: valor
        if (!long.TryParse(lines[0], out var expiryTicks)) return null;
        if (DateTime.UtcNow.Ticks > expiryTicks) return null;

        return lines[1];
    }

    public async Task SetStringAsync(string key, string value, TimeSpan expiry)
    {
        Directory.CreateDirectory(_metaDir);
        var path = Path.Combine(_metaDir, SanitizeKey(key) + ".txt");
        var expiryTicks = (DateTime.UtcNow + expiry).Ticks;
        await File.WriteAllLinesAsync(path, [expiryTicks.ToString(), value]);
    }

    private static string SanitizeKey(string key) =>
        string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public Task ClearCacheAsync() => Task.Run(() =>
    {
        foreach (var dir in new[] { _cacheDir, _metaDir })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
                Directory.CreateDirectory(dir);
            }
        }
    });

    public Task<long> GetCacheSizeAsync() => Task.Run(() =>
    {
        long total = 0;
        foreach (var dir in new[] { _cacheDir, _metaDir })
        {
            if (!Directory.Exists(dir)) continue;
            total += new DirectoryInfo(dir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        return total;
    });
}