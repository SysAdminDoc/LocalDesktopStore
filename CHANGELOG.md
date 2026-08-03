# Changelog

All notable changes to LocalDesktopStore are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to [Semantic Versioning](https://semver.org/).

## Unreleased

### Added
- **Authenticode verification and publisher pinning** (N6) — downloaded MSI/EXE installers must pass Windows `WinVerifyTrust` before execution. The trusted signer thumbprint and subject are stored in schema v2 of `installed.json`; a changed publisher requires explicit approval before the installer can start. Portable ZIPs retain their existing SHA-256 path and are noted as archive-level signature skips.
- **Per-card crash-log access** (N10) — failed install and uninstall cards keep their inline error and now expose a "View crash log" link to the existing LocalDesktopStore logs directory.
- **Accessibility names and live activity log** (N11) — interactive controls expose deliberate UI Automation names, and the activity log uses a polite live region for Narrator announcements.
- **WinGet manifest export** (N12) — each card can download/hash its release asset and write a v1.6 singleton manifest under `manifests/<first-letter>/<owner>/<repo>/<version>/`, including the installer type, silent switches, architecture, SHA-256, and nested portable metadata where applicable.
- **Runtime theme switching** (X1) — settings can swap between Catppuccin Mocha and Latte immediately, with an optional Windows system-accent override for primary actions and focus rings.
- **Scheduled background update checks** (X2) — optional 1–24 hour polling uses a current-user, least-privilege Task Scheduler entry, a headless `--scheduled-check` path, and native tray notifications; it never installs updates automatically.
- **Bulk selection operations** (X3) — card checkboxes reveal sequential install, update, and uninstall commands with one aggregate status banner and per-card error attribution.
- **Catalog file transfer** (X4) — File → Export/Import round-trips owners, hidden app overrides, preferences, and version pins through a bounded, validated `.lds.json` document without exporting the GitHub PAT.
- **MSIX and App Installer support** (X5) — `.msix` / `.msixbundle` assets install and uninstall through current-user `Add-AppxPackage` / `Remove-AppxPackage`, while `.appinstaller` release URLs open the Windows App Installer protocol. Certificate trust failures are explicit and no certificate is imported automatically.
- **WinGet detection oracle** (X6) — refreshes query the local WinGet installed-package catalog through `Microsoft.WindowsPackageManager.ComInterop`, cross-check recorded uninstall metadata, and keep registry detection authoritative when the optional COM server is unavailable.

### Changed
- Refreshes now use a lockfile aligned with the framework-dependent project target, so locked restores no longer carry a stale `win-x64` runtime graph.

## v0.2.1 - 2026-06-27

### Added
- **Settings UI for extra owners and hidden repos** (N4) - the settings drawer now edits `ExtraOwners` and `HiddenRepos` directly, normalizes duplicates, persists changes immediately, and applies hidden-repo filtering to the current catalog view.
- **Per-card hide action** - each app card can hide its repo without manually editing `%APPDATA%\LocalDesktopStore\settings.json`.

### Changed
- Uninstall now runs immediately through the existing status/log feedback path instead of showing a confirmation dialog.
- Removed the Escape-key settings drawer shortcut so the UI has no hidden keyboard-only commands.
- Status bar version, assembly version, `User-Agent`, Octokit `ProductHeaderValue`, and README badge bumped to `0.2.1`.

## v0.2.0 — 2026-04-25

Slice B headline feature pass — update lifecycle. Promotes v0.2.0-alpha (Slice A groundwork) to a full v0.2.0 release.

### Added
- **Update detection on refresh** (N1) — `Services/VersionCompare.cs` parses GitHub release tags as semver-ish (strips `v`/`V`, splits prerelease, compares dotted-numeric core, ties broken by prerelease per semver 2.0). Date-driven tags like `2026.04.25` compare numerically; tags that won't parse fall back to case-insensitive ordinal equality with non-equal treated as "different". `AppCardViewModel.IsUpdateAvailable` and `InstallButtonLabel` route through the new comparer.
- **"Update all" toolbar action** (N2) — sequential `await` loop over every card where the remote release is newer than the local pin. Per-card error attribution kept intact via the existing `AppCardViewModel.HasError`/`ErrorMessage` plumbing. Button surfaces only when at least one app is outdated and labels itself with the count (e.g. `Update all (3)`).
- **ETag-based catalog refresh** (N3) — new `Services/EtagCachingHandler.cs` (DelegatingHandler) wired into Octokit via `HttpClientAdapter`. Adds `If-None-Match` on every GET it sees, replays cached body on 304. 304 responses do not count against GitHub's rate limit per [GitHub conditional requests](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api). Per-token cache: rotating the PAT instantiates a fresh handler so one user's payload is never replayed to a different account. Activity log reports `ETag cache: <hits> 304 hit(s), <misses> fresh fetch(es)` after each Discover.
- `MainViewModel.RefreshAsync` now `_installer.Reload()`s the manifest before rebuilding the card collection so out-of-band installs surface immediately on the next refresh.

### Changed
- `AppCardViewModel.InstallAsync` extracted to a public `RunInstallAsync(CancellationToken)` so the new `UpdateAllCommand` can drive it without going through `ICommand.Execute`.
- `AppInfo.IconUrl` (single string) → already-shipped `IconCandidates` chain (no on-disk impact, this entry just reaffirms the shape established in v0.2.0-alpha).
- Status bar version + `User-Agent` + Octokit `ProductHeaderValue` bumped to `0.2.0`.

## v0.2.0-alpha — 2026-04-25

Slice A groundwork pass — additive only, no behavior change to install / uninstall / run paths. Sets up the safety net that the v0.2.0 update-lifecycle work (N1-N3) and the v0.2.2 trust pass (N6, N9) build on.

### Added
- **Icon fallback chain** (N5) — `GitHubService.ResolveIconCandidates` now probes `logo.png` → `banner.png` → `icon.png` → GitHub OG-image API (`opengraph.githubassets.com`) before giving up. `AppCardViewModel.LoadIconAsync` walks the chain, caches the first hit. Fewer "APP" placeholder cards on third-party owners.
- **Schema-versioned `installed.json` migrator** (N7) — new `IInstalledManifestMigrator` interface plus `InstalledManifestMigrationRunner` walks the manifest from its on-disk schema version to `CurrentSchemaVersion`. No migrators registered yet (current schema = 1); future record fields (cert thumbprint for N6, MSIX product family for X5) ship as one migrator each. Forward-rolled files from a newer build now refuse loudly instead of silently dropping fields.
- **Reproducible builds + SourceLink** (N8) — `DotNet.ReproducibleBuilds 2.0.2` + `Microsoft.SourceLink.GitHub 8.0.0` referenced as private build assets. `ContinuousIntegrationBuild` lights up under `GITHUB_ACTIONS`; `EmbedUntrackedSources` + `PublishRepositoryUrl` set so crash-log stack traces map back to source. Sets up SLSA L2 provenance in N9.
- **Dependency scanning CI** (N13) — `.github/dependabot.yml` watches NuGet (weekly, /src/LocalDesktopStore) and `github-actions` (weekly). `.github/workflows/dependabot-auto-merge.yml` enables auto-merge on direct production patch updates (still gated by branch protection). Release workflow now runs OSV-Scanner against the restored project before publish; any advisory fails the build.

### Changed
- `AppInfo.IconUrl` (single string) replaced with `AppInfo.IconCandidates` (ordered list). Internal type only; no on-disk impact.
- Status bar version + `User-Agent` + Octokit `ProductHeaderValue` bumped to `0.2.0-alpha`.

## v0.1.0 — 2026-04-25

Initial public release. Desktop sibling of [LocalChromeStore](https://github.com/SysAdminDoc/LocalChromeStore).

### Added
- WPF / .NET 9 store UI in Catppuccin Mocha — card grid with install / run / uninstall / folder / repo buttons
- GitHub-sourced discovery via Octokit 13.x — primary user + extra owners + optional GitHub topic filter (default `windows-app`)
- Smart asset classification: `*.msi` → MSI, `*.exe` with Inno Setup signature → Inno, `*.exe` with Nullsoft / NSIS signature → NSIS, generic `*setup*.exe` / `*installer*.exe` → interactive, `*.zip` → portable
- File-content scan refines `GenericExe` to `Inno` / `Nsis` after download (bounded 4 MB byte scan + `FileVersionInfo`)
- Install handlers
  - MSI: `msiexec /i <file> /qb /norestart` with verbose log to `%LOCALAPPDATA%\LocalDesktopStore\logs\`
  - Inno Setup: `<file> /SILENT /NORESTART`
  - NSIS: `<file> /S`
  - Generic installer: interactive launch
  - Portable ZIP: extract to `%LOCALAPPDATA%\LocalDesktopStore\apps\<owner>\<repo>\<version>\`, find the largest non-uninstaller `.exe`, create Start Menu shortcut via `IShellLink` COM
- Uninstall handlers
  - MSI: `msiexec /x <ProductCode> /qb /norestart`
  - Inno / NSIS: invoke recorded `UninstallString` / `QuietUninstallString`
  - Portable: delete extraction folder + remove shortcut
- Run handler — DisplayIcon path → InstallLocation primary `.exe` → portable launcher
- Install-state detection via registry diff — snapshot `HKLM`, `HKLM\WOW6432Node`, and `HKCU` uninstall keys pre-install, then identify the new entry post-install. No WMI dependency.
- SHA-256 sidecar verification — when a release ships `<asset>.sha256.txt`, verify before invoking the installer and refuse on mismatch
- Settings drawer — GitHub user, optional PAT (PasswordBox + codebehind sync), topic filter toggle, install root override, hash verification toggle
- Activity log panel + on-disk crash log writer at `%LOCALAPPDATA%\LocalDesktopStore\logs\`
- README banner + logo (transparent PNG with alpha channel)
- GitHub Actions release workflow — `workflow_dispatch`, framework-dependent `dotnet publish` for `win-x64`, ZIP + SHA-256 sidecar attached to the GitHub Release
