namespace LocalDesktopStore.Models;

public sealed class AppSettings
{
    public string GitHubUser { get; set; } = "SysAdminDoc";
    public string? GitHubToken { get; set; }
    public bool UseTopicFilter { get; set; } = false;
    public string TopicFilter { get; set; } = "windows-app";
    public List<string> ExtraOwners { get; set; } = new();
    public List<string> HiddenRepos { get; set; } = new();
    public bool VerifyHashSidecar { get; set; } = true;
    public string? InstallRootOverride { get; set; }
    public bool UseLightTheme { get; set; }
    public bool UseSystemAccent { get; set; }
    public bool EnableScheduledUpdateChecks { get; set; }
    public int ScheduledUpdateIntervalHours { get; set; } = 6;
    public Dictionary<string, string> CatalogVersionPins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AppInstallPreferences> InstallPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AppInstallPreferences
{
    public bool RunAfterInstall { get; set; }
    public bool PinToTaskbar { get; set; }
    public string? InstallerArguments { get; set; }
}
