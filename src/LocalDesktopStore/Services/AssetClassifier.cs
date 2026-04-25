using System.Diagnostics;
using System.IO;
using System.Text;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

/// <summary>
/// Classifies a release asset by file name and (when on disk) by signature/PE inspection.
/// MSI is dead-simple: extension. EXE installers split into Inno / NSIS / Generic — name
/// hints come first, then optional content scan when the file has been downloaded.
/// </summary>
public static class AssetClassifier
{
    public static ArtifactKind ClassifyByName(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName)) return ArtifactKind.Unknown;
        var n = assetName.ToLowerInvariant();

        if (n.EndsWith(".msi")) return ArtifactKind.Msi;

        if (n.EndsWith(".zip"))
        {
            // ZIPs that look like userland source dumps shouldn't be treated as portable apps —
            // but at the asset-classifier level we can't tell. The download caller decides whether
            // to trust the ZIP based on whether it contains a usable .exe.
            return ArtifactKind.PortableZip;
        }

        if (n.EndsWith(".exe"))
        {
            // Filename hints — fast path.
            if (n.Contains("innosetup") || n.Contains("inno-setup")) return ArtifactKind.Inno;
            if (n.Contains("nsis")) return ArtifactKind.Nsis;
            // Setup / installer flag — kind unresolved until we scan the bytes.
            if (n.Contains("setup") || n.Contains("installer") || n.EndsWith("-setup.exe") || n.EndsWith("-installer.exe"))
                return ArtifactKind.GenericExe;
            // A bare .exe is treated as a portable launcher; we'll wrap it like a single-file app.
            return ArtifactKind.GenericExe;
        }

        return ArtifactKind.Unknown;
    }

    /// <summary>
    /// Given the file on disk, refine GenericExe → Inno or Nsis when possible.
    /// Inno Setup binaries always contain the literal string "Inno Setup Setup Data".
    /// NSIS binaries contain "Nullsoft.NSIS" or "Nullsoft Install System" near the resources.
    /// We scan a bounded prefix; both signatures sit early in practice and we don't need
    /// full PE parsing to make a routing decision.
    /// </summary>
    public static ArtifactKind RefineFromFile(string path, ArtifactKind hint)
    {
        if (hint == ArtifactKind.Msi || hint == ArtifactKind.PortableZip) return hint;
        try
        {
            // Prefer FileVersionInfo-based hints — fast and accurate when populated.
            var fvi = FileVersionInfo.GetVersionInfo(path);
            var meta = string.Join(" | ",
                new[] { fvi.CompanyName, fvi.ProductName, fvi.FileDescription, fvi.OriginalFilename, fvi.Comments }
                    .Where(s => !string.IsNullOrEmpty(s)));
            var metaLower = meta.ToLowerInvariant();
            if (metaLower.Contains("inno setup")) return ArtifactKind.Inno;
            if (metaLower.Contains("nullsoft") || metaLower.Contains("nsis")) return ArtifactKind.Nsis;

            // Scan up to 4 MB of bytes for marker strings — Inno + NSIS both leave them near the head.
            const int maxScan = 4 * 1024 * 1024;
            using var fs = File.OpenRead(path);
            int len = (int)Math.Min(fs.Length, maxScan);
            var buf = new byte[len];
            int read = fs.Read(buf, 0, len);
            // Compare against ASCII byte sequences (case-sensitive) — these are stable markers.
            if (Contains(buf, read, "Inno Setup Setup Data")) return ArtifactKind.Inno;
            if (Contains(buf, read, "Nullsoft Install System")) return ArtifactKind.Nsis;
            if (Contains(buf, read, "Nullsoft.NSIS")) return ArtifactKind.Nsis;
        }
        catch { /* fall through and return hint */ }

        return hint == ArtifactKind.Unknown ? ArtifactKind.GenericExe : hint;
    }

    private static bool Contains(byte[] haystack, int len, string needle)
    {
        var nb = Encoding.ASCII.GetBytes(needle);
        if (nb.Length == 0 || nb.Length > len) return false;
        int last = len - nb.Length;
        for (int i = 0; i <= last; i++)
        {
            int j = 0;
            while (j < nb.Length && haystack[i + j] == nb[j]) j++;
            if (j == nb.Length) return true;
        }
        return false;
    }
}
