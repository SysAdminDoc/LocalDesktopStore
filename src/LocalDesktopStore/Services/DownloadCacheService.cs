using System.IO;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

/// <summary>
/// Stores verified whole-asset downloads below the existing downloads directory.
/// A sidecar hash is part of every cache key, so a new release never reuses an
/// unrelated asset with the same repository/version labels.
/// </summary>
public sealed class DownloadCacheService
{
    private readonly string _cacheRoot;

    public DownloadCacheService(string downloadsRoot)
    {
        _cacheRoot = Path.Combine(downloadsRoot, "cache");
    }

    public async Task<bool> TryRestoreAsync(
        AppInfo info,
        string expectedHash,
        string destination,
        IProgress<long>? progress = null,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        var cachePath = CachePathFor(info, expectedHash);
        if (!File.Exists(cachePath)) return false;

        try
        {
            var actualHash = await HashVerifier.ComputeSha256Async(cachePath, ct);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                log?.Report("  ~ cached download failed its SHA-256 check; downloading a fresh copy.");
                TryDelete(cachePath);
                return false;
            }

            await CopyFileAsync(cachePath, destination, progress, ct);
            log?.Report($"  ✓ cache hit: verified {Path.GetFileName(info.AssetName)}");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ cached download unavailable ({ex.Message}); downloading a fresh copy.");
            return false;
        }
    }

    public async Task StoreVerifiedAsync(
        AppInfo info,
        string expectedHash,
        string source,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        var cachePath = CachePathFor(info, expectedHash);
        var partialPath = cachePath + $".{Guid.NewGuid():N}.partial";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await CopyFileAsync(source, partialPath, progress: null, ct);
            File.Move(partialPath, cachePath, overwrite: true);
            log?.Report("  cached verified download for future installs.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(partialPath);
            throw;
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ could not save the download cache ({ex.Message}); the install remains complete.");
            TryDelete(partialPath);
        }
    }

    public string CachePathFor(AppInfo info, string expectedHash)
    {
        var owner = SafeSegment(info.RepoOwner);
        var repo = SafeSegment(info.RepoName);
        var version = SafeSegment(info.DisplayVersion);
        var asset = SafeSegment(Path.GetFileName(info.AssetName ?? "asset"));
        var hash = expectedHash.ToLowerInvariant();
        return Path.Combine(_cacheRoot, owner, repo, version, hash, asset);
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;
            progress?.Report(copied);
        }
    }

    private static string SafeSegment(string value)
    {
        var chars = value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '_').ToArray();
        var result = new string(chars).Trim('.', ' ');
        return string.IsNullOrEmpty(result) ? "_" : result.Length <= 120 ? result : result[..120];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* cache cleanup is best effort */ }
    }
}
