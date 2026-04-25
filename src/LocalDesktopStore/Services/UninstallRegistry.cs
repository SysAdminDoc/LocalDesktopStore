using Microsoft.Win32;

namespace LocalDesktopStore.Services;

public sealed record UninstallEntry(
    string Hive,
    string SubKeyName,
    string DisplayName,
    string? Publisher,
    string? DisplayVersion,
    string? UninstallString,
    string? QuietUninstallString,
    string? InstallLocation,
    string? IconPath,
    bool IsMsi);

/// <summary>
/// Reads Windows uninstall keys to detect installed apps. Reads HKLM (both 64-bit and 32-bit
/// views) and HKCU. We never write — write happens through msiexec / installer.
/// </summary>
public static class UninstallRegistry
{
    private const string Path64 = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Path32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<UninstallEntry> ReadAll()
    {
        var results = new List<UninstallEntry>();
        Read(Registry.LocalMachine, Path64, "HKLM\\64", results);
        Read(Registry.LocalMachine, Path32, "HKLM\\32", results);
        Read(Registry.CurrentUser, Path64, "HKCU", results);
        return results;
    }

    /// <summary>
    /// Find the entry that best matches the given repo identifiers. Match strategy: prefer
    /// exact DisplayName == repoName, fall back to substring of DisplayName, then Publisher
    /// substring of repoOwner. Caller can pin the chosen entry to the install record.
    /// </summary>
    public static UninstallEntry? FindBestMatch(string repoOwner, string repoName, string? assetVersion = null)
    {
        var all = ReadAll();
        UninstallEntry? exact = null;
        UninstallEntry? prefix = null;
        UninstallEntry? contains = null;
        foreach (var e in all)
        {
            if (string.IsNullOrEmpty(e.DisplayName)) continue;
            if (e.DisplayName.Equals(repoName, StringComparison.OrdinalIgnoreCase))
            {
                exact ??= e;
                if (assetVersion != null && e.DisplayVersion != null
                    && e.DisplayVersion.TrimStart('v').Equals(assetVersion.TrimStart('v'), StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            else if (e.DisplayName.StartsWith(repoName + " ", StringComparison.OrdinalIgnoreCase)
                  || e.DisplayName.StartsWith(repoName + "-", StringComparison.OrdinalIgnoreCase))
            {
                prefix ??= e;
            }
            else if (e.DisplayName.Contains(repoName, StringComparison.OrdinalIgnoreCase))
            {
                contains ??= e;
            }
        }
        return exact ?? prefix ?? contains;
    }

    private static void Read(RegistryKey hive, string subKeyPath, string hiveLabel, List<UninstallEntry> results)
    {
        try
        {
            using var view = hive == Registry.LocalMachine && subKeyPath == Path32
                ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                : (hive == Registry.LocalMachine
                    ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default));
            using var root = view.OpenSubKey(subKeyPath == Path32 ? Path64 : subKeyPath);
            if (root == null) return;

            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                if (k == null) continue;

                var name = k.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Skip Windows updates and system components.
                var systemComponent = k.GetValue("SystemComponent") as int?;
                if (systemComponent.HasValue && systemComponent.Value == 1) continue;
                var parent = k.GetValue("ParentKeyName") as string;
                if (!string.IsNullOrEmpty(parent)) continue;
                var releaseType = k.GetValue("ReleaseType") as string;
                if (!string.IsNullOrEmpty(releaseType)
                    && (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase)
                     || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)
                     || releaseType.Contains("ServicePack", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var entry = new UninstallEntry(
                    Hive: hiveLabel,
                    SubKeyName: sub,
                    DisplayName: name!,
                    Publisher: k.GetValue("Publisher") as string,
                    DisplayVersion: k.GetValue("DisplayVersion") as string,
                    UninstallString: k.GetValue("UninstallString") as string,
                    QuietUninstallString: k.GetValue("QuietUninstallString") as string,
                    InstallLocation: k.GetValue("InstallLocation") as string,
                    IconPath: k.GetValue("DisplayIcon") as string,
                    IsMsi: sub.StartsWith('{') && sub.EndsWith('}'));
                results.Add(entry);
            }
        }
        catch { /* registry view unavailable; skip */ }
    }
}
