# Changelog

All notable changes to LocalDesktopStore are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to [Semantic Versioning](https://semver.org/).

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
