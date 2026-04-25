# LocalDesktopStore Roadmap

_Last revised 2026-04-25. Reconciled from research-driven competitive sweep — UniGetUI, Scoop, WinGet, Chocolatey, RuckZuck, GitHub Store, Patch My PC, Velopack, Microsoft Trusted Signing, DSC v3, Avalonia Parcel, plus the live Windows CVE feed. Every item below is sourced; sources are listed in the Appendix._

## State of the repo

- **Today (v0.2.0-alpha)**: WPF / .NET 9 catalog UI sourcing apps from one or more GitHub accounts. Asset classifier routes MSI / Inno / NSIS / generic EXE / portable ZIP. Install-state detection runs as a registry diff across `HKLM`, `HKLM\WOW6432Node`, and `HKCU` uninstall keys. SHA-256 sidecar verification runs before the installer fires. Activity log + crash log on disk. Slice-A groundwork landed: icon fallback chain (logo→banner→icon→OG), schema-versioned `installed.json` migrator, reproducible builds + SourceLink, Dependabot + OSV-Scanner CI. ~3,042 LOC, two direct dependencies (Octokit 13.0.1 + Microsoft.Win32.Registry 5.0) plus build-time `DotNet.ReproducibleBuilds` + `Microsoft.SourceLink.GitHub`.
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

## Shipped — v0.2.0-alpha (2026-04-25)

Slice A groundwork pass — additive only, sets up the safety net N1-N3 + N6 + N9 build on.

- **N5 · Icon fallback chain** — `logo.png` → `banner.png` → `icon.png` → `opengraph.githubassets.com`.
- **N7 · Schema-versioned `installed.json` migrator** — `IInstalledManifestMigrator` chain; refuses forward-rolled files instead of silently dropping fields.
- **N8 · Reproducible builds + SourceLink** — `DotNet.ReproducibleBuilds 2.0.2` + `Microsoft.SourceLink.GitHub 8.0.0`, `ContinuousIntegrationBuild` + `EmbedUntrackedSources` + `PublishRepositoryUrl`.
- **N13 · Dep-scanning CI** — Dependabot weekly (NuGet + Actions), security-patch auto-merge workflow, OSV-Scanner gate in `release.yml`.

---

## Now — v0.2.0 (next 3 releases)

Items here close the largest known gaps in v0.1.0. Each is in scope for the next minor cycle.

### N1 · Update detection on refresh `[T1]`

