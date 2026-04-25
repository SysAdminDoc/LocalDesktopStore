namespace LocalDesktopStore.Models;

public sealed class InstalledApp
{
    public required string RepoOwner { get; set; }
    public required string RepoName { get; set; }
    public required string Version { get; set; }
    public required ArtifactKind Kind { get; set; }
    public DateTimeOffset InstalledAt { get; set; }

    /// <summary>For portable apps, the extraction directory. Empty for installer-driven apps.</summary>
    public string? PortableRoot { get; set; }

    /// <summary>For portable apps, the chosen Start-Menu shortcut path.</summary>
    public string? ShortcutPath { get; set; }

    /// <summary>For portable apps, the executable to launch.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Registry uninstall key path (e.g. HKLM\...\Uninstall\{guid}). Used for installer-driven apps.</summary>
    public string? UninstallRegistryKey { get; set; }

    /// <summary>UninstallString or QuietUninstallString as captured at install time.</summary>
    public string? UninstallCommand { get; set; }

    /// <summary>InstallLocation as captured from the registry, used to locate the launchable .exe.</summary>
    public string? InstallLocation { get; set; }

    /// <summary>MSI ProductCode (registry subkey for MSI installs). Used for `msiexec /x`.</summary>
    public string? MsiProductCode { get; set; }

    public string Key => $"{RepoOwner}/{RepoName}";
}

public sealed class InstalledAppsManifest
{
    public int Version { get; set; } = 1;
    public List<InstalledApp> Apps { get; set; } = new();
}
