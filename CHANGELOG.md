# Changelog

All notable changes to LocalDesktopStore are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to [Semantic Versioning](https://semver.org/).

## v0.3.1 - 2026-08-12

### Changed
- Reconciled the active roadmap against the shipped implementation: update lifecycle, trust and supply-chain checks, catalog source controls, operability features, and Windows distribution paths are complete and removed from `ROADMAP.md`.
- Kept the Avalonia cross-platform port explicitly blocked until both Linux and macOS install paths can be smoke-tested on supported hosts.
- Refreshed the framework-dependent NuGet lockfiles after the current SDK removed stale `win-x64` runtime graphs.
- Synchronized product metadata, release-script defaults, HTTP user-agent versions, the README badge, and the in-app version footer to `0.3.1`.

## v0.3.0 - 2026-08-03

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
- **Post-install card actions** (X8) — per-repository preferences can launch the app after an install/update and invoke Windows' pointer-free `pintotaskbar` shell verb for taskbar pinning; unsupported targets are reported without failing the installation.
- **Custom installer arguments** (X9) — per-card MSI / Inno / NSIS / EXE overrides use Windows command-line quoting, are passed through `ProcessStartInfo.ArgumentList`, and persist in `installed.json` so updates reuse them.
- **Verified download cache** (X10) — sidecar-verified assets are keyed by owner/repo, version, and SHA-256 under the downloads root; valid hits skip the network download, while corrupt or unavailable entries fall back safely.
- **In-process artifact-handler host** (L2) — classification and install / uninstall / run lifecycle paths now resolve through one explicitly bundled `IArtifactHandler` registry. Velopack full-package payloads are recognized and refused as non-standalone installers, while AppImage support is guarded to Linux; no remote plugin loading exists.
- **Headless CLI** (L4) — `--install`, `--uninstall`, `--run`, `--refresh`, `--list`, `--version`, `--help`, and optional `--json` output share the GUI service layer without constructing a WPF window. Publisher changes remain fail-closed without interactive approval.
- **Opt-in GitHub Search discovery** (L5) — an explicit topic search probes up to 50 star-sorted repositories for supported release assets and labels results as uncurated. Search-discovered installs require a preconfigured, exact Authenticode publisher thumbprint for that `owner/repo`; archive/package handoffs are refused.
- **Runtime localization** (L6) — user-facing WPF text now comes from `Strings.resx` with an optional Spanish satellite resource, an English default, and an in-app System default / English / Español culture switch persisted in settings and catalog exports.
- **Enterprise MSI deployment** (L7) — WiX 5 now builds separately validated unsigned x64 per-user and per-machine packages, with isolated install roots, silent GPO/Intune commands, and a machine-scope DPAPI seed path for preconfigured GitHub owners and PATs.
- **OSV advisory checks** (L8) — an opt-in refresh setting queries OSV.dev's GitHubReleases ecosystem for each discovered repository, reports bounded results on cards, and keeps network failures informational so discovery and installation remain available.
- **Velopack self-update** (L9) — Velopack-installed copies can check the public GitHub release channel from File → Check for updates, download the latest package, and restart into it; raw ZIP and WiX MSI installs continue to report their supported manual update path.

### Changed
- Refreshes now use a lockfile aligned with the framework-dependent project target, so locked restores no longer carry a stale `win-x64` runtime graph.
- Velopack release packaging clears only its own stale channel artifacts before rebuilding, keeping the published feed aligned with the uploaded packages.

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

## Roadmap archive — 2026-08-10 — ROADMAP.md

<details>
<summary>Original roadmap snapshot</summary>

```markdown
# LocalDesktopStore Roadmap

_Last revised 2026-04-25. Reconciled from research-driven competitive sweep — UniGetUI, Scoop, WinGet, Chocolatey, RuckZuck, GitHub Store, Patch My PC, Velopack, Microsoft Trusted Signing, DSC v3, Avalonia Parcel, plus the live Windows CVE feed. Every item below is sourced; sources are listed in the Appendix._

## State of the repo

- **Today (v0.2.1)**: WPF / .NET 9 catalog UI sourcing apps from one or more GitHub accounts. Asset classifier routes MSI / Inno / NSIS / generic EXE / portable ZIP. Install-state detection runs as a registry diff across `HKLM`, `HKLM\WOW6432Node`, and `HKCU` uninstall keys. SHA-256 sidecar verification runs before the installer fires. Activity log + crash log on disk. **Update lifecycle is live**: semver-aware update detection on refresh, "Update all" sequential bulk update, ETag-based conditional refresh (304s don't count against rate limit). N4 is live: the settings drawer edits extra owners and hidden repos, and cards can be hidden from the catalog without JSON edits. The repo is local-build-only: no GitHub Actions, Dependabot, or Renovate.
- **Hard constraints**: MIT, framework-dependent `net9.0-windows` only, no MVVM toolkit, no third-party UI library, no telemetry, no auto-elevation, Catppuccin Mocha aesthetic, sibling visual UX to LocalChromeStore.
- **Closest competitor**: **UniGetUI 2026.1.6** (Devolutions, ~50 MB, MIT) — unifies WinGet/Scoop/Chocolatey/Pip/Npm/.NET Tool/PowerShell Gallery in one GUI [#1]. We are deliberately narrower than UniGetUI: GitHub-Releases-only, single source of truth, no public catalog dependency. The same shape exists in **GitHub Store** (Compose Multiplatform, OpenHub-Store) but with a fundamentally different architecture (Compose, no install-state pinning) [#2][#3].

## Cross-cutting themes

The Now / Next / Later tiers below all map back to one of these themes:

- **T1 · Update lifecycle** — detect, surface, and apply updates without losing the surgical install/uninstall guarantees we ship today.
- **T2 · Trust & supply chain** — every artifact installed is verifiable (hash, signature, publisher) and every release we ship is itself verifiable (SLSA / reproducible).
- **T3 · Source surface** — make it easier to declare *what* is in the catalog (multi-org, hidden repos, topic filters) without writing JSON by hand.
- **T4 · Operability** — accessibility, localization, scheduling, and headless modes so the app is usable beyond a single English-speaking sysadmin clicking buttons.
- **T5 · Distribution** — winget-pkgs export, MSIX/`.appinstaller`, signed installer, GPO/Intune-aware deployment so LDS itself ships well into other people's machines.
- **T6 · Cross-platform** — Avalonia path; Linux + macOS only when it carries weight, never as a marketing checkbox.

---

## Shipped — v0.2.0 (2026-04-25)

Slice B — update lifecycle headline pass. Builds on the v0.2.0-alpha groundwork (N5 + N7 + N8 + N13).

- **N1 · Update detection on refresh** — `Services/VersionCompare.cs` (semver-ish), wired through `IsUpdateAvailable` / `InstallButtonLabel`. Manifest is reloaded before the card collection rebuild so out-of-band installs surface immediately.
- **N2 · "Update all" toolbar action** — `MainViewModel.UpdateAllCommand` runs a sequential `await` loop over outdated cards. Surfaces only when at least one app is outdated; labels itself with the count.
- **N3 · ETag-based refresh** — `Services/EtagCachingHandler.cs` (DelegatingHandler) injected into Octokit via `HttpClientAdapter`. 304 short-circuit replays cached body, doesn't count against rate limit. Per-token cache so PAT rotation invalidates implicitly. Activity log reports the hit/miss tally per discover.

## Shipped — v0.2.0-alpha (2026-04-25)

Slice A groundwork pass — additive only, sets up the safety net N1-N3 + N6 + N9 build on.

- **N5 · Icon fallback chain** — `logo.png` → `banner.png` → `icon.png` → `opengraph.githubassets.com`.
- **N7 · Schema-versioned `installed.json` migrator** — `IInstalledManifestMigrator` chain; refuses forward-rolled files instead of silently dropping fields.
- **N8 · Reproducible builds + SourceLink** — `DotNet.ReproducibleBuilds 2.0.2` + `Microsoft.SourceLink.GitHub 8.0.0`, `ContinuousIntegrationBuild` + `EmbedUntrackedSources` + `PublishRepositoryUrl`.
- **N13 · Dep-scanning CI** — Dependabot weekly (NuGet + Actions), security-patch auto-merge workflow, OSV-Scanner gate in `release.yml`.

---

## Now — v0.2.2 (next patch)

Items here close the remaining gaps from the original v0.2.0 scope.

---

## Next — v0.3.0 / v0.4.0 (targeted within 6 months)

Items here need new architecture or bigger UX surface.

---

## Later — v0.5.0+ (architectural)

These items reshape the project. Don't start until Now / Next have shipped.

---

---

## Rejected — explicitly off-roadmap

These items have been considered and ruled out. Listed so they don't get silently re-pitched.

### R1 · WMI-based installed-app enumeration

Slow, requires WMI service running, surfaces UAC prompts on some lockdowns, and doesn't see HKCU. Registry diff already gives us the data faster and cheaper. _Anti-pattern called out in the original build prompt._

### R2 · Auto-elevation to admin "just in case"

Per-machine MSI requires it; per-user MSI doesn't. We never assume. Already in the Anti-Roadmap. Reaffirmed against [CVE-2025-59287](https://www.esentire.com/security-advisories/critical-windows-vulnerability-exploited-cve-2025-59287) (msiexec abuse) and [CVE-2025-21275](https://www.sentinelone.com/vulnerability-database/cve-2025-21275/) (AppX EoP) — minimum-privilege is the right default.

### R3 · Mandatory paid feature tier

Chocolatey's pay-walling of `choco sync` is a significant complaint vector [#46][#50]. We will not. _MIT, free, no Pro tier ever._

### R4 · Self-contained .NET runtime as default publish target

Doubles the artifact (~80 MB vs ~660 KB), encourages stale runtime + missed CVE patches, and contradicts the user's [stack-csharp.md](memory/stack-csharp.md) convention of framework-dependent publish. Optional self-contained build only when an enterprise asks for it explicitly.

### R5 · Bundled MVVM toolkit (CommunityToolkit.Mvvm, Prism, etc.)

`ViewModelBase` + `RelayCommand` + `AsyncRelayCommand` is enough — already proven in LocalChromeStore. Ban remains.

### R6 · Bundled UI control library (MaterialDesign, MahApps, Telerik, Syncfusion)

Catppuccin Mocha lives in a single `Themes/DarkTheme.xaml`. Adding a control library would invalidate the theme and 5x the install size. Ban remains.

### R7 · Browser-extension parity (CRX install paths)

That's [LocalChromeStore](https://github.com/SysAdminDoc/LocalChromeStore)'s job. The desktop one stays focused on desktop binaries.

### R8 · Driver / kernel-level installer support

Drivers require EV-cert + Microsoft attestation [#28]. Out of scope; users should use Windows Update + INF directly.

### R9 · Default-on telemetry of any kind

See U2 — the only path forward is explicit opt-in with a published schema, never default. The Anti-Roadmap "no telemetry" remains the default reading.

### R10 · Auto-decoding of Inno / NSIS installers to inspect contents

Tempting for portable-style install of installer-only releases, but it's brittle, breaks per-installer-version, and replaces a clean install path with a guess. We'd rather classify cleanly and run the installer the way the publisher intended.

### R11 · Mobile (Android / iOS) port

That's [LocalAndroidStore](https://github.com/SysAdminDoc/LocalAndroidStore)'s job. LocalDesktopStore stays focused on Windows desktop binaries. Even after L1 (Avalonia), mobile remains a separate product track because it has a different install model (Play Store / App Store sideload), different UX, and different security model.

### R12 · Default test suite as a release gate

Per global CLAUDE.md: "no tests unless explicitly requested". v0.1.0 ships none. Re-evaluation lives at U6 — but until the user explicitly opts in, the release gate is build + smoke-test, not unit/integration coverage.

---

## Anti-roadmap (preserved from v0.1.0)

Reaffirmed:

- **No silent admin elevation.** UAC is the installer's call.
- **No unattended catalog updates** without manual refresh — drift is louder than silent surprises.
- **No bundled .NET runtime.** Framework-dependent only.
- **No telemetry by default.** See U2 / R9 above.
- **No MVVM-toolkit dependency.** See R5.
- **No third-party UI library.** See R6.

---

## Appendix — sources

[#1] UniGetUI README & feature list (Devolutions stewardship, v2026.1.6, Avalonia experimental port, .NET 10 build pipeline) — <https://github.com/Devolutions/UniGetUI>, <https://unigetui.com/>, <https://www.neowin.net/software/unigetui-202615/>.
[#2] GitHub Store (OpenHub-Store) project page — <https://github-store.org/>.
[#3] GitHub Store source repo — <https://github.com/OpenHub-Store/Github-Store>; coverage at <https://windowsnews.ai/article/github-store-transforms-releases-into-app-discovery-platform-for-windows-users.405427>.
[#4] Patch My PC Home Updater (free) — <https://patchmypc.com/product/home-updater/>; release notes <https://patchmypc.com/release-notes/production-release/home-updater-releases/>.
[#5] GitHub conditional requests / ETag rate-limit guidance — <https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api>; reference impl <https://github.com/bored-engineer/github-conditional-http-transport>; Jamie Magee blog <https://jamiemagee.co.uk/blog/making-the-most-of-github-rate-limits/>.
[#6] GitHub community discussion #189255 — working with the API rate limit — <https://github.com/orgs/community/discussions/189255>.
[#7] UniGetUI / WingetUI issue tracker hot threads — <https://github.com/marticliment/UniGetUI/issues/701> (Chocolatey-to-Winget migration), <https://github.com/marticliment/UniGetUI/discussions/1444> (system Chocolatey detection).
[#8] SmartScreen reputation (Microsoft Learn) — <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation>.
[#9] Azure Artifact Signing (formerly Trusted Signing) FAQ — <https://learn.microsoft.com/en-us/azure/artifact-signing/faq>; KB5022661 <https://support.microsoft.com/en-us/topic/kb5022661>.
[#10] DotNet.ReproducibleBuilds 2.0.2 (NuGet) — <https://www.nuget.org/packages/DotNet.ReproducibleBuilds/>; repo <https://github.com/dotnet/reproducible-builds>.
[#11] dotnet/designs — Reproducible Builds — <https://github.com/dotnet/designs/blob/main/accepted/2020/reproducible-builds.md>.
[#12] Meziantou — Creating reproducible builds in .NET — <https://www.meziantou.net/creating-reproducible-build-in-dotnet.htm>.
[#13] Practical supply-chain security 2026 (Sigstore + SLSA + reproducible) — <https://kawaldeepsingh.medium.com/practical-software-supply-chain-security-2026-sboms-signing-slsa-reproducible-builds-a-0416cfac32dc>.
[#14] OpenSSF Sigstore overview — <https://openssf.org/blog/2023/11/21/sigstore-simplifying-code-signing-for-open-source-ecosystems/>.
[#15] WPF accessibility part 4 — Microsoft Learn — <https://learn.microsoft.com/en-us/archive/blogs/winuiautomation/common-approaches-for-enhancing-the-programmatic-accessibility-of-your-win32-winforms-and-wpf-apps-part-4-wpf>.
[#16] WPF accessibility improvements — dotnet/Documentation — <https://github.com/microsoft/dotnet/blob/main/Documentation/compatibility/wpf-accessibility-improvements.MD>.
[#17] WinGet manifest format — <https://learn.microsoft.com/en-us/windows/package-manager/package/manifest>.
[#18] WingetCreate — <https://techcommunity.microsoft.com/blog/educatordeveloperblog/wingetcreate-keeping-winget-packages-up-to-date/4037598>.
[#19] winget-pkgs Authoring guide — <https://github.com/microsoft/winget-pkgs/blob/master/doc/Authoring.md>.
[#20] Scoop architecture, buckets, manifests, import/export — <https://github.com/ScoopInstaller/scoop>; tutorial <https://mrotaru.co.uk/blog/windows-package-manager-scoop/>; comparison <https://dev.to/bowmanjd/chocolatey-vs-scoop-package-managers-for-windows-2kik>.
[#21] App Installer install/update — Microsoft Learn — <https://learn.microsoft.com/en-us/windows/msix/app-installer/install-update-app-installer>.
[#22] App Installer file (.appinstaller) creation — <https://learn.microsoft.com/en-us/windows/msix/app-installer/create-appinstallerfile-vs>; troubleshooting <https://learn.microsoft.com/en-us/windows/msix/app-installer/troubleshoot-appinstaller-issues>.
[#23] WinGet COM API from C# — <https://github.com/marticliment/WinGet-API-from-CSharp>.
[#24] WinGet COM API spec — <https://github.com/microsoft/winget-cli/blob/master/doc/specs/#888%20-%20Com%20Api.md>.
[#25] winget-cli discussion #3953 — COM API documentation — <https://github.com/microsoft/winget-cli/discussions/3953>.
[#26] Trusted Signing GA + 2026 industry changes — <https://securityboulevard.com/2026/01/how-to-set-up-azure-trusted-signing-to-sign-an-exe/>; <https://www.ssl.com/faqs/which-code-signing-certificate-do-i-need-ev-ov/>.
[#27] Code signing on Windows with Azure Artifact Signing — Melatonin — <https://melatonin.dev/blog/code-signing-on-windows-with-azure-trusted-signing/>.
[#28] Code-signing options for Windows app developers — <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options>.
[#29] Velopack — installer + auto-update — <https://github.com/velopack/velopack>; docs <https://docs.velopack.io/>.
[#30] Avalonia Parcel — <https://avaloniaui.net/parcel>; macOS docs <https://docs.avaloniaui.net/docs/distribution-publishing/macos>.
[#31] Avalonia macOS packaging docs — <https://docs.avaloniaui.net/docs/distribution-publishing/macos>.
[#32] Avalonia Debian/Ubuntu packaging — <https://docs.avaloniaui.net/docs/deployment/debian-ubuntu>.
[#33] Avalonia 11.3.13 (NuGet) — <https://www.nuget.org/packages/Avalonia/>.
[#34] Sigstore — Software Signing for Everybody — <https://www.researchgate.net/publication/365216788_Sigstore_Software_Signing_for_Everybody>.
[#35] WinGet Configuration / DSC v3 — <https://learn.microsoft.com/en-us/windows/package-manager/configuration/>.
[#36] Microsoft Learn — winget overview — <https://learn.microsoft.com/en-us/windows/package-manager/winget/>.
[#37] RESX file format & best practices — <https://better-i18n.com/en/blog/resx-file-format/>.
[#38] WPF runtime localization — Keyhole Software — <https://keyholesoftware.com/dynamically-localizing-a-wpf-application-at-runtime/>.
[#39] Soluling — WPF localization — <https://www.soluling.com/Help/WPF/Index.htm>.
[#40] Group Policy 2026: AGPM EOL & Windows 11 25H2 templates — <https://hartiga.de/windows-server/group-policies-foundation/>; <https://4sysops.com/archives/new-windows-11-25h2-group-policy-settings/>.
[#41] Deploy Software with Group Policy (MSI & EXE) — <https://activedirectorypro.com/deploy-software-using-group-policy/>.
[#42] LTSC customization with GPOs — <https://wholsalekeys.com/customizing-windows-11-ltsc-group-policies-deployment-tools/>.
[#43] Live CVE feed — Windows installer & .NET attack surface — CVE-2025-21275 <https://www.sentinelone.com/vulnerability-database/cve-2025-21275/>; CVE-2025-59287 <https://www.esentire.com/security-advisories/critical-windows-vulnerability-exploited-cve-2025-59287>; CVE-2026-23666 (.NET DoS) <https://msrc.microsoft.com/update-guide/vulnerability>.
[#44] Velopack landing page — <https://velopack.io/>.
[#45] Velopack — migrating from Squirrel — <https://docs.velopack.io/migrating/squirrel>.
[#46] Chocolatey alternatives & complaints — <https://alternativeto.net/software/chocolatey/>; xda comparison <https://www.xda-developers.com/chocolatey-vs-winget-vs-scoop/>.
[#47] VS Code telemetry — <https://code.visualstudio.com/docs/configure/telemetry>; OpenTelemetry client apps <https://opentelemetry.io/docs/platforms/client-apps/>; .NET telemetry opt-out <https://learn.microsoft.com/en-us/dotnet/core/tools/telemetry>.
[#48] WinGet configure (background) — <https://woshub.com/winget-dsc-configure/>.
[#49] RuckZuck repo + ConfigMgr integration — <https://github.com/rzander/ruckzuck>; <https://ruckzuck.tools/>.
[#50] Windows package manager comparison (XDA) — <https://www.xda-developers.com/chocolatey-vs-winget-vs-scoop/>.
[#51] OSV.dev — open-source vulnerability database — <https://osv.dev/docs/#tag/api>.
```

</details>
