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

    /// <summary>Optional custom install switches reused for later updates of this repo.</summary>
    public string? InstallerArguments { get; set; }

    /// <summary>MSI ProductCode (registry subkey for MSI installs). Used for `msiexec /x`.</summary>
    public string? MsiProductCode { get; set; }

    /// <summary>MSIX Identity Name from AppxManifest.xml, used to find the current-user package.</summary>
    public string? AppxPackageName { get; set; }

    /// <summary>MSIX PackageFullName captured after installation when available.</summary>
    public string? AppxPackageFullName { get; set; }

    /// <summary>Normalized SHA-1 thumbprint of the trusted Authenticode signer at install time.</summary>
    public string? PublisherCertThumbprint { get; set; }

    /// <summary>Subject of the trusted Authenticode signer at install time, for display and diagnostics.</summary>
    public string? PublisherCertSubject { get; set; }

    public string Key => $"{RepoOwner}/{RepoName}";
}

public sealed class InstalledAppsManifest
{
    public int Version { get; set; } = 2;
    public List<InstalledApp> Apps { get; set; } = new();
}
