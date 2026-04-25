# LocalDesktopStore Roadmap

## Shipped

### v0.1.0 — 2026-04-25
- WPF / .NET 9 store UI, Catppuccin Mocha
- GitHub-sourced discovery (Octokit 13.x), topic filter, optional PAT
- Asset classification: MSI / Inno / NSIS / generic / portable
- Install + uninstall + run handlers per kind
- Registry-diff install-state detection
- SHA-256 sidecar verification
- Settings drawer, activity log, crash log
- GitHub Actions release pipeline (framework-dependent ZIP + sha256)

## Planned

### v0.2.0 — Auto-update
- Compare local installed version against latest release tag on refresh, surface an "Update available" badge + "Update all" button
- WinGet manifest export — write a `manifests/` tree under each repo so apps can also be `winget install`-able
- MSIX packaging support — accept `.msix` / `.msixbundle` release assets, route through `Add-AppxPackage`

### v0.3.0 — Theming
- Catppuccin Latte light theme + system accent color
- Theme switch live (no restart)
- Cards retain their density and motion on both themes

### v0.4.0 — Cross-platform exploration
- Avalonia port for Linux + macOS — same data layer, same MVVM, OS-specific install handlers (`.deb` via `dpkg`, `.dmg` mount + drop into `/Applications`, Homebrew bottle)
- Single `localdesktopstore` codebase that sniffs OS at startup and shows only the relevant artifact kinds

### v0.5.0 — Discovery surface
- Multi-org UI for `ExtraOwners` (currently JSON-only edit)
- "Hide" gesture per card writes to `HiddenRepos`
- Optional GitHub Stars threshold filter to suppress experimental repos

### Backlog (not scheduled)
- App publisher / signature display on the card
- Per-card update history (which versions have been installed locally over time)
- Pre-flight disk space check + post-install size telemetry
- Optional auto-launch on install
- Quiet update-all loop with a single status summary

## Anti-roadmap

Things this project explicitly will not do:

- **No silent admin elevation.** UAC is the installer's call; we never bundle a manifest that requests elevation by default.
- **No unattended catalog updates** without a manual refresh — drift is louder than silent surprises.
- **No bundled .NET runtime.** Framework-dependent only. Self-contained doubles the artifact size for no real win in this workflow.
- **No telemetry.** Logs stay on the user's box.
- **No MVVM toolkit dependency.** `ViewModelBase` + `RelayCommand` + `AsyncRelayCommand` is enough.
- **No third-party UI library.** Catppuccin Mocha lives in a single `DarkTheme.xaml` — that's the entire design system.
