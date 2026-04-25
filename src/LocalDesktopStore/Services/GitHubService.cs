using System.IO;
using System.Net.Http;
using LocalDesktopStore.Models;
using Octokit;

namespace LocalDesktopStore.Services;

public sealed class GitHubService
{
    private readonly HttpClient _http;
    private GitHubClient? _client;
    private string? _activeToken;

    public GitHubService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalDesktopStore/0.1");
    }

    private GitHubClient GetClient(AppSettings cfg)
    {
        if (_client != null && _activeToken == cfg.GitHubToken) return _client;
        var product = new ProductHeaderValue("LocalDesktopStore", "0.1.0");
        var c = new GitHubClient(product);
        if (!string.IsNullOrWhiteSpace(cfg.GitHubToken))
            c.Credentials = new Credentials(cfg.GitHubToken);
        _client = c;
        _activeToken = cfg.GitHubToken;
        return c;
    }

    /// <summary>
    /// Discover candidate desktop apps across the configured user(s).
    /// A repo qualifies when its latest release contains at least one .msi, .exe, or .zip
    /// asset that AssetClassifier accepts.
    /// </summary>
    public async Task<List<AppInfo>> DiscoverAsync(AppSettings cfg, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var client = GetClient(cfg);
        var owners = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.GitHubUser)) owners.Add(cfg.GitHubUser.Trim());
        owners.AddRange(cfg.ExtraOwners.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()));
        owners = owners.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var found = new List<AppInfo>();
        foreach (var owner in owners)
        {
            log?.Report($"Listing repos for {owner}...");
            IReadOnlyList<Repository> repos;
            try { repos = await client.Repository.GetAllForUser(owner); }
            catch (Exception ex) { log?.Report($"  ! {owner}: {ex.Message}"); continue; }

            log?.Report($"  {repos.Count} repos returned");
            foreach (var repo in repos)
            {
                ct.ThrowIfCancellationRequested();
                if (repo.Archived) continue;
                if (cfg.HiddenRepos.Contains($"{repo.Owner.Login}/{repo.Name}", StringComparer.OrdinalIgnoreCase)) continue;

                if (cfg.UseTopicFilter && !string.IsNullOrWhiteSpace(cfg.TopicFilter))
                {
                    var topics = await SafeGetTopics(client, repo);
                    if (topics is null || !topics.Any(t => t.Equals(cfg.TopicFilter, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                var info = await ProbeRepoAsync(client, repo, log, ct);
                if (info != null) found.Add(info);
            }
        }
        return found;
    }

    private static async Task<List<string>?> SafeGetTopics(GitHubClient client, Repository repo)
    {
        try
        {
            var topics = await client.Repository.GetAllTopics(repo.Id);
            return topics?.Names?.ToList();
        }
        catch { return null; }
    }

    private async Task<AppInfo?> ProbeRepoAsync(GitHubClient client, Repository repo, IProgress<string>? log, CancellationToken ct)
    {
        Release? release = null;
        try { release = await client.Repository.Release.GetLatest(repo.Owner.Login, repo.Name); }
        catch (NotFoundException) { return null; }
        catch (Exception ex) { log?.Report($"  ! release {repo.Name}: {ex.Message}"); return null; }

        if (release == null || release.Assets.Count == 0) return null;

        var classified = release.Assets
            .Select(a => (Asset: a, Kind: AssetClassifier.ClassifyByName(a.Name)))
            .Where(t => t.Kind != ArtifactKind.Unknown)
            .OrderByDescending(t => t.Kind.Priority())
            .ThenByDescending(t => t.Asset.Size)
            .ToList();

        var best = classified.FirstOrDefault();
        if (best.Asset == null) return null;

        // Sidecar lookup: prefer "<asset>.sha256.txt", accept "<asset>.sha256" too.
        var sidecar = release.Assets.FirstOrDefault(a =>
            a.Name.Equals($"{best.Asset.Name}.sha256.txt", StringComparison.OrdinalIgnoreCase)
            || a.Name.Equals($"{best.Asset.Name}.sha256", StringComparison.OrdinalIgnoreCase));

        var info = new AppInfo
        {
            RepoOwner = repo.Owner.Login,
            RepoName = repo.Name,
            RepoUrl = repo.HtmlUrl,
            RepoDescription = repo.Description,
            Stars = repo.StargazersCount,
            LatestVersion = release.TagName,
            AssetUrl = best.Asset.BrowserDownloadUrl,
            AssetName = best.Asset.Name,
            AssetSizeBytes = best.Asset.Size,
            Kind = best.Kind,
            Sha256Url = sidecar?.BrowserDownloadUrl,
            PublishedAt = release.PublishedAt,
            IconUrl = ResolveIconUrl(repo)
        };
        return info;
    }

    private static string ResolveIconUrl(Repository repo)
    {
        // Convention: the user ships logo.png at repo root for every project; fall back to GitHub OG image.
        // We don't HEAD-probe here — the icon loader retries gracefully on 404.
        return $"https://raw.githubusercontent.com/{repo.Owner.Login}/{repo.Name}/HEAD/logo.png";
    }

    public async Task DownloadAssetToFileAsync(string url, string destination, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = new FileStream(destination, System.IO.FileMode.Create, FileAccess.Write, FileShare.None);
        var buf = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, read), ct);
            readTotal += read;
            progress?.Report(readTotal);
        }
    }

    public async Task<string?> TryDownloadTextAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch { return null; }
    }

    public async Task<byte[]?> TryDownloadBytesAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch { return null; }
    }
}
