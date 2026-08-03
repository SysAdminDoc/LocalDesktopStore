using System.IO;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

/// <summary>
/// The information available to an artifact handler before a release asset is downloaded.
/// <paramref name="PeScanKind"/> is populated when a local byte scan refines a generic EXE.
/// </summary>
public sealed record ArtifactProbe(
    string AssetName,
    ArtifactKind NameHint = ArtifactKind.Unknown,
    ArtifactKind? PeScanKind = null,
    string? LocalPath = null);

/// <summary>
/// In-process installer extension point. The application only registers classes shipped in
/// this assembly; there is deliberately no assembly, script, or network plugin loader.
/// </summary>
public interface IArtifactHandler
{
    string Id { get; }
    ArtifactKind Kind { get; }
    bool IsAvailable { get; }
    bool CanHandle(ArtifactProbe asset);
    Task<InstalledApp?> InstallAsync(ArtifactInstallContext context, CancellationToken ct);
    Task UninstallAsync(ArtifactUninstallContext context, CancellationToken ct);
    bool TryRun(ArtifactRunContext context);
}

public sealed class ArtifactInstallContext
{
    public required AppInfo Info { get; init; }
    public required AppSettings Settings { get; init; }
    public required string StagedPath { get; init; }
    public required string? CustomInstallerArguments { get; init; }
    public required IProgress<string>? Log { get; init; }

    /// <summary>Invokes the trusted, bundled implementation for a Windows/Linux artifact kind.</summary>
    public Func<ArtifactKind, CancellationToken, Task<InstalledApp?>>? InstallBundledAsync { get; init; }

    /// <summary>Hands an HTTPS .appinstaller source to Windows App Installer.</summary>
    public Action<string, IProgress<string>?>? OpenAppInstallerUri { get; init; }
}

public sealed class ArtifactUninstallContext
{
    public required InstalledApp App { get; init; }
    public required IProgress<string>? Log { get; init; }
    public Func<ArtifactKind, InstalledApp, CancellationToken, Task>? UninstallBundledAsync { get; init; }
}

public sealed class ArtifactRunContext
{
    public required InstalledApp App { get; init; }
    public required IProgress<string>? Log { get; init; }
    public required Func<InstalledApp, string?> ResolveLaunchTarget { get; init; }
    public required Func<string, bool> LaunchTarget { get; init; }
}

/// <summary>
/// Resolves only explicitly registered in-process handlers. This is the single host used by
/// discovery, install, uninstall, and run paths, so adding a handler cannot silently create a
/// second classification or execution pipeline.
/// </summary>
public sealed class ArtifactHandlerRegistry
{
    private readonly IReadOnlyList<IArtifactHandler> _handlers;

    public ArtifactHandlerRegistry(IEnumerable<IArtifactHandler> handlers)
    {
        _handlers = handlers?.ToArray() ?? throw new ArgumentNullException(nameof(handlers));
        if (_handlers.Count == 0)
            throw new ArgumentException("At least one artifact handler is required.", nameof(handlers));

        var duplicate = _handlers
            .GroupBy(handler => handler.Kind)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Multiple handlers registered for {duplicate.Key}.", nameof(handlers));
    }

    public IReadOnlyList<IArtifactHandler> Handlers => _handlers;

    public static ArtifactHandlerRegistry CreateBundled()
        => new(
        [
            new MsiArtifactHandler(),
            new AppInstallerArtifactHandler(),
            new VelopackArtifactHandler(),
            new MsixArtifactHandler(),
            new InnoArtifactHandler(),
            new NsisArtifactHandler(),
            new GenericExeArtifactHandler(),
            new PortableZipArtifactHandler(),
            new AppImageArtifactHandler()
        ]);

    public ArtifactKind ClassifyByName(string assetName)
        => TryResolve(new ArtifactProbe(assetName))?.Kind ?? ArtifactKind.Unknown;

    public IArtifactHandler Resolve(ArtifactProbe asset)
        => TryResolve(asset)
            ?? throw new InvalidOperationException($"No available artifact handler accepts '{asset.AssetName}'.");

    public IArtifactHandler? TryResolve(ArtifactProbe asset)
        => _handlers.FirstOrDefault(handler => handler.IsAvailable && handler.CanHandle(asset));
}

internal abstract class BundledArtifactHandler : IArtifactHandler
{
    public abstract string Id { get; }
    public abstract ArtifactKind Kind { get; }
    public virtual bool IsAvailable => OperatingSystem.IsWindows();

    public bool CanHandle(ArtifactProbe asset)
    {
        if (asset.PeScanKind.HasValue)
            return asset.PeScanKind.Value == Kind;
        if (asset.NameHint != ArtifactKind.Unknown)
            return asset.NameHint == Kind || NameMatches(asset.AssetName);
        return NameMatches(asset.AssetName);
    }

    protected abstract bool NameMatches(string assetName);

