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
}
