using System.IO;
using System.IO.Compression;
using System.Text;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed record WingetManifestExportResult(string ManifestPath, string InstallerSha256);

/// <summary>
/// Writes a v1.6 singleton WinGet manifest for one catalog card. The installer is
/// hashed locally before the manifest is written so the exported hash is usable by
/// winget validate rather than merely copied from unverified metadata.
/// </summary>
public sealed class WingetManifestExporter
{
    public const string ManifestVersion = "1.6.0";

    private readonly SettingsService _settings;
    private readonly GitHubService _github;

    public string ExportRoot { get; }

    public WingetManifestExporter(SettingsService settings, GitHubService github, string? exportRoot = null)
    {
        _settings = settings;
        _github = github;
        ExportRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(exportRoot)
            ? GetDefaultExportRoot()
            : exportRoot);
    }

    public async Task<WingetManifestExportResult> ExportAsync(
        AppInfo info,
        IProgress<string>? log = null,
        IProgress<long>? bytes = null,
        CancellationToken ct = default)
    {
        ValidatePackageIdentity(info);
        ValidateInstaller(info);
        var cachePath = GetCachePath(info);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        if (!File.Exists(cachePath) || (info.AssetSizeBytes > 0 && new FileInfo(cachePath).Length != info.AssetSizeBytes))
        {
            var partialPath = cachePath + ".partial";
            try
            {
                log?.Report($"Downloading {info.AssetName} to calculate the WinGet installer hash...");
                await _github.DownloadAssetToFileAsync(info.AssetUrl!, partialPath, bytes, ct);
                File.Move(partialPath, cachePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(partialPath))
                    File.Delete(partialPath);
            }
        }
        else
        {
            log?.Report("Using the cached release asset to calculate the WinGet installer hash.");
        }

        var kind = info.Kind is ArtifactKind.Msi or ArtifactKind.PortableZip
            ? info.Kind
            : AssetClassifier.RefineFromFile(cachePath, info.Kind);
        var sha256 = await HashVerifier.ComputeSha256Async(cachePath, ct);
        if (!string.IsNullOrWhiteSpace(info.Sha256Url))
        {
            var sidecar = await _github.TryDownloadTextAsync(info.Sha256Url!, ct);
            var expected = HashVerifier.ParseSidecar(sidecar);
            if (expected is null)
                throw new InvalidOperationException("The release hash sidecar contains no SHA-256 value; refusing to export a manifest.");
            if (!expected.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The release hash sidecar does not match the downloaded asset (expected {expected}, actual {sha256}).");
        }

        var nestedLauncher = kind == ArtifactKind.PortableZip
            ? FindPortableLauncher(cachePath)
            : null;
        if (kind == ArtifactKind.PortableZip && nestedLauncher is null)
            throw new InvalidOperationException("The portable archive contains no launchable .exe for a WinGet nested portable installer.");

        var yaml = BuildYaml(info, kind, sha256, nestedLauncher);
        var manifestPath = GetManifestPath(ExportRoot, info);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        log?.Report($"WinGet manifest written to {manifestPath}");
        return new WingetManifestExportResult(manifestPath, sha256);
    }

    public static string GetDefaultExportRoot()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(desktop, "manifests");
    }

    public static string GetManifestPath(string exportRoot, AppInfo info)
    {
        ValidatePackageIdentity(info);
        var firstLetter = char.ToLowerInvariant(info.RepoOwner[0]).ToString();
        var version = SafePathSegment(info.DisplayVersion);
        var fileName = $"{info.RepoOwner}.{info.RepoName}.yaml";
        return Path.Combine(exportRoot, firstLetter, info.RepoOwner, info.RepoName, version, fileName);
    }

    public static string BuildYaml(
        AppInfo info,
        ArtifactKind kind,
        string sha256,
        string? nestedLauncher = null)
    {
        ValidatePackageIdentity(info);
        ValidateInstaller(info);
        var hash = NormalizeSha256(sha256);

        var installerType = kind switch
        {
            ArtifactKind.Msi => "msi",
            ArtifactKind.Inno => "inno",
            ArtifactKind.Nsis => "nullsoft",
            ArtifactKind.GenericExe => "exe",
            ArtifactKind.PortableZip => "zip",
            _ => throw new InvalidOperationException($"Unsupported artifact kind for WinGet export: {kind}")
        };

        var modes = kind == ArtifactKind.GenericExe
            ? new[] { "interactive" }
            : kind == ArtifactKind.PortableZip
                ? new[] { "silent" }
                : new[] { "silent", "silentWithProgress", "interactive" };
        var silentSwitch = kind switch
        {
            ArtifactKind.Msi => "/qb /norestart",
            ArtifactKind.Inno => "/SILENT /NORESTART",
            ArtifactKind.Nsis => "/S",
            _ => null
        };

        var packageVersion = info.DisplayVersion;
        var releaseTag = info.LatestVersion ?? packageVersion;
        var description = NormalizeDescription(info.DisplayDescription);
        var packageIdentifier = $"{info.RepoOwner}.{info.RepoName}";
        var repoUrl = info.RepoUrl;
        var sb = new StringBuilder();
        sb.AppendLine("# yaml-language-server: $schema=https://aka.ms/winget-manifest.singleton.1.6.0.schema.json");
        AppendScalar(sb, "PackageIdentifier", packageIdentifier);
        AppendScalar(sb, "PackageVersion", packageVersion);
        AppendScalar(sb, "PackageLocale", "en-US");
        AppendScalar(sb, "Publisher", info.RepoOwner);
        AppendScalar(sb, "PublisherUrl", $"https://github.com/{info.RepoOwner}");
        AppendScalar(sb, "PackageName", info.DisplayName);
        AppendScalar(sb, "PackageUrl", repoUrl);
        AppendScalar(sb, "License", "MIT");
        AppendScalar(sb, "ShortDescription", description);
        AppendScalar(sb, "ReleaseNotesUrl", $"https://github.com/{info.RepoOwner}/{info.RepoName}/releases/tag/{Uri.EscapeDataString(releaseTag)}");
        sb.AppendLine("Installers:");
        sb.AppendLine("  - Architecture: neutral");
        AppendScalar(sb, "InstallerType", installerType, "    ");
        AppendScalar(sb, "InstallerUrl", info.AssetUrl!, "    ");
        AppendScalar(sb, "InstallerSha256", hash, "    ");
        sb.AppendLine("    InstallModes:");
        foreach (var mode in modes)
            sb.AppendLine($"      - {YamlQuote(mode)}");

        if (silentSwitch is not null)
        {
            sb.AppendLine("    InstallerSwitches:");
            AppendScalar(sb, "Silent", silentSwitch, "      ");
            AppendScalar(sb, "SilentWithProgress", silentSwitch, "      ");
        }

        if (kind == ArtifactKind.PortableZip)
        {
            AppendScalar(sb, "NestedInstallerType", "portable", "    ");
            sb.AppendLine("    NestedInstallerFiles:");
            AppendScalar(sb, "RelativeFilePath", nestedLauncher!.Replace('\\', '/'), "      - ");
        }

        AppendScalar(sb, "ManifestType", "singleton");
        sb.AppendLine($"ManifestVersion: {ManifestVersion}");
        return sb.ToString();
    }

    private static string? FindPortableLauncher(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries
            .Where(e => !e.FullName.EndsWith("/", StringComparison.Ordinal)
                && !e.FullName.EndsWith("\\", StringComparison.Ordinal)
                && e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(e => !Path.GetFileName(e.FullName).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            .Where(e => !Path.GetFileName(e.FullName).Equals("vc_redist.exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Length)
            .Select(e => e.FullName.Replace('\\', '/'))
            .FirstOrDefault();
    }

    private string GetCachePath(AppInfo info)
    {
        var version = SafePathSegment(info.DisplayVersion);
        var owner = SafePathSegment(info.RepoOwner);
        var repo = SafePathSegment(info.RepoName);
        var asset = SafePathSegment(Path.GetFileName(info.AssetName ?? string.Empty));
        return Path.Combine(_settings.DownloadsDir, "winget", owner, repo, version, asset);
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("WinGet export requires a 64-character SHA-256 hash.");
        return normalized.ToLowerInvariant();
    }

    private static void ValidatePackageIdentity(AppInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.RepoOwner) || string.IsNullOrWhiteSpace(info.RepoName))
            throw new InvalidOperationException("WinGet export requires a repository owner and name.");
        var identifier = $"{info.RepoOwner}.{info.RepoName}";
        var segments = identifier.Split('.');
        if (identifier.Length > 128 || segments.Length is < 2 or > 8
            || segments.Any(segment => segment.Length is < 1 or > 32
                || segment.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "\\/:*?\"<>|".Contains(c))))
            throw new InvalidOperationException($"'{identifier}' is not a valid v1.6 WinGet package identifier.");
        if (info.DisplayName.Length < 2)
            throw new InvalidOperationException("WinGet requires a package name with at least two characters.");
        if (string.IsNullOrWhiteSpace(info.DisplayVersion) || info.DisplayVersion == "—")
            throw new InvalidOperationException("WinGet export requires a release version.");
    }

    private static void ValidateInstaller(AppInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.AssetName))
            throw new InvalidOperationException("WinGet export requires a release asset name.");
        if (string.IsNullOrWhiteSpace(info.AssetUrl)
            || !Uri.TryCreate(info.AssetUrl, UriKind.Absolute, out var assetUri)
            || (assetUri.Scheme != Uri.UriSchemeHttp && assetUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("WinGet requires an HTTP(S) installer URL.");
        if (string.IsNullOrWhiteSpace(info.RepoUrl)
            || !Uri.TryCreate(info.RepoUrl, UriKind.Absolute, out var repoUri)
            || (repoUri.Scheme != Uri.UriSchemeHttp && repoUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("WinGet requires an HTTP(S) package URL.");
    }

    private static string NormalizeDescription(string value)
    {
        var oneLine = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (oneLine.Length <= 256) return oneLine;
        return oneLine[..253] + "...";
    }

    private static string SafePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("A required WinGet path segment is blank.");
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        safe = safe.Trim().TrimEnd('.');
        if (safe.Length == 0)
            throw new InvalidOperationException("A required WinGet path segment contains no usable characters.");
        return safe;
    }

    private static void AppendScalar(StringBuilder sb, string key, string value, string indent = "")
        => sb.AppendLine($"{indent}{key}: {YamlQuote(value)}");

    private static string YamlQuote(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