    public virtual Task<InstalledApp?> InstallAsync(ArtifactInstallContext context, CancellationToken ct)
        => context.InstallBundledAsync is not null
            ? context.InstallBundledAsync(Kind, ct)
            : throw new InvalidOperationException($"Bundled install operation is unavailable for {Id}.");

    public virtual Task UninstallAsync(ArtifactUninstallContext context, CancellationToken ct)
        => context.UninstallBundledAsync is not null
            ? context.UninstallBundledAsync(Kind, context.App, ct)
            : throw new InvalidOperationException($"Bundled uninstall operation is unavailable for {Id}.");

    public virtual bool TryRun(ArtifactRunContext context)
    {
        try
        {
            var target = context.ResolveLaunchTarget(context.App);
            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                context.Log?.Report($"! Could not locate executable for {context.App.RepoName}.");
                return false;
            }

            if (!context.LaunchTarget(target!))
            {
                context.Log?.Report($"! Launch failed for {context.App.RepoName}.");
                return false;
            }

            context.Log?.Report($"Launched {context.App.RepoName}.");
            return true;
        }
        catch (Exception ex)
        {
            context.Log?.Report($"! Run failed: {ex.Message}");
            return false;
        }
    }
}

internal sealed class MsiArtifactHandler : BundledArtifactHandler
{
    public override string Id => "msi";
    public override ArtifactKind Kind => ArtifactKind.Msi;
    protected override bool NameMatches(string assetName) => assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
}

internal sealed class AppInstallerArtifactHandler : BundledArtifactHandler
{
    public override string Id => "appinstaller";
    public override ArtifactKind Kind => ArtifactKind.AppInstaller;
    protected override bool NameMatches(string assetName) => assetName.EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase);

    public override Task<InstalledApp?> InstallAsync(ArtifactInstallContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.Info.AssetUrl))
            throw new InvalidOperationException("App Installer source URL is missing.");
        context.OpenAppInstallerUri?.Invoke(context.Info.AssetUrl, context.Log);
        context.Log?.Report("App Installer opened; refresh after its installation flow completes.");
        return Task.FromResult<InstalledApp?>(null);
    }
}

internal sealed class VelopackArtifactHandler : BundledArtifactHandler
{
    public override string Id => "velopack-full-package";
    public override ArtifactKind Kind => ArtifactKind.Velopack;

    protected override bool NameMatches(string assetName)
        => assetName.EndsWith(".nupkg.full", StringComparison.OrdinalIgnoreCase)
            || (assetName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                && assetName.Contains("-full", StringComparison.OrdinalIgnoreCase));

    public override Task<InstalledApp?> InstallAsync(ArtifactInstallContext context, CancellationToken ct)
        => throw new InvalidOperationException(
            "Velopack full packages are update payloads, not standalone installers. Use the Velopack Setup.exe asset or enable the LDS self-update channel.");

    public override Task UninstallAsync(ArtifactUninstallContext context, CancellationToken ct)
        => throw new InvalidOperationException("A Velopack update payload has no standalone uninstall path.");

    public override bool TryRun(ArtifactRunContext context)
    {
        context.Log?.Report("! Velopack update payloads cannot be launched directly.");
        return false;
    }
}

internal sealed class MsixArtifactHandler : BundledArtifactHandler
{
    public override string Id => "msix";
    public override ArtifactKind Kind => ArtifactKind.Msix;
    protected override bool NameMatches(string assetName)
        => assetName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
            || assetName.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase);
}

internal sealed class InnoArtifactHandler : BundledArtifactHandler
{
    public override string Id => "inno";
    public override ArtifactKind Kind => ArtifactKind.Inno;
    protected override bool NameMatches(string assetName)
    {
        var name = assetName.ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal)
            && (name.Contains("innosetup") || name.Contains("inno-setup"));
    }
}

internal sealed class NsisArtifactHandler : BundledArtifactHandler
{
    public override string Id => "nsis";
    public override ArtifactKind Kind => ArtifactKind.Nsis;
    protected override bool NameMatches(string assetName)
    {
        var name = assetName.ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) && name.Contains("nsis");
    }
}

internal sealed class GenericExeArtifactHandler : BundledArtifactHandler
{
    public override string Id => "generic-exe";
    public override ArtifactKind Kind => ArtifactKind.GenericExe;
    protected override bool NameMatches(string assetName) => assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}

internal sealed class PortableZipArtifactHandler : BundledArtifactHandler
{
    public override string Id => "portable-zip";
    public override ArtifactKind Kind => ArtifactKind.PortableZip;
    protected override bool NameMatches(string assetName) => assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}

internal sealed class AppImageArtifactHandler : BundledArtifactHandler
{
    public override string Id => "appimage";
    public override ArtifactKind Kind => ArtifactKind.AppImage;
    public override bool IsAvailable => OperatingSystem.IsLinux();
    protected override bool NameMatches(string assetName) => assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase);
}
