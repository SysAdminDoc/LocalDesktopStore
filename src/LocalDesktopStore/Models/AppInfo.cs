namespace LocalDesktopStore.Models;

public sealed class AppInfo
{
    public required string RepoOwner { get; init; }
    public required string RepoName { get; init; }
    public required string RepoUrl { get; init; }
    public string? RepoDescription { get; init; }
    public string? LatestVersion { get; set; }
    public string? AssetUrl { get; set; }
    public string? AssetName { get; set; }
    public long AssetSizeBytes { get; set; }
    public ArtifactKind Kind { get; set; } = ArtifactKind.Unknown;
    public string? Sha256Url { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public IReadOnlyList<string> IconCandidates { get; set; } = Array.Empty<string>();
    public int Stars { get; set; }
    public string? Topics { get; set; }

    public string DisplayName => RepoName;
    public string DisplayVersion => LatestVersion?.TrimStart('v') ?? "—";
    public string DisplayDescription =>
        !string.IsNullOrWhiteSpace(RepoDescription) ? RepoDescription! : "No description provided.";
}
