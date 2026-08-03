using System.Runtime.InteropServices;
using LocalDesktopStore.Models;
using Microsoft.Management.Deployment;

namespace LocalDesktopStore.Services;

public sealed record WingetInstalledPackage(
    string Id,
    string Name,
    string Version,
    string? Publisher,
    string? InstalledLocation,
    string? StandardUninstallCommand,
    string? SilentUninstallCommand,
    IReadOnlyList<string> PackageFamilyNames);

public sealed class WingetDetectionSnapshot
{
    private WingetDetectionSnapshot(
        bool isAvailable,
        IReadOnlyList<WingetInstalledPackage> packages,
        string? error)
    {
        IsAvailable = isAvailable;
        Packages = packages;
        Error = error;
    }

    public bool IsAvailable { get; }
    public IReadOnlyList<WingetInstalledPackage> Packages { get; }
    public string? Error { get; }

    public static WingetDetectionSnapshot Available(IReadOnlyList<WingetInstalledPackage> packages)
        => new(true, packages, null);

    public static WingetDetectionSnapshot Unavailable(string error)
        => new(false, Array.Empty<WingetInstalledPackage>(), error);

    public WingetInstalledPackage? FindFor(string repoOwner, string repoName)
    {
        var packageId = $"{repoOwner}.{repoName}";
        var exact = Packages.FirstOrDefault(package =>
            package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase)
            || package.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var containing = Packages.Where(package =>
                package.Id.Contains(repoName, StringComparison.OrdinalIgnoreCase)
                || package.Name.Contains(repoName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return containing.Count == 1 ? containing[0] : null;
    }
}

/// <summary>
/// Best-effort WinGet local-catalog oracle. The registry diff remains the install
/// authority; this service only reports what WinGet can independently see and checks
/// its uninstall metadata without replacing the recorded command.
/// </summary>
public sealed class WingetDetectionService
{
    private const uint ClsctxLocalServer = 0x4;
    private const int CoInitMultithreaded = 0x0;

    private static readonly Guid PackageManagerClassId =
        new(0xC53A4F16, 0x787E, 0x42A4, 0xB3, 0x04, 0x29, 0xEF, 0xFB, 0x4B, 0xF5, 0x97);

    private static readonly Guid PackageManagerInterfaceId =
        new(0xB375E3B9, 0xF2E0, 0x5C93, 0x87, 0xA7, 0xB6, 0x74, 0x97, 0xF7, 0xE5, 0x93);

    public async Task<WingetDetectionSnapshot> QueryInstalledAsync(
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() => QueryInstalled(log, ct), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var detail = ex.GetBaseException().Message;
            log?.Report($"  ~ WinGet oracle unavailable; registry detection remains authoritative: {detail}");
            return WingetDetectionSnapshot.Unavailable(detail);
        }
    }

    public static WingetDetectionSnapshot QueryInstalledSynchronously(IProgress<string>? log = null)
    {
        try
        {
            return QueryInstalled(log, CancellationToken.None);
        }
        catch (Exception ex)
        {
            var detail = ex.GetBaseException().Message;
            log?.Report($"  ~ WinGet oracle unavailable; registry detection remains authoritative: {detail}");
            return WingetDetectionSnapshot.Unavailable(detail);
        }
    }

    private static WingetDetectionSnapshot QueryInstalled(IProgress<string>? log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var initialized = false;
        try
        {
            var initResult = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);
            if (initResult < 0 && initResult != unchecked((int)0x80010106))
                Marshal.ThrowExceptionForHR(initResult);
            initialized = initResult >= 0;

            var packageManager = CreatePackageManager();
            var reference = packageManager.GetLocalPackageCatalog(LocalPackageCatalog.InstalledPackages);
            if (reference is null)
                throw new InvalidOperationException("WinGet did not expose its installed-package catalog.");

            log?.Report("Querying WinGet's installed-package catalog...");
            var connected = reference.Connect();
            if (connected.Status != ConnectResultStatus.Ok || connected.PackageCatalog is null)
            {
                throw new InvalidOperationException(
                    $"WinGet local catalog connection failed ({connected.Status}).");
            }

            var options = new FindPackagesOptions { ResultLimit = 10_000 };
            var result = connected.PackageCatalog.FindPackages(options);
            if (result.Status != FindPackagesResultStatus.Ok)
            {
                throw new InvalidOperationException($"WinGet package enumeration failed ({result.Status}).");
            }

            var packages = new List<WingetInstalledPackage>();
            foreach (var match in result.Matches)
            {
                ct.ThrowIfCancellationRequested();
                var package = match.CatalogPackage;
                var installed = package?.InstalledVersion;
                if (package is null || installed is null) continue;

                packages.Add(new WingetInstalledPackage(
                    package.Id,
                    package.Name,
                    installed.Version,
                    installed.Publisher,
                    ReadMetadata(installed, PackageVersionMetadataField.InstalledLocation),
                    ReadMetadata(installed, PackageVersionMetadataField.StandardUninstallCommand),
                    ReadMetadata(installed, PackageVersionMetadataField.SilentUninstallCommand),
                    installed.PackageFamilyNames?.ToArray() ?? Array.Empty<string>()));
            }

            log?.Report($"  WinGet oracle found {packages.Count} installed package(s).");
            return WingetDetectionSnapshot.Available(packages);
        }
        finally
        {
            if (initialized) CoUninitialize();
        }
    }

    private static string? ReadMetadata(
        PackageVersionInfo package,
        PackageVersionMetadataField field)
    {
        try
        {
            var value = package.GetMetadata(field);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static PackageManager CreatePackageManager()
    {
        var classId = PackageManagerClassId;
        var interfaceId = PackageManagerInterfaceId;
        var result = CoCreateInstance(
            ref classId,
            IntPtr.Zero,
            ClsctxLocalServer,
            ref interfaceId,
            out var nativePackageManager);
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);

        try
        {
            return PackageManager.FromAbi(nativePackageManager);
        }
        finally
        {
            Marshal.Release(nativePackageManager);
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outer,
        uint context,
        ref Guid interfaceId,
        out IntPtr instance);
}
