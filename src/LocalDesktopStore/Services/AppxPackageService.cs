using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed record AppxPackageIdentity(string Name, string Publisher, string Version);

public sealed record AppxInstallResult(
    string IdentityName,
    string? PackageFullName,
    string? InstallLocation);

/// <summary>
/// Installs and removes MSIX packages through the built-in Windows Appx PowerShell
/// module. Every command runs as the current user; no elevation, certificate import,
/// or unsigned-package override is attempted.
/// </summary>
public sealed class AppxPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppxInstallResult> InstallPackageAsync(
        string packagePath,
        IProgress<string>? log,
        CancellationToken ct = default)
    {
        var identity = ReadIdentity(packagePath);
        log?.Report($"Installing {Path.GetFileName(packagePath)} for the current Windows user...");

        var result = await RunPowerShellAsync(
            $"$ErrorActionPreference = 'Stop'\nAdd-AppxPackage -Path {PowerShellLiteral(packagePath)}",
            ct);
        EnsureSuccess(result, "MSIX installation");

        AppxPackageLookup? installed = null;
        try
        {
            installed = await FindInstalledPackageAsync(identity.Name, ct);
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ package installed, but package metadata lookup failed: {ex.Message}");
        }

        return new AppxInstallResult(
            identity.Name,
            installed?.PackageFullName,
            installed?.InstallLocation);
    }

    public async Task UninstallAsync(
        InstalledApp app,
        IProgress<string>? log,
        CancellationToken ct = default)
    {
        var packageName = app.AppxPackageName;
        var packageFullName = app.AppxPackageFullName;
        if (string.IsNullOrWhiteSpace(packageName) && string.IsNullOrWhiteSpace(packageFullName))
            throw new InvalidOperationException("MSIX uninstall requires the recorded package identity.");

        log?.Report("Removing the current-user MSIX package...");
        string script;
        if (!string.IsNullOrWhiteSpace(packageFullName))
        {
            script = $"$ErrorActionPreference = 'Stop'\nRemove-AppxPackage -Package {PowerShellLiteral(packageFullName)}";
        }
        else
        {
            script = $$"""
                $ErrorActionPreference = 'Stop'
                $package = Get-AppxPackage -Name {{PowerShellLiteral(packageName!)}} |
                    Sort-Object Version -Descending |
                    Select-Object -First 1
                if ($null -ne $package) {
                    Remove-AppxPackage -Package $package.PackageFullName
                }
                """;
        }

        var result = await RunPowerShellAsync(script, ct);
        EnsureSuccess(result, "MSIX uninstall");
    }

    public static AppxPackageIdentity ReadIdentity(string packagePath)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("The MSIX package was not found.", packagePath);

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifest = archive.Entries.FirstOrDefault(entry =>
                string.Equals(Path.GetFileName(entry.FullName), "AppxBundleManifest.xml", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry.FullName), "AppxManifest.xml", StringComparison.OrdinalIgnoreCase));
            if (manifest is null)
                throw new InvalidOperationException("The MSIX archive does not contain AppxManifest.xml or AppxBundleManifest.xml.");

            using var stream = manifest.Open();
            var document = XDocument.Load(stream, LoadOptions.None);
            var identity = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName.Equals("Identity", StringComparison.OrdinalIgnoreCase));
            if (identity is null)
                throw new InvalidOperationException("The MSIX manifest does not contain an Identity element.");

            var name = identity.Attribute("Name")?.Value.Trim();
            var publisher = identity.Attribute("Publisher")?.Value.Trim();
            var version = identity.Attribute("Version")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher) || string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("The MSIX manifest Identity is missing Name, Publisher, or Version.");

            return new AppxPackageIdentity(name, publisher, version);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not read the MSIX package manifest: {ex.Message}", ex);
        }
    }

    public static string BuildAppInstallerUri(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
            || (source.Scheme != Uri.UriSchemeHttps && source.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("App Installer requires an HTTP(S) .appinstaller source URL.");
        }
        if (!source.AbsolutePath.EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("App Installer source URLs must end in .appinstaller.");

        return $"ms-appinstaller:?source={source.AbsoluteUri}";
    }

    public static void LaunchAppInstallerUri(string sourceUrl, IProgress<string>? log = null)
    {
        var appInstallerUri = BuildAppInstallerUri(sourceUrl);
        log?.Report("Opening the Windows App Installer for this release...");
        log?.Report("  ~ App Installer will validate the package certificate; LocalDesktopStore never imports certificates.");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = appInstallerUri,
            UseShellExecute = true
        });
        if (process is null)
            throw new InvalidOperationException("Windows could not open the App Installer URI.");
    }

    private static async Task<AppxPackageLookup?> FindInstalledPackageAsync(string identityName, CancellationToken ct)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $package = Get-AppxPackage -Name {{PowerShellLiteral(identityName)}} |
                Sort-Object Version -Descending |
                Select-Object -First 1
            if ($null -ne $package) {
                [pscustomobject]@{
                    PackageFullName = [string]$package.PackageFullName
                    InstallLocation = [string]$package.InstallLocation
                } | ConvertTo-Json -Compress
            }
            """;
        var result = await RunPowerShellAsync(script, ct);
        EnsureSuccess(result, "MSIX package lookup");
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return null;
        return JsonSerializer.Deserialize<AppxPackageLookup>(result.StandardOutput.Trim(), JsonOptions);
    }

    private static async Task<PowerShellResult> RunPowerShellAsync(string script, CancellationToken ct)
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershell))
            throw new InvalidOperationException("Windows PowerShell with the Appx module is not available.");

        var psi = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Windows PowerShell.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }
            throw;
        }

        return new PowerShellResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static void EnsureSuccess(PowerShellResult result, string operation)
    {
        if (result.ExitCode == 0) return;

        var detail = string.Join(
                Environment.NewLine,
                new[] { result.StandardError.Trim(), result.StandardOutput.Trim() }.Where(text => text.Length > 0))
            .Trim();
        if (detail.Length > 1600) detail = detail[..1600] + "...";

        if (LooksLikeTrustFailure(detail))
        {
            throw new InvalidOperationException(
                $"{operation} was refused because the package certificate is not trusted by this user. "
                + "Install the publisher's certificate through your approved Windows trust process; "
                + "LocalDesktopStore will not import certificates automatically. "
                + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $"Details: {detail}"));
        }

        throw new InvalidOperationException(
            $"{operation} failed with exit code {result.ExitCode}. "
            + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $"Details: {detail}"));
    }

    private static bool LooksLikeTrustFailure(string detail)
        => detail.Contains("0x800B0100", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("0x800B0101", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("0x800B0109", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("0x800B010A", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("certificate chain", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("not trusted", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("signature", StringComparison.OrdinalIgnoreCase);

    private static string PowerShellLiteral(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed class AppxPackageLookup
    {
        public string? PackageFullName { get; set; }
        public string? InstallLocation { get; set; }
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);
}
