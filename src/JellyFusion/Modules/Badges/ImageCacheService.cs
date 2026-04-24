using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Badges;

/// <summary>
/// In-memory + disk cache for badge-composited images.
/// Prevents re-rendering on every request.
/// </summary>
public class ImageCacheService
{
    private readonly ILogger<ImageCacheService> _logger;
    private readonly string _cacheDir;

    private record CacheEntry(byte[] Data, DateTime ExpiresAt);
    private readonly ConcurrentDictionary<string, CacheEntry> _memCache = new();

    public ImageCacheService(
        MediaBrowser.Common.Configuration.IApplicationPaths appPaths,
        ILogger<ImageCacheService> logger)
    {
        _logger   = logger;
        _cacheDir = Path.Combine(appPaths.CachePath, "JellyFusion", "badges");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <param name="ttl">
    /// Must match the TTL passed to <see cref="Set"/> so that disk-cached files
    /// are expired with the same policy as in-memory entries.
    /// </param>
    public byte[]? Get(string key, TimeSpan ttl)
    {
        // Memory first
        if (_memCache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow) return entry.Data;
            _memCache.TryRemove(key, out _);
        }

        // Disk fallback — honour the caller-supplied TTL, not a hardcoded 24 h.
        var path = DiskPath(key);
        if (!File.Exists(path)) return null;

        var info = new FileInfo(path);
        if (info.LastWriteTimeUtc.Add(ttl) < DateTime.UtcNow)
        {
            File.Delete(path);
            return null;
        }

        var data = File.ReadAllBytes(path);
        _memCache[key] = new CacheEntry(data, DateTime.UtcNow.Add(ttl));
        return data;
    }

    public void Set(string key, byte[] data, TimeSpan ttl)
    {
        var expiry = DateTime.UtcNow.Add(ttl);
        _memCache[key] = new CacheEntry(data, expiry);

        try { File.WriteAllBytes(DiskPath(key), data); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed writing badge cache to disk"); }
    }

    public void ClearAll()
    {
        _memCache.Clear();
        try
        {
            foreach (var f in Directory.GetFiles(_cacheDir))
                File.Delete(f);
            _logger.LogInformation("JellyFusion badge cache cleared");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed clearing badge disk cache"); }
    }

    public (int Files, long Bytes, DateTime? Oldest) GetStats()
    {
        var files = Directory.GetFiles(_cacheDir);
        long bytes = 0;
        DateTime? oldest = null;
        foreach (var f in files)
        {
            var info = new FileInfo(f);
            bytes += info.Length;
            if (oldest is null || info.LastWriteTimeUtc < oldest)
                oldest = info.LastWriteTimeUtc;
        }
        return (files.Length, bytes, oldest);
    }

    private string DiskPath(string key)
    {
        // Sanitize key → safe filename
        var safe = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_cacheDir, safe + ".cache");
    }
}
