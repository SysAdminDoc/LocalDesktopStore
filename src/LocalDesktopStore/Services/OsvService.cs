using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed record AdvisoryQueryResult(int? Count, string? Error)
{
    public bool Succeeded => Count.HasValue && string.IsNullOrWhiteSpace(Error);
}

public sealed class OsvService
{
    private const int MaxConcurrentQueries = 4;
    private const int MaxPages = 16;
    private readonly HttpClient _httpClient;

    public OsvService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public async Task<AdvisoryQueryResult> QueryAsync(string repository, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return new(null, "Repository is required.");

        var total = 0;
        string? pageToken = null;
        for (var page = 0; page < MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var payload = new Dictionary<string, object?>
            {
                ["package"] = new Dictionary<string, string>
                {
                    ["ecosystem"] = "GitHubReleases",
                    ["name"] = repository.Trim()
                }
            };
            if (!string.IsNullOrWhiteSpace(pageToken))
                payload["page_token"] = pageToken;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "v1/query")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json")
                };
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                if (!response.IsSuccessStatusCode)
                    return new(null, $"OSV returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = document.RootElement;
                if (root.TryGetProperty("vulns", out var vulns)
                    && vulns.ValueKind == JsonValueKind.Array)
                {
                    total = checked(total + vulns.GetArrayLength());
                }

                pageToken = root.TryGetProperty("next_page_token", out var nextToken)
                    && nextToken.ValueKind == JsonValueKind.String
                    ? nextToken.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(pageToken))
                    return new(total, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return new(null, "OSV request timed out.");
            }
            catch (JsonException)
            {
                return new(null, "OSV returned an invalid response.");
            }
            catch (HttpRequestException)
            {
                return new(null, "OSV could not be reached.");
            }
            catch (OverflowException)
            {
                return new(null, "OSV returned too many advisories to display.");
            }
        }

        return new(null, "OSV returned too many result pages.");
    }

    public async Task EnrichAsync(
        IReadOnlyList<AppInfo> apps,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentQueries);
        var tasks = apps.Select(async app =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var result = await QueryAsync($"{app.RepoOwner}/{app.RepoName}", ct);
                app.AdvisoryCount = result.Count;
                app.AdvisoryCheckError = result.Error;
                progress?.Report(result.Succeeded
                    ? $"OSV advisories for {app.RepoOwner}/{app.RepoName}: {result.Count} open."
                    : $"! OSV advisories unavailable for {app.RepoOwner}/{app.RepoName}: {result.Error}");
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.osv.dev/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalDesktopStore", "0.2.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
