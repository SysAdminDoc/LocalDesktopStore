using System.IO;
using System.Net.Http;
using LocalDesktopStore.Models;
using Octokit;
using Octokit.Internal;

namespace LocalDesktopStore.Services;

public sealed class GitHubService
{
    private readonly HttpClient _http;
    private GitHubClient? _client;
    private EtagCachingHandler? _etagHandler;
    private string? _activeToken;

    public GitHubService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalDesktopStore/0.3.1");
    }

    public int CacheHits => _etagHandler?.Hits ?? 0;
    public int CacheMisses => _etagHandler?.Misses ?? 0;

    private GitHubClient GetClient(AppSettings cfg)
    {
        if (_client != null && _activeToken == cfg.GitHubToken) return _client;
        var product = new ProductHeaderValue("LocalDesktopStore", "0.3.1");
        var credStore = string.IsNullOrWhiteSpace(cfg.GitHubToken)
            ? (ICredentialStore)new InMemoryCredentialStore(Credentials.Anonymous)
            : new InMemoryCredentialStore(new Credentials(cfg.GitHubToken));
        // Per-token handler: rotating the PAT must invalidate the ETag cache so we don't
        // replay one user's cached payload to a different account.
        _etagHandler = new EtagCachingHandler(new HttpClientHandler());
        var handler = _etagHandler;
        var connection = new Connection(
            product,
            GitHubClient.GitHubApiUrl,
            credStore,
            new HttpClientAdapter(() => handler),
            new SimpleJsonSerializer());
        _client = new GitHubClient(connection);
        _activeToken = cfg.GitHubToken;
        return _client;
    }

    /// <summary>
    /// Discover candidate desktop apps across the configured user(s).
    /// A repo qualifies when its latest release contains at least one supported installer
    /// asset (.msi, .exe, .zip, .msix, .msixbundle, or .appinstaller).
    /// </summary>
    public async Task<List<AppInfo>> DiscoverAsync(AppSettings cfg, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var client = GetClient(cfg);
        var owners = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.GitHubUser)) owners.Add(cfg.GitHubUser.Trim());
        owners.AddRange(cfg.ExtraOwners.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()));
        owners = owners.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var hitsBefore = _etagHandler?.Hits ?? 0;
        var missesBefore = _etagHandler?.Misses ?? 0;
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

        if (cfg.EnableGitHubSearchDiscovery)
        {
            var searched = await DiscoverSearchResultsAsync(client, cfg, log, ct);
            var existing = new HashSet<string>(
                found.Select(info => $"{info.RepoOwner}/{info.RepoName}"),
                StringComparer.OrdinalIgnoreCase);
            foreach (var info in searched)
            {
                if (existing.Add($"{info.RepoOwner}/{info.RepoName}"))
                    found.Add(info);
            }
        }

        if (_etagHandler is not null)
        {
            var hits = _etagHandler.Hits - hitsBefore;
            var misses = _etagHandler.Misses - missesBefore;
            if (hits + misses > 0)
                log?.Report($"  ETag cache: {hits} 304 hit(s), {misses} fresh fetch(es)");
        }
        return found;
    }

    private async Task<List<AppInfo>> DiscoverSearchResultsAsync(
        GitHubClient client,
        AppSettings cfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var topic = string.IsNullOrWhiteSpace(cfg.SearchTopic) ? "windows-app" : cfg.SearchTopic.Trim();
        log?.Report($"Searching GitHub repositories tagged topic:{topic}...");
        SearchRepositoryResult results;
        try
        {
            var request = new SearchRepositoriesRequest($"topic:{topic}")
            {
                SortField = RepoSearchSort.Stars,
                Order = SortDirection.Descending,
                Page = 1,
                PerPage = 50,
                Archived = false
            };
            results = await client.Search.SearchRepo(request);
        }
        catch (Exception ex)
        {
            log?.Report($"  ! GitHub Search discovery failed: {ex.Message}");
            return new List<AppInfo>();
        }

        log?.Report($"  {results.TotalCount} search result(s); probing the first {results.Items.Count} for Windows release assets.");
        var found = new List<AppInfo>();
        for (var index = 0; index < results.Items.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var repo = results.Items[index];
            if (repo.Archived || cfg.HiddenRepos.Contains($"{repo.Owner.Login}/{repo.Name}", StringComparer.OrdinalIgnoreCase))
                continue;

            var info = await ProbeRepoAsync(client, repo, log, ct, isSearchDiscovered: true, discoveryRank: index + 1);
            if (info is not null)
                found.Add(info);
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

    private async Task<AppInfo?> ProbeRepoAsync(
        GitHubClient client,
        Repository repo,
        IProgress<string>? log,
        CancellationToken ct,
        bool isSearchDiscovered = false,
        int discoveryRank = 0)
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
            IconCandidates = ResolveIconCandidates(repo),
            IsSearchDiscovered = isSearchDiscovered,
            DiscoveryRank = discoveryRank
        };
        return info;
    }

    private static IReadOnlyList<string> ResolveIconCandidates(Repository repo)
    {
        var owner = repo.Owner.Login;
        var name = repo.Name;
        var raw = $"https://raw.githubusercontent.com/{owner}/{name}/HEAD";
        return new[]
        {
            $"{raw}/logo.png",
            $"{raw}/banner.png",
            $"{raw}/icon.png",
            $"https://opengraph.githubassets.com/1/{owner}/{name}"
        };
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