Compare each card's pinned `InstalledApp.Version` against the latest GitHub release tag and flip `IsUpdateAvailable` accordingly. Surface as the existing yellow `Update available` badge already in [src/LocalDesktopStore/Views/AppCardView.xaml](src/LocalDesktopStore/Views/AppCardView.xaml). UniGetUI [#1], Patch My PC [#4], GitHub Store [#3] all do this; doing it ourselves is table stakes.
- **Source:** UniGetUI features [#1], Patch My PC Home Updater [#4], GitHub Store [#2].
- **Effort:** 2/5 — version comparison + view-model wiring already exists; just needs the refresh path to actually recompute `_installed!.Version` against `Info.LatestVersion` after each `RefreshAsync`.
- **Risk:** Tag formats vary (`v1.2.3`, `1.2.3-rc1`, date-driven). Use `SemanticVersion`-aware compare with fallback to ordinal.

### N2 · "Update all" command + per-card "Update" button label `[T1]`

After N1 ships, add an explicit `Update all` toolbar action and replace the "Reinstall" label on outdated cards with "Update to vX.Y.Z" (already partly wired in `AppCardViewModel.InstallButtonLabel`).
- **Source:** UniGetUI bulk operations [#1], Patch My PC one-click outdated filter [#4].
- **Effort:** 2/5.
- **Dependencies:** N1.

### N3 · ETag-based catalog refresh `[T1, T6]`

Refresh today calls `client.Repository.GetAllForUser` + `Release.GetLatest` for every owner on every refresh. With one PAT and a dozen repos that's ~25 requests; without a PAT it eats half the 60 req/h public budget. Switch to conditional `If-None-Match` requests via Octokit's response-header capture or a thin `HttpClientHandler`. 304s do not count against rate limit per [#5].
- **Source:** [#5] GitHub conditional requests, [#6] best practices for REST API.
- **Effort:** 3/5 — Octokit 13 surfaces `ApiResponse<T>` with headers; needs a small response cache keyed by `(url, token)`.
- **Risk:** Per-token cache (cache invalidates on PAT rotation, per [#5]).

### N4 · Settings UI for `ExtraOwners` and `HiddenRepos` `[T3]`

Currently JSON-only edit. Add a multi-org list editor in the settings drawer + per-card "Hide this app" overflow menu. UniGetUI has had this for years [#1]; it's the most-asked feature shape on the WingetUI repo [#7].
- **Source:** UniGetUI feature list [#1], discussion #1444 [#7].
- **Effort:** 3/5 — XAML and view-model only, no service-layer changes.

### N6 · Verify Authenticode signature and publisher pin `[T2]`

After hash verification but before invoking the installer, call `WinVerifyTrust` (P/Invoke) on the downloaded `.exe` / `.msi` to confirm the file is signed and the certificate is in the Trusted Root Program. Pin the cert thumbprint or subject to the install record on first install; on re-install or update, refuse if the cert subject changed without explicit user approval. Mirrors the LocalAndroidStore signature pin model.
- **Source:** SmartScreen reputation guidance [#8], SmartScreen + Authenticode hierarchy [#9].
- **Effort:** 3/5 — `WinVerifyTrust` P/Invoke is well-documented; cert pinning fits naturally into `InstalledApp.UninstallRegistryKey` neighbours.
- **Risk:** Some repos legitimately rotate signing identity (Sectigo OV → Trusted Signing). Surface as a warning prompt, not a hard block.
- **Dependencies:** N7 (shipped — `InstalledApp` schema bump for `PublisherCertThumbprint` ships as a v1→v2 migrator).

### N9 · SLSA L2 build provenance attestations `[T2]`

GitHub Actions has native attestation support via `actions/attest-build-provenance@v2`. Each release ZIP gets a Sigstore-signed in-toto provenance artifact attached, anchored to the `vX.Y.Z` tag commit SHA. Free; no certificate purchase. Supplements the existing SHA-256 sidecar.
- **Source:** [#13] practical-software-supply-chain (Medium), [#14] Sigstore overview, [#11] dotnet/designs.
- **Effort:** 2/5 — workflow YAML edit only.
- **Dependencies:** N8 (shipped).

### N10 · Per-card error inline + crash-log link `[T4]`

The card already has an `ErrorMessage` field but install failures only land in the activity log. Surface the error on the card itself with a "View crash log" link that opens `%LOCALAPPDATA%\LocalDesktopStore\logs\` in Explorer. Don't silently swallow — already a hard rule in the build prompt; just needs the UX to surface it.
- **Source:** existing v0.1.0 anti-pattern rule; UniGetUI per-package error UI [#1].
- **Effort:** 2/5.

### N11 · `AutomationProperties.Name` + `LiveSetting` for the activity log `[T4]`

Every interactive control gets a deliberate `AutomationProperties.Name` (icons currently announce as "custom"). The activity log gets `AutomationProperties.LiveSetting="Polite"` so Narrator announces install / uninstall events without needing focus. .NET 4.8 already removed collapsed/hidden elements from the UIA tree, so only positive work is required.
- **Source:** [#15] WPF accessibility — Microsoft Learn, [#16] .NET accessibility improvements MD.
- **Effort:** 2/5.

### N12 · `winget validate`-able manifest export `[T5]`

Per-card "Export winget manifest" action that emits a v1.6 schema-compliant YAML at `manifests/<first-letter>/<owner>/<repo>/<version>/<owner>.<repo>.yaml` matching the layout `winget-pkgs` requires. User can then submit upstream by hand or via `wingetcreate submit`. Doesn't auto-publish — that's user-driven.
- **Source:** [#17] winget package manifest format, [#18] wingetcreate technique, [#19] winget-pkgs Authoring.md.
- **Effort:** 3/5 — write a small YAML serializer; capture installer type, args, scope, switches.
- **Novelty:** This is leapfrog — no other GUI catalog ships winget-pkgs export today.

---

## Next — v0.3.0 / v0.4.0 (targeted within 6 months)

Items here need new architecture or bigger UX surface.

### X1 · Catppuccin Latte light theme + system-accent option `[T4]`

Single `LightTheme.xaml` with the Catppuccin Latte palette; runtime swap by replacing `App.Resources.MergedDictionaries[0]`. Optional system-accent override (`SystemParameters.WindowGlassBrush`). Already on the v0.1.0 ROADMAP — keep it; UniGetUI's recent runtime-theme switch [#1] confirms it's table stakes.
- **Source:** [#1] UniGetUI release notes; existing ROADMAP commit.
- **Effort:** 3/5 — palette swap is easy, contrast pass on every status badge / card border is the real work.

### X2 · Scheduled background update check `[T1]`

Tray-resident update worker that polls every N hours (default 6 h) using ETag refresh from N3. Notifies via toast (`Microsoft.Toolkit.Uwp.Notifications`-free path: pure Win32 `Shell_NotifyIcon` + WinRT `XmlDocument`). Persists between launches via `Task Scheduler` registration so it survives close. Patch My PC ships this; UniGetUI's tray surfaces it [#1][#4].
- **Source:** [#1] UniGetUI tray UX, [#4] Patch My PC scheduling.
- **Effort:** 4/5 — tray + scheduler is real Win32 work.
- **Risk:** Anti-roadmap explicitly bans "no auto-elevation"; the scheduler entry runs as the user, never SYSTEM.

### X3 · Bulk operations + selection mode `[T1]`

Card grid gains a checkbox per card and a toolbar revealing "Install selected" / "Update selected" / "Uninstall selected" once at least one is checked. Sequential install loop with single status banner (don't fan out parallel msiexec — the OS serializes anyway and it confuses error attribution).
- **Source:** [#1] UniGetUI bulk install/update/uninstall, [#4] Patch My PC bulk uninstaller.
- **Effort:** 3/5.
- **Dependencies:** N4 view-model patterns.

### X4 · Catalog file import / export (`.lds.json`) `[T1, T3]`

`File → Export catalog` writes a JSON containing chosen owners + per-app overrides + currently-installed version pins. `File → Import catalog` is the round-trip — useful for replicating a sysadmin's loadout to a fresh box. Mirrors Scoopfile [#20] and UniGetUI's package list export [#1].
- **Source:** [#20] Scoop import/export, [#1] UniGetUI export.
- **Effort:** 3/5.

### X5 · MSIX and `.msixbundle` install / `.appinstaller` URL acceptance `[T1, T5]`

`AssetClassifier` learns `*.msix` / `*.msixbundle` → `Add-AppxPackage` (or COM equivalent), and `.appinstaller` → `Invoke-Expression "ms-appinstaller:?source=$url"`. `Add-AppPackage` requires the cert in the trust store — surface a clear error if not, don't auto-import.
- **Source:** [#21] App Installer install/update, [#22] App Installer file format.
- **Effort:** 4/5 — needs Windows 10 1709+ MSIX path + `WindowsPackageManager` projection or `Add-AppxPackage` PowerShell shell-out.
- **Risk:** [CVE-2025-21275](https://www.sentinelone.com/vulnerability-database/cve-2025-21275/) is an `AppX` installer EoP — keep our process at standard user; `Add-AppxPackage` itself runs as the user.

### X6 · WinGet COM API as detection oracle `[T1, T2]`

Use `Microsoft.WindowsPackageManager.ComInterop` (`PackageManager.GetPackageCatalogs`) to ask WinGet what *it* thinks is installed before doing our registry diff — gives us a second authoritative source and lets us cross-check `InstalledApp.UninstallCommand` against winget's own metadata. Useful for the "this app installed itself outside our store" case.
- **Source:** [#23] marticliment/WinGet-API-from-CSharp, [#24] microsoft/winget-cli COM spec, [#25] discussion #3953.
- **Effort:** 4/5 — boilerplate for the elevated/standard factory split is awkward; `WindowsPackageManagerElevatedFactory()` crashes when used from a standard process [#23]. Wrap it carefully.

### X7 · Authenticode-signed LDS releases via Azure Trusted Signing `[T2, T5]`

Sign every release artifact with `azure/trusted-signing-action@v0.5+`. ~$10/mo (per [#9]); GA as of April 2026 [#26]. Removes the SmartScreen "Unknown Publisher" warning on first install. Ties to N6's signature-pin feature — we eat our own dogfood.
- **Source:** [#9] Azure Artifact Signing FAQ, [#26] Trusted Signing GA notes, [#27] melatonin.dev walkthrough, [#28] code signing options for Windows app developers.
- **Effort:** 3/5 — Azure subscription + identity validation (au10tix) is the real cost. CI YAML is small.
- **Risk:** SmartScreen reputation is now per-file-hash, not per-cert (as of 2024 Trusted Root Program update [#8]); EV no longer grants instant reputation. Plan accordingly — N9 SLSA + signed ZIP + 6 weeks of downloads is a more honest path than buying EV.

### X8 · "Run after install" + "Pin to taskbar" optional toggles `[T4]`

Per-card preference. Run-after-install is in `AppSettings` already (carried over from LocalChromeStore template) — wire it. Pin-to-taskbar via `IShellLink` + `User32.dll PinToTaskbar` (deprecated path; prefer `Verb=pintotaskbar` via `IContextMenu`).
- **Source:** standard Windows pattern; UniGetUI offers similar [#1].
- **Effort:** 3/5.

### X9 · Custom installer args per app `[T3]`

Per-card override JSON for the silent-install switches (UniGetUI's most-popular customization [#1]). Defaults stay sane; power user can force `INSTALLDIR=...` on an MSI or `/D=...` on an NSIS installer. Persist into `installed.json` so subsequent updates inherit.
- **Source:** [#1] UniGetUI custom installation options.
- **Effort:** 3/5.

### X10 · Pre-install download cache + delta on update `[T1]`

Today every install re-downloads. Cache in `%LOCALAPPDATA%\LocalDesktopStore\downloads\` keyed by `(repo, version, sha256)`; on update, if the new asset's `<asset>.sha256.txt` already matches a cached blob, skip the re-download. Velopack's delta concept [#29] is overkill for v0.4 — start with whole-asset caching.
- **Source:** [#29] Velopack delta packages.
- **Effort:** 2/5.

---

## Later — v0.5.0+ (architectural)

These items reshape the project. Don't start until Now / Next have shipped.

### L1 · Avalonia 11 cross-platform port `[T6]`

Port the UI to Avalonia 11.3+; keep the C# service layer untouched. Linux gets `.deb` install handling (`apt install ./pkg.deb`), macOS gets `.dmg` mount + drop into `/Applications`, both via Avalonia Parcel [#30]. Carry the registry diff pattern across only as a `IInstallStateProvider` interface — Linux uses `dpkg -l`, macOS uses `mdls`. Don't ship until both Linux and macOS install paths are smoke-tested.
- **Source:** [#30] Avalonia Parcel, [#31] Avalonia macOS, [#32] Avalonia Debian/Ubuntu, [#33] Avalonia 11.3.
- **Effort:** 5/5 — cross-platform install is its own project. Likely a v1.0 milestone.

### L2 · Plugin system for installer kinds `[T1, T3]`

`AssetClassifier` and `InstallService` become plugin host: each plugin is a single C# class implementing `IArtifactHandler { CanHandle(asset, peScan); Install(...); Uninstall(...); Run(...) }`. Today's MSI / Inno / NSIS / Generic / Portable become bundled plugins; MSIX (X5) is a plugin; Velopack-style (`.nupkg.full`) is a plugin; `.appimage` (Linux post-L1) is a plugin.
- **Source:** Scoop's manifest plugin model [#20], Velopack's framework-agnostic install [#29].
- **Effort:** 4/5.
- **Risk:** Plugins are an attack surface — restrict to in-process only, no remote loads, signed & shipped with releases.

### L3 · Repo signature-mirror catalog `[T2]`

Self-hosted GitHub Pages site that mirrors the user's curated catalog as a single signed `catalog.json` + `catalog.json.sig`. Other instances of LDS can subscribe to this catalog instead of the GitHub API directly. Solves the fresh-machine cold-start cost and gives offline catalog updates. Sigstore keyless signing on the catalog.
- **Source:** [#13] practical supply-chain blog, [#34] OpenSSF Sigstore.
- **Effort:** 4/5.
- **Novelty:** leapfrog.

### L4 · Headless / CLI mode `[T4]`

`LocalDesktopStore.exe --install <owner>/<repo>` from PowerShell. Same install / uninstall / refresh primitives as the GUI; useful from PowerShell DSC v3 scripts and Intune devicePrep [#35]. Mirrors WinGet's CLI / GUI symmetry.
- **Source:** [#35] WinGet Configuration / DSC v3, [#36] Microsoft Learn — winget overview.
- **Effort:** 3/5 — share `Services/`, swap views for `System.CommandLine`.

### L5 · Catalog discovery via GitHub Search API (not just user repos) `[T3]`

Optional opt-in: instead of "list repos for owner X", search for repos with topic `windows-app` AND a release with a Windows-installer asset, sorted by stars / freshness. Mirrors GitHub Store's discovery model [#2][#3]. Stays opt-in because it changes the trust model from "I curated my own GitHub" to "anyone on GitHub".
- **Source:** [#2] GitHub Store, [#3] OpenHub-Store/Github-Store.
- **Effort:** 3/5.
- **Risk:** Trust boundary; require explicit signature pinning per app for search-discovered installs.

### L6 · Localization (RESX + satellite assemblies) `[T4]`

Move user-facing strings from XAML to `Strings.resx` + `Strings.{lang}.resx`. Runtime culture switch via `Thread.CurrentThread.CurrentUICulture` and a `LocalizationProvider`. Default English; community translations via PR. ResXManager extension for editing.
- **Source:** [#37] better-i18n.com RESX guide, [#38] WPF runtime localization, [#39] Soluling.
- **Effort:** 4/5 — every XAML string needs `x:Static` or markup-extension binding.

### L7 · Group Policy / Intune-deployable LDS `[T5]`

Ship LDS as both a per-user MSI and a per-machine MSI. Per-machine MSI usable from a GPO ADMX or Intune Win32 app. Documented enterprise install with pre-seeded `settings.json` (GitHub PAT via DPAPI). AGPM is dead 2026-04-14 [#40]; orgs are pushing to Intune anyway.
- **Source:** [#40] Group Policy / AGPM EOL, [#41] Active Directory Pro deploy via GPO, [#42] LTSC customization with GPOs.
- **Effort:** 5/5 — proper MSI authoring (WiX / Advanced Installer), DPAPI-protected token, per-machine vs per-user dual SKU.

### L8 · Inline CVE / advisory feed per app `[T2]`

For each card, hit OSV.dev (`/v1/query` with `package: { ecosystem: "GitHubReleases", name: "<owner>/<repo>" }`) and surface the count of open advisories at the card. Doesn't block install — just informs.
- **Source:** [#51] OSV.dev API; [#43] CVE feed pattern (CVE-2025-21275 et al).
- **Effort:** 3/5.
- **Risk:** Network call to a third party — opt-in default per the Anti-Roadmap.

### L9 · Self-update via Velopack `[T1, T5]`

Replace the "manually download the next ZIP" flow with `vpk pack` + Velopack's update channel. ~2-second seamless update + relaunch with no UAC [#29][#44]. Squirrel migration is straightforward [#45].
- **Source:** [#29] Velopack repo, [#44] velopack.io, [#45] Velopack Squirrel migration.
- **Effort:** 3/5.
- **Risk:** Velopack adds ~5 MB; weigh against the manual ZIP+sha256 path that already works.

---

## Under Consideration

These items are real value with unresolved fit-or-philosophy questions. Listed so they don't quietly accumulate as backlog cruft.

### U1 · VirusTotal scan integration before install

Scoop has it [#20]; Patch My PC verifies via VirusTotal [#4]; Chocolatey scans community packages [#46]. Flag: requires hashing → uploading hashes to VT → reading the verdict. Mostly a network call to a third party, which the project tries to avoid by default. **Decision pending** — ship as opt-in only, gated by an `AppSettings.UseVirusTotal` toggle, never default-on.

### U2 · Opt-in OpenTelemetry exporter

Crash counts, install-success rates, classifier accuracy. VS Code's three-tier model (crash / error / usage) [#47] is the canonical opt-in. The Anti-Roadmap currently says "no telemetry" — but "no telemetry" usually means "no telemetry on by default", which a tightly-scoped opt-in pipeline doesn't violate. **Decision pending** — would only ship after a clear privacy schema is published in this repo.

### U3 · DSC v3 export of installed apps `winget configure export`

The `winget configure export` flow [#35][#48] writes a YAML that recreates the machine state. Mirroring it from LDS makes a sysadmin's "set up new machine" trip a one-command playback. Niche audience. **Decision pending** — fits L4 (headless mode) more than the GUI; revisit when L4 lands.

### U4 · Plugin distribution channel

Once L2 (plugin system) ships, do plugins live in this repo's `plugins/` dir, or in a discoverable channel? RuckZuck has a server-mediated repo [#49]; Scoop has bucket repos [#20]. **Decision pending** — depends on whether L2 ships.

### U6 · Test harness for `InstallService`

The user's [stack-csharp.md](memory/stack-csharp.md) and global CLAUDE.md both stand on "no tests unless explicitly requested" — and v0.1.0 ships zero tests by design. But: registry-diff detection and the byte-scan classifier in `AssetClassifier.RefineFromFile` would benefit from snapshot tests against fixture installers (Inno-built, NSIS-built, Wix-built) to prevent silent regressions on small refactors. **Decision pending** — would need user sign-off before adding any test project, since it's a hard "no" by default.

### U5 · Microsoft Store listing for LDS itself

Microsoft Store distribution skips SmartScreen warnings entirely [#9]. But the Store rejects MSI/EXE installers signed with our own cert [#22]. So the Store path means re-signing as an MSIX with a Microsoft-supplied subject — a forking concern with the GitHub-released ZIP. **Decision pending** — only worth it if L9 (Velopack) doesn't handle the SmartScreen story.

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

That's [LocalAndroidStore](https://github.com/SysAdminDoc/LocalAndroidStore)'s job. LocalDesktopStore stays focused on Windows desktop binaries. Even after L1 (Avalonia), mobile is out of scope — different install model (Play Store / App Store sideload), different UX, different security model.

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
