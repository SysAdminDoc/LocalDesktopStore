using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed class InstallService
{
    private readonly SettingsService _settings;
    private readonly GitHubService _github;
    private readonly AppxPackageService _appx;
    private readonly WingetDetectionService _winget;
    private readonly DownloadCacheService _downloadCache;
    private readonly ArtifactHandlerRegistry _artifactHandlers;
    private InstalledAppsManifest _manifest;
    private WingetDetectionSnapshot _wingetSnapshot =
        WingetDetectionSnapshot.Unavailable("WinGet oracle has not been queried yet.");

    public InstallService(SettingsService settings, GitHubService github)
    {
        _settings = settings;
        _github = github;
        _appx = new AppxPackageService();
        _winget = new WingetDetectionService();
        _downloadCache = new DownloadCacheService(settings.DownloadsDir);
        _artifactHandlers = ArtifactHandlerRegistry.CreateBundled();
        _manifest = settings.LoadManifest();
    }

    public IReadOnlyList<InstalledApp> Installed => _manifest.Apps;

    public InstalledApp? Find(string repoOwner, string repoName)
        => _manifest.Apps.FirstOrDefault(e =>
            e.RepoOwner.Equals(repoOwner, StringComparison.OrdinalIgnoreCase) &&
            e.RepoName.Equals(repoName, StringComparison.OrdinalIgnoreCase));

    public WingetDetectionSnapshot WingetSnapshot => _wingetSnapshot;

    public void Reload() => _manifest = _settings.LoadManifest();

    public async Task ReloadAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        _manifest = _settings.LoadManifest();
        _wingetSnapshot = await _winget.QueryInstalledAsync(log, ct);
        CrossCheckWingetMetadata(log);
    }

    public void UpdateInstallerArguments(InstalledApp app, string? arguments)
    {
        var current = Find(app.RepoOwner, app.RepoName);
        if (current is null) return;
        current.InstallerArguments = InstallerArgumentParser.Normalize(arguments);
        _settings.SaveManifest(_manifest);
    }

    public async Task<InstalledApp?> InstallAsync(
        AppInfo info,
        AppSettings cfg,
        IProgress<string>? log,
        IProgress<long>? bytes,
        CancellationToken ct = default,
        Func<PublisherChangeWarning, Task<bool>>? confirmPublisherChange = null)
    {
        if (string.IsNullOrEmpty(info.AssetUrl) || string.IsNullOrEmpty(info.AssetName))
            throw new InvalidOperationException("No release asset to install.");

        var initialHandler = _artifactHandlers.Resolve(new ArtifactProbe(info.AssetName!, info.Kind));
        if (initialHandler.Kind == ArtifactKind.AppInstaller)
        {
            return await initialHandler.InstallAsync(new ArtifactInstallContext
            {
                Info = info,
                Settings = cfg,
                StagedPath = string.Empty,
                CustomInstallerArguments = null,
                Log = log,
                OpenAppInstallerUri = AppxPackageService.LaunchAppInstallerUri
            }, ct);
        }

        var previous = Find(info.RepoOwner, info.RepoName);

        var safeVersion = info.DisplayVersion.Replace('/', '_').Replace('\\', '_');
        var stagingDir = Path.Combine(_settings.DownloadsDir, $"{info.RepoName}-{safeVersion}");
        Directory.CreateDirectory(stagingDir);
        var stagedFile = Path.Combine(stagingDir, info.AssetName!);

        string? sidecarText = null;
        string? expectedHash = null;
        if (cfg.VerifyHashSidecar)
        {
            if (string.IsNullOrEmpty(info.Sha256Url))
            {
                log?.Report("  ~ no .sha256.txt sidecar present in release; skipping verification.");
            }
            else
            {
                log?.Report("Reading SHA-256 sidecar...");
                sidecarText = await _github.TryDownloadTextAsync(info.Sha256Url!, ct);
                if (sidecarText is null)
                {
                    throw new InvalidOperationException("Hash sidecar download failed — refusing to install.");
                }
                expectedHash = HashVerifier.ParseSidecar(sidecarText)
                    ?? throw new InvalidOperationException("Sidecar present but no SHA-256 hash could be parsed — refusing to install.");
            }
        }

        var restoredFromCache = expectedHash is not null
            && await _downloadCache.TryRestoreAsync(info, expectedHash, stagedFile, bytes, log, ct);
        if (!restoredFromCache)
        {
            log?.Report($"Downloading {info.AssetName} ({Format(info.AssetSizeBytes)}) ...");
            await _github.DownloadAssetToFileAsync(info.AssetUrl!, stagedFile, bytes, ct);

            if (sidecarText is not null)
            {
                log?.Report("Verifying SHA-256 against sidecar...");
                var result = await HashVerifier.VerifyAsync(stagedFile, sidecarText, ct);
                if (!result.Verified)
                {
                    log?.Report($"  ! {result.Detail} (expected {result.ExpectedHash}, actual {result.ActualHash})");
                    throw new InvalidOperationException($"Hash verification failed: {result.Detail}");
                }
                log?.Report("  ✓ SHA-256 OK");
                await _downloadCache.StoreVerifiedAsync(info, expectedHash!, stagedFile, log, ct);
            }
        }

        AuthenticodeVerificationResult? publisher = null;
        if (info.Kind == ArtifactKind.PortableZip)
        {
            log?.Report("  ~ portable ZIP has no archive-level Authenticode signature; publisher pin skipped.");
        }
        else if (info.Kind is ArtifactKind.Msix or ArtifactKind.Velopack or ArtifactKind.AppImage)
        {
            log?.Report(info.Kind == ArtifactKind.Msix
                ? "  ~ MSIX signature and certificate trust will be validated by Add-AppxPackage; no certificate will be imported."
                : "  ~ archive or executable trust is handled by the artifact-specific installer.");
        }
        else
        {
            log?.Report("Verifying Authenticode signature and trusted publisher...");
            publisher = AuthenticodeVerifier.Verify(stagedFile);
            if (!publisher.IsTrusted || string.IsNullOrEmpty(publisher.Thumbprint))
                throw new InvalidOperationException($"Authenticode verification failed: {publisher.Detail}");

            log?.Report($"  ✓ Authenticode OK: {publisher.Subject} [{publisher.Thumbprint}]");
            if (!string.IsNullOrEmpty(previous?.PublisherCertThumbprint)
                && !previous.PublisherCertThumbprint.Equals(publisher.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                var warning = new PublisherChangeWarning(
                    $"{info.RepoOwner}/{info.RepoName}",
                    previous.PublisherCertThumbprint,
                    previous.PublisherCertSubject,
                    publisher.Thumbprint,
                    publisher.Subject ?? "(subject unavailable)");
                var approved = confirmPublisherChange is not null
                    && await confirmPublisherChange(warning);
                if (!approved)
                {
                    log?.Report("  ! Publisher changed — refusing to invoke the installer without explicit approval.");
                    throw new InvalidOperationException(
                        $"Publisher certificate changed from {previous.PublisherCertSubject ?? previous.PublisherCertThumbprint} "
                        + $"to {publisher.Subject ?? publisher.Thumbprint}. Installation was not started.");
                }

                log?.Report("  ~ Publisher changed; continuing after explicit approval.");
            }
        }

        var refinedKind = info.Kind is ArtifactKind.PortableZip or ArtifactKind.Msi or ArtifactKind.Msix
            ? info.Kind
            : AssetClassifier.RefineFromFile(stagedFile, info.Kind);
        if (refinedKind != info.Kind)
            log?.Report($"Asset refined to {refinedKind.DisplayName()} after byte scan.");

        var handler = _artifactHandlers.Resolve(new ArtifactProbe(info.AssetName!, info.Kind, refinedKind, stagedFile));

        var customInstallerArguments = ResolveInstallerArguments(info, cfg, previous);
        if (!string.IsNullOrWhiteSpace(customInstallerArguments))
        {
            var argumentCount = InstallerArgumentParser.Parse(customInstallerArguments).Count;
            if (handler.Kind is ArtifactKind.Msi or ArtifactKind.Inno or ArtifactKind.Nsis or ArtifactKind.GenericExe)
                log?.Report($"Applying {argumentCount} custom installer argument(s).");
            else
                log?.Report("  ~ custom installer arguments are ignored for this artifact kind.");
        }

        var record = await handler.InstallAsync(new ArtifactInstallContext
        {
            Info = info,
            Settings = cfg,
            StagedPath = stagedFile,
            CustomInstallerArguments = customInstallerArguments,
            Log = log,
            InstallBundledAsync = (kind, token) => InstallBundledAsync(
                kind, info, cfg, stagedFile, customInstallerArguments, log, token)
        }, ct) ?? throw new InvalidOperationException($"Artifact handler '{handler.Id}' did not produce an install record.");
        record.PublisherCertThumbprint = publisher?.Thumbprint;
        record.PublisherCertSubject = publisher?.Subject;
        record.InstallerArguments = customInstallerArguments;

        // Replace any prior install row for this repo.
        _manifest.Apps.RemoveAll(e =>
            e.RepoOwner.Equals(info.RepoOwner, StringComparison.OrdinalIgnoreCase) &&
            e.RepoName.Equals(info.RepoName, StringComparison.OrdinalIgnoreCase));
        _manifest.Apps.Add(record);
        _settings.SaveManifest(_manifest);
        log?.Report($"Installed {info.DisplayName} v{info.DisplayVersion} ({record.Kind.DisplayName()}).");
        return record;
    }

    public async Task UninstallAsync(InstalledApp app, IProgress<string>? log, CancellationToken ct = default)
    {
        log?.Report($"Uninstalling {app.RepoName} v{app.Version} ({app.Kind.DisplayName()})...");
        var handler = _artifactHandlers.Resolve(new ArtifactProbe(
            $"{app.RepoName}.{app.Kind}", app.Kind, app.Kind));
        await handler.UninstallAsync(new ArtifactUninstallContext
        {
            App = app,
            Log = log,
            UninstallBundledAsync = (kind, installed, token) => UninstallBundledAsync(kind, installed, log, token)
        }, ct);

        _manifest.Apps.RemoveAll(e =>
            e.RepoOwner.Equals(app.RepoOwner, StringComparison.OrdinalIgnoreCase) &&
            e.RepoName.Equals(app.RepoName, StringComparison.OrdinalIgnoreCase));
        _settings.SaveManifest(_manifest);
        log?.Report($"Uninstall complete: {app.RepoOwner}/{app.RepoName}");
    }

    public bool TryRun(InstalledApp app, IProgress<string>? log)
    {
        try
        {
            var handler = _artifactHandlers.Resolve(new ArtifactProbe(
                $"{app.RepoName}.{app.Kind}", app.Kind, app.Kind));
            return handler.TryRun(new ArtifactRunContext
            {
                App = app,
                Log = log,
                ResolveLaunchTarget = ResolveLaunchTarget,
                LaunchTarget = LaunchTarget
            });
        }
        catch (Exception ex)
        {
            log?.Report($"! Run failed: {ex.Message}");
            return false;
        }
    }

    private static bool LaunchTarget(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(target) ?? ""
        });
        return true;
    }

    public bool TryPinToTaskbar(InstalledApp app, IProgress<string>? log)
    {
        try
        {
            var target = !string.IsNullOrEmpty(app.ShortcutPath) && File.Exists(app.ShortcutPath)
                ? app.ShortcutPath
                : app.Kind == ArtifactKind.PortableZip
                    ? app.ExecutablePath
                    : ResolveLaunchExe(app);
            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                log?.Report($"  ~ Could not pin {app.RepoName}: no launch target was located.");
                return false;
            }

            return TaskbarPinService.TryPin(target, log);
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ Taskbar pinning failed for {app.RepoName}: {ex.Message}");
            return false;
        }
    }

    private string? ResolveLaunchTarget(InstalledApp app)
    {
        if (app.Kind is ArtifactKind.PortableZip or ArtifactKind.AppImage)
        {
            return !string.IsNullOrEmpty(app.ExecutablePath) && File.Exists(app.ExecutablePath)
                ? app.ExecutablePath
                : !string.IsNullOrEmpty(app.PortableRoot) ? FindPrimaryExe(app.PortableRoot!) : null;
        }
        return ResolveLaunchExe(app);
    }

    private string? ResolveLaunchExe(InstalledApp app)
    {
        // Strategy 1: DisplayIcon often points to the main executable for installer-driven apps.
        if (!string.IsNullOrEmpty(app.UninstallRegistryKey))
        {
            // We didn't store DisplayIcon at install time. Re-query the registry by name.
            var entry = UninstallRegistry.FindBestMatch(app.RepoOwner, app.RepoName, app.Version);
            if (entry != null)
            {
                if (!string.IsNullOrEmpty(entry.IconPath))
                {
                    var iconPath = entry.IconPath.Trim('"');
                    var commaIdx = iconPath.LastIndexOf(',');
                    if (commaIdx > 0) iconPath = iconPath.Substring(0, commaIdx);
                    if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(iconPath))
                        return iconPath;
                }
                if (!string.IsNullOrEmpty(entry.InstallLocation) && Directory.Exists(entry.InstallLocation))
                {
                    return FindPrimaryExe(entry.InstallLocation);
                }
            }
        }
        if (!string.IsNullOrEmpty(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            return FindPrimaryExe(app.InstallLocation);
        return null;
    }

    private async Task<InstalledApp?> InstallBundledAsync(
        ArtifactKind kind,
        AppInfo info,
        AppSettings cfg,
        string stagedPath,
        string? customArguments,
        IProgress<string>? log,
        CancellationToken ct)
    {
        return kind switch
        {
            ArtifactKind.Msi => await InstallMsiAsync(info, stagedPath, customArguments, log, ct),
            ArtifactKind.Inno => await RunInstallerAsync(info, stagedPath, kind, "/SILENT /NORESTART", customArguments, log, ct),
            ArtifactKind.Nsis => await RunInstallerAsync(info, stagedPath, kind, "/S", customArguments, log, ct),
            ArtifactKind.GenericExe => await RunInstallerAsync(info, stagedPath, kind, null, customArguments, log, ct),
            ArtifactKind.PortableZip => await InstallPortableAsync(info, cfg, stagedPath, log, ct),
            ArtifactKind.Msix => await InstallMsixAsync(info, stagedPath, log, ct),
            ArtifactKind.AppImage => await InstallAppImageAsync(info, cfg, stagedPath, log, ct),
            _ => throw new InvalidOperationException($"Unsupported artifact kind: {kind}")
        };
    }

    private async Task UninstallBundledAsync(
        ArtifactKind kind,
        InstalledApp app,
        IProgress<string>? log,
        CancellationToken ct)
    {
        switch (kind)
        {
            case ArtifactKind.Msi:
                await UninstallMsiAsync(app, log, ct);
                break;
            case ArtifactKind.Inno:
            case ArtifactKind.Nsis:
            case ArtifactKind.GenericExe:
                await UninstallExeAsync(app, log, ct);
                break;
            case ArtifactKind.PortableZip:
            case ArtifactKind.AppImage:
                UninstallPortable(app, log);
                break;
            case ArtifactKind.Msix:
                await _appx.UninstallAsync(app, log, ct);
                break;
            default:
                throw new InvalidOperationException($"Unsupported uninstall kind: {kind}");
        }
    }

    // ----- MSI -----

    private async Task<InstalledApp> InstallMsiAsync(
        AppInfo info,
        string msiPath,
        string? customArguments,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var preSnapshot = SnapshotEntries();
        var logPath = Path.Combine(_settings.LogsDir, $"msi-{info.RepoName}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        log?.Report($"Running msiexec /i \"{Path.GetFileName(msiPath)}\" /qb /norestart");
        log?.Report($"  log: {logPath}");
        var psi = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("/i");
        psi.ArgumentList.Add(msiPath);
        psi.ArgumentList.Add("/qb");
        psi.ArgumentList.Add("/norestart");
        foreach (var argument in InstallerArgumentParser.Parse(customArguments))
            psi.ArgumentList.Add(argument);
        psi.ArgumentList.Add("/L*v");
        psi.ArgumentList.Add(logPath);
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start msiexec.");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0 && proc.ExitCode != 3010 /* reboot required */)
            throw new InvalidOperationException($"msiexec returned exit code {proc.ExitCode}. See {logPath}");

        var entry = DiffNewEntry(preSnapshot, info.RepoName);
        return new InstalledApp
        {
            RepoOwner = info.RepoOwner,
            RepoName = info.RepoName,
            Version = info.DisplayVersion,
            Kind = ArtifactKind.Msi,
            InstalledAt = DateTimeOffset.UtcNow,
            UninstallRegistryKey = entry?.SubKeyName,
            UninstallCommand = entry?.QuietUninstallString ?? entry?.UninstallString,
            InstallLocation = entry?.InstallLocation,
            MsiProductCode = entry?.IsMsi == true ? entry.SubKeyName : null
        };
    }

    private async Task UninstallMsiAsync(InstalledApp app, IProgress<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(app.MsiProductCode) && string.IsNullOrEmpty(app.UninstallCommand))
            throw new InvalidOperationException("MSI uninstall requires a ProductCode or UninstallString — neither is recorded.");

        var logPath = Path.Combine(_settings.LogsDir, $"msi-uninst-{app.RepoName}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var psi = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(app.MsiProductCode))
        {
            psi.ArgumentList.Add("/x");
            psi.ArgumentList.Add(app.MsiProductCode!);
            psi.ArgumentList.Add("/qb");
            psi.ArgumentList.Add("/norestart");
            psi.ArgumentList.Add("/L*v");
            psi.ArgumentList.Add(logPath);
        }
        else
        {
            // UninstallString from registry — split and forward.
            log?.Report("Falling back to recorded UninstallString.");
            await RunRawCommandAsync(app.UninstallCommand!, log, ct);
            return;
        }
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start msiexec.");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0 && proc.ExitCode != 3010)
            throw new InvalidOperationException($"msiexec /x returned {proc.ExitCode}. See {logPath}");
    }

    // ----- MSIX -----

    private async Task<InstalledApp> InstallMsixAsync(
        AppInfo info,
        string packagePath,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var result = await _appx.InstallPackageAsync(packagePath, log, ct);
        return new InstalledApp
        {
            RepoOwner = info.RepoOwner,
            RepoName = info.RepoName,
            Version = info.DisplayVersion,
            Kind = ArtifactKind.Msix,
            InstalledAt = DateTimeOffset.UtcNow,
            AppxPackageName = result.IdentityName,
            AppxPackageFullName = result.PackageFullName,
            InstallLocation = result.InstallLocation
        };
    }

    private void CrossCheckWingetMetadata(IProgress<string>? log)
    {
        if (!_wingetSnapshot.IsAvailable) return;

        foreach (var app in _manifest.Apps)
        {
            var package = _wingetSnapshot.FindFor(app.RepoOwner, app.RepoName);
            if (package is null) continue;

            var wingetCommands = new[]
                {
                    package.StandardUninstallCommand,
                    package.SilentUninstallCommand
                }
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Select(command => command!)
                .ToList();
            if (!string.IsNullOrWhiteSpace(app.UninstallCommand)
                && wingetCommands.Count > 0
                && !wingetCommands.Any(command => CommandsOverlap(app.UninstallCommand!, command)))
            {
                log?.Report($"  ~ WinGet cross-check differs for {app.RepoOwner}/{app.RepoName}; keeping LDS's recorded registry uninstall command.");
            }
            else
            {
                log?.Report($"  ✓ WinGet cross-check matched {app.RepoOwner}/{app.RepoName} ({package.Id} v{package.Version}).");
            }
        }
    }

    private static bool CommandsOverlap(string left, string right)
    {
        var normalizedLeft = NormalizeCommand(left);
        var normalizedRight = NormalizeCommand(right);
        return normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCommand(string command)
        => string.Join(' ', command.Trim().Trim('"').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ----- EXE installers -----

    private async Task<InstalledApp> RunInstallerAsync(
        AppInfo info,
        string exePath,
        ArtifactKind kind,
        string? silentArgs,
        string? customArguments,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var preSnapshot = SnapshotEntries();
        var arguments = InstallerArgumentParser.Parse(silentArgs)
            .Concat(InstallerArgumentParser.Parse(customArguments))
            .ToList();
        var psi = new ProcessStartInfo(exePath);
        if (arguments.Count > 0)
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);
            log?.Report($"Running {Path.GetFileName(exePath)} with {arguments.Count} installer argument(s).");
        }
        else
        {
            log?.Report($"Running interactive installer {Path.GetFileName(exePath)} (no silent mode known)");
        }
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start installer.");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0 && proc.ExitCode != 3010)
            log?.Report($"  ~ installer exit code {proc.ExitCode} — proceeding with detection anyway.");

        var entry = DiffNewEntry(preSnapshot, info.RepoName);
        if (entry is null)
            log?.Report($"  ~ no new uninstall registry entry detected. The installer may have skipped registration or written to a non-standard location.");

        return new InstalledApp
        {
            RepoOwner = info.RepoOwner,
            RepoName = info.RepoName,
            Version = info.DisplayVersion,
            Kind = kind,
            InstalledAt = DateTimeOffset.UtcNow,
            UninstallRegistryKey = entry?.SubKeyName,
            UninstallCommand = entry?.QuietUninstallString ?? entry?.UninstallString,
            InstallLocation = entry?.InstallLocation
        };
    }

    private async Task UninstallExeAsync(InstalledApp app, IProgress<string>? log, CancellationToken ct)
    {
        var cmd = app.UninstallCommand;
        if (string.IsNullOrEmpty(cmd))
        {
            // Fall back to a fresh registry lookup.
            var entry = UninstallRegistry.FindBestMatch(app.RepoOwner, app.RepoName, app.Version);
            cmd = entry?.QuietUninstallString ?? entry?.UninstallString;
        }
        if (string.IsNullOrEmpty(cmd))
            throw new InvalidOperationException("No UninstallString could be located for this app.");

        // Append silent flags appropriate to the kind if not already silent.
        if (app.Kind == ArtifactKind.Inno && !cmd.Contains("/SILENT", StringComparison.OrdinalIgnoreCase))
            cmd += " /SILENT /NORESTART";
        else if (app.Kind == ArtifactKind.Nsis && !cmd.Contains("/S", StringComparison.Ordinal))
            cmd += " /S";

        log?.Report($"Running: {cmd}");
        await RunRawCommandAsync(cmd, log, ct);
    }

    private static async Task RunRawCommandAsync(string commandLine, IProgress<string>? log, CancellationToken ct)
    {
        var (exe, args) = SplitCommand(commandLine);
        if (string.IsNullOrEmpty(exe))
            throw new InvalidOperationException("Could not parse command line.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = args ?? ""
        };
        var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0 && proc.ExitCode != 3010)
            log?.Report($"  ~ exit code {proc.ExitCode}");
    }

    private static (string exe, string? args) SplitCommand(string commandLine)
    {
        commandLine = commandLine.Trim();
        if (string.IsNullOrEmpty(commandLine)) return ("", null);
        if (commandLine.StartsWith('"'))
        {
            var end = commandLine.IndexOf('"', 1);
            if (end < 0) return (commandLine.Trim('"'), null);
            var exe = commandLine.Substring(1, end - 1);
            var rest = commandLine.Length > end + 1 ? commandLine.Substring(end + 1).Trim() : null;
            return (exe, string.IsNullOrEmpty(rest) ? null : rest);
        }
        var idx = commandLine.IndexOf(' ');
        if (idx < 0) return (commandLine, null);
        return (commandLine.Substring(0, idx), commandLine.Substring(idx + 1).Trim());
    }

    private static string? ResolveInstallerArguments(
        AppInfo info,
        AppSettings cfg,
        InstalledApp? previous)
    {
        var key = $"{info.RepoOwner}/{info.RepoName}";
        if (cfg.InstallPreferences?.TryGetValue(key, out var preference) == true)
            return InstallerArgumentParser.Normalize(preference.InstallerArguments);
        return InstallerArgumentParser.Normalize(previous?.InstallerArguments);
    }

    // ----- Portable -----

    private async Task<InstalledApp> InstallPortableAsync(AppInfo info, AppSettings cfg, string zipPath, IProgress<string>? log, CancellationToken ct)
    {
        var safeVersion = info.DisplayVersion.Replace('/', '_').Replace('\\', '_');
        var targetDir = Path.Combine(_settings.AppsRoot(cfg), info.RepoOwner, info.RepoName, safeVersion);
        if (Directory.Exists(targetDir))
        {
            log?.Report($"Removing previous extraction at {targetDir}");
            Directory.Delete(targetDir, recursive: true);
        }
        Directory.CreateDirectory(targetDir);
        log?.Report($"Extracting ZIP to {targetDir}");
        await Task.Run(() => ExtractZip(zipPath, targetDir), ct);

        var exe = FindPrimaryExe(targetDir)
            ?? throw new InvalidOperationException("No .exe found in the portable archive.");
        log?.Report($"Selected launcher: {Path.GetFileName(exe)}");

        var lnkPath = ShortcutService.ShortcutPathFor(info.RepoName);
        ShortcutService.Create(info.RepoName, exe, Path.GetDirectoryName(exe), info.DisplayDescription);
        log?.Report($"Start Menu shortcut: {lnkPath}");

        // Prune older versions of the same repo.
        PruneOldPortableVersions(_settings.AppsRoot(cfg), info.RepoOwner, info.RepoName, safeVersion, log);

        return new InstalledApp
        {
            RepoOwner = info.RepoOwner,
            RepoName = info.RepoName,
            Version = info.DisplayVersion,
            Kind = ArtifactKind.PortableZip,
            InstalledAt = DateTimeOffset.UtcNow,
            PortableRoot = targetDir,
            ShortcutPath = lnkPath,
            ExecutablePath = exe
        };
    }

    private async Task<InstalledApp> InstallAppImageAsync(AppInfo info, AppSettings cfg, string appImagePath, IProgress<string>? log, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("AppImage installation is available only on Linux.");

        var safeVersion = info.DisplayVersion.Replace('/', '_').Replace('\\', '_');
        var targetDir = Path.Combine(_settings.AppsRoot(cfg), info.RepoOwner, info.RepoName, safeVersion);
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, recursive: true);
        Directory.CreateDirectory(targetDir);

        var targetFile = Path.Combine(targetDir, Path.GetFileName(info.AssetName) ?? "app.appimage");
        log?.Report($"Copying AppImage to {targetDir}");
        await using (var source = new FileStream(appImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var destination = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(destination, ct);
        }

        var mode = File.GetUnixFileMode(targetFile)
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(targetFile, mode);
        log?.Report($"Selected launcher: {Path.GetFileName(targetFile)}");

        PruneOldPortableVersions(_settings.AppsRoot(cfg), info.RepoOwner, info.RepoName, safeVersion, log);
        return new InstalledApp
        {
            RepoOwner = info.RepoOwner,
            RepoName = info.RepoName,
            Version = info.DisplayVersion,
            Kind = ArtifactKind.AppImage,
            InstalledAt = DateTimeOffset.UtcNow,
            PortableRoot = targetDir,
            ExecutablePath = targetFile
        };
    }

    private static void UninstallPortable(InstalledApp app, IProgress<string>? log)
    {
        if (!string.IsNullOrEmpty(app.PortableRoot))
        {
            // Walk up to the per-repo folder and remove all versions, not just this one.
            var repoDir = Directory.GetParent(app.PortableRoot)?.FullName;
            try
            {
                if (!string.IsNullOrEmpty(repoDir) && Directory.Exists(repoDir))
                {
                    Directory.Delete(repoDir, recursive: true);
                    log?.Report($"Removed {repoDir}");
                }
            }
            catch (Exception ex)
            {
                log?.Report($"! Failed to remove {repoDir}: {ex.Message}");
            }
        }
        ShortcutService.Remove(app.ShortcutPath);
    }

    private static void ExtractZip(string zipPath, string targetDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        // Detect a single top-level wrapper folder so we can flatten if present.
        string? wrapper = null;
        var rootEntries = zip.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(n => n.Length > 0)
            .ToList();
        if (rootEntries.Count > 0)
        {
            var firstSegments = rootEntries.Select(n => n.Split('/').First()).Distinct().ToList();
            if (firstSegments.Count == 1
                && rootEntries.All(n => n.StartsWith(firstSegments[0] + "/", StringComparison.Ordinal) || n == firstSegments[0]))
                wrapper = firstSegments[0];
        }

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                continue;
            var rel = entry.FullName.Replace('\\', '/');
            if (wrapper != null && rel.StartsWith(wrapper + "/", StringComparison.Ordinal))
                rel = rel.Substring(wrapper.Length + 1);
            if (string.IsNullOrEmpty(rel)) continue;

            var dest = Path.GetFullPath(Path.Combine(targetDir, rel));
            // Zip-slip guard
            if (!dest.StartsWith(Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to extract path outside target: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var es = entry.Open();
            using var fs = File.Create(dest);
            es.CopyTo(fs);
        }
    }

    private static string? FindPrimaryExe(string root)
    {
        if (!Directory.Exists(root)) return null;
        var exes = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
            .Select(p => new FileInfo(p))
            // Skip uninstaller stubs and obvious helper executables.
            .Where(fi => !fi.Name.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            .Where(fi => !fi.Name.Equals("vc_redist.exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(fi => fi.Length)
            .ToList();
        return exes.FirstOrDefault()?.FullName;
    }

    private static void PruneOldPortableVersions(string appsRoot, string owner, string repo, string keepVersion, IProgress<string>? log)
    {
        var repoDir = Path.Combine(appsRoot, owner, repo);
        if (!Directory.Exists(repoDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(repoDir))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals(keepVersion, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                log?.Report($"Pruned old version: {name}");
            }
            catch (Exception ex)
            {
                log?.Report($"! Could not prune {dir}: {ex.Message}");
            }
        }
    }

    // ----- Registry diff helpers -----

    private static HashSet<string> SnapshotEntries()
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in UninstallRegistry.ReadAll())
            s.Add(e.Hive + "::" + e.SubKeyName);
        return s;
    }

    private static UninstallEntry? DiffNewEntry(HashSet<string> preSnapshot, string repoNameHint)
    {
        var post = UninstallRegistry.ReadAll();
        var diff = post.Where(e => !preSnapshot.Contains(e.Hive + "::" + e.SubKeyName)).ToList();
        if (diff.Count == 0) return UninstallRegistry.FindBestMatch("", repoNameHint);
        if (diff.Count == 1) return diff[0];
        // Multiple new keys appeared (e.g. installer wrote a bundle entry too) — pick the one that mentions the repo name.
        return diff.FirstOrDefault(e => e.DisplayName.Contains(repoNameHint, StringComparison.OrdinalIgnoreCase))
            ?? diff[0];
    }

    private static string Format(long bytes)
    {
        if (bytes <= 0) return "?";
        string[] units = ["B", "KB", "MB", "GB"];
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}
