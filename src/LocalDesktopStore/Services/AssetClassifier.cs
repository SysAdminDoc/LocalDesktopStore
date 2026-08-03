using System.Diagnostics;
using System.IO;
using System.Text;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

/// <summary>
/// Classifies a release asset by file name and (when on disk) by signature/PE inspection.
/// MSI and MSIX are dead-simple: extension. EXE installers split into Inno / NSIS / Generic —
/// name hints come first, then optional content scan when the file has been downloaded.
/// </summary>
public static class AssetClassifier
{
    private static readonly ArtifactHandlerRegistry Handlers = ArtifactHandlerRegistry.CreateBundled();

    public static ArtifactKind ClassifyByName(string assetName)
        => string.IsNullOrWhiteSpace(assetName) ? ArtifactKind.Unknown : Handlers.ClassifyByName(assetName);

    /// <summary>
    /// Given the file on disk, refine GenericExe → Inno or Nsis when possible.
    /// Inno Setup binaries always contain the literal string "Inno Setup Setup Data".
    /// NSIS binaries contain "Nullsoft.NSIS" or "Nullsoft Install System" near the resources.
    /// We scan a bounded prefix; both signatures sit early in practice and we don't need
    /// full PE parsing to make a routing decision.
    /// </summary>
    public static ArtifactKind RefineFromFile(string path, ArtifactKind hint)
    {
        if (hint is ArtifactKind.Msi or ArtifactKind.PortableZip or ArtifactKind.Msix or ArtifactKind.AppInstaller
            or ArtifactKind.Velopack or ArtifactKind.AppImage)
            return hint;
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
