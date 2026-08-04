using System.Net;
using System.Text.Json;
using LocalDesktopStore.Services;
using Xunit;

namespace LocalDesktopStore.Tests;

public sealed class OsvServiceTests
{
    [Fact]
    public async Task QueryAsyncUsesGitHubReleasesPackageAndCountsAdvisories()
    {
        var handler = new RecordingHandler(["{\"vulns\":[{\"id\":\"OSV-1\"},{\"id\":\"OSV-2\"}]}"]);
        using var client = CreateClient(handler);
        var service = new OsvService(client);

        var result = await service.QueryAsync("SysAdminDoc/Example");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Count);
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        var package = document.RootElement.GetProperty("package");
        Assert.Equal("GitHubReleases", package.GetProperty("ecosystem").GetString());
        Assert.Equal("SysAdminDoc/Example", package.GetProperty("name").GetString());
    }

    [Fact]
    public async Task QueryAsyncFollowsOsvPagination()
    {
        var handler = new RecordingHandler([
            "{\"vulns\":[{\"id\":\"OSV-1\"}],\"next_page_token\":\"next-page\"}",
            "{\"vulns\":[{\"id\":\"OSV-2\"},{\"id\":\"OSV-3\"}]}"
        ]);
        using var client = CreateClient(handler);
        var service = new OsvService(client);

        var result = await service.QueryAsync("SysAdminDoc/Example");

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Count);
        Assert.Equal(2, handler.RequestBodies.Count);
        using var secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Equal("next-page", secondRequest.RootElement.GetProperty("page_token").GetString());
    }

    private static HttpClient CreateClient(RecordingHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.local/", UriKind.Absolute)
        };
    }

    private sealed class RecordingHandler(IReadOnlyList<string> responses) : HttpMessageHandler
    {
        private int _responseIndex;

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var index = Interlocked.Increment(ref _responseIndex) - 1;
            if (index >= responses.Count)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[index])
            };
        }
    }
}
