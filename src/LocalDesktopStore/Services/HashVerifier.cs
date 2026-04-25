using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LocalDesktopStore.Services;

public sealed record HashVerificationResult(bool Verified, string? ExpectedHash, string? ActualHash, string Detail);

public static class HashVerifier
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Pull the hex hash out of a PowerShell-style sidecar (just hex), or shasum-style
    /// ("<hex>  <filename>"). Both formats are accepted.
    /// </summary>
    public static string? ParseSidecar(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Regex.Match(text, "[0-9a-fA-F]{64}");
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    public static async Task<HashVerificationResult> VerifyAsync(string filePath, string sidecarText, CancellationToken ct = default)
    {
        var expected = ParseSidecar(sidecarText);
        if (expected is null)
            return new(false, null, null, "Sidecar present but no SHA-256 hash could be parsed.");
        var actual = await ComputeSha256Async(filePath, ct);
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            return new(true, expected, actual, "Hash matches sidecar.");
        return new(false, expected, actual, "Hash mismatch — refusing to install.");
    }
}
