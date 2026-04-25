using System.Globalization;

namespace LocalDesktopStore.Services;

/// <summary>
/// Best-effort semver-ish comparison for GitHub release tags. Strips a leading
/// "v"/"V", splits the prerelease tail, parses the dotted core as System.Version
/// (or as a left-padded numeric tuple to handle date-driven 2026.04.25 tags),
/// and compares per semver 2.0 ordering. Falls back to case-insensitive ordinal
/// equality when either side won't parse — non-equal strings are treated as "different",
/// which is enough to surface an update prompt without claiming false directionality.
/// </summary>
public static class VersionCompare
{
    public enum Result
    {
        Equal,
        RemoteNewer,
        RemoteOlder,
        IndeterminateButDifferent
    }

    public static Result Compare(string? installed, string? remote)
    {
        var i = (installed ?? string.Empty).Trim();
        var r = (remote ?? string.Empty).Trim();

        if (string.Equals(i, r, StringComparison.OrdinalIgnoreCase)) return Result.Equal;
        if (string.IsNullOrEmpty(i) || string.IsNullOrEmpty(r)) return Result.IndeterminateButDifferent;

        if (TryParse(i, out var ip) && TryParse(r, out var rp))
        {
            var coreCompare = CompareCore(ip.Core, rp.Core);
            if (coreCompare != 0) return coreCompare > 0 ? Result.RemoteOlder : Result.RemoteNewer;

            // Core matched — compare prerelease (no prerelease > prerelease).
            var preCompare = ComparePrerelease(ip.Prerelease, rp.Prerelease);
            if (preCompare == 0) return Result.Equal;
            return preCompare > 0 ? Result.RemoteOlder : Result.RemoteNewer;
        }

        return Result.IndeterminateButDifferent;
    }

    public static bool IsRemoteNewer(string? installed, string? remote)
    {
        var c = Compare(installed, remote);
        return c == Result.RemoteNewer || c == Result.IndeterminateButDifferent;
    }

    private readonly record struct Parsed(int[] Core, string? Prerelease);

    private static bool TryParse(string raw, out Parsed parsed)
    {
        parsed = default;
        var s = raw;
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];

        // Strip build metadata "+sha".
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string? prerelease = null;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = s[(dash + 1)..];
            s = s[..dash];
        }

        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var core = new int[parts.Length];
        for (var idx = 0; idx < parts.Length; idx++)
        {
            if (!int.TryParse(parts[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out core[idx]))
                return false;
        }

        parsed = new Parsed(core, prerelease);
        return true;
    }

    private static int CompareCore(int[] a, int[] b)
    {
        var len = Math.Max(a.Length, b.Length);
        for (var idx = 0; idx < len; idx++)
        {
            var av = idx < a.Length ? a[idx] : 0;
            var bv = idx < b.Length ? b[idx] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    private static int ComparePrerelease(string? a, string? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return 1;   // no prerelease > prerelease
        if (b is null) return -1;
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
