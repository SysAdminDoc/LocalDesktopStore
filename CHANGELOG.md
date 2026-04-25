# Changelog

All notable changes to LocalDesktopStore are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to [Semantic Versioning](https://semver.org/).

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
