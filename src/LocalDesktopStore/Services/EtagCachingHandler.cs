using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace LocalDesktopStore.Services;

/// <summary>
/// HTTP delegating handler that adds <c>If-None-Match</c> on GET requests using a per-key
/// ETag cache, and short-circuits 304 Not Modified responses by replaying the cached body.
/// Cache key is <c>(absolute URL, Authorization header value)</c> so PAT rotation invalidates
/// implicitly. Per GitHub docs, conditional 304 responses do not count against the rate limit.
/// </summary>
public sealed class EtagCachingHandler : DelegatingHandler
{
    private readonly Dictionary<string, CachedResponse> _cache = new();
    private readonly object _lock = new();

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Bypassed { get; private set; }

    public EtagCachingHandler() : base() { }

    public EtagCachingHandler(HttpMessageHandler inner) : base(inner) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var key = BuildKey(request);
        CachedResponse? cached;
        lock (_lock) _cache.TryGetValue(key, out cached);

        if (cached is { ETag.Length: > 0 })
        {
            // Octokit may have already pinned an If-None-Match — defer to it if so.
            if (request.Headers.IfNoneMatch.Count == 0)
            {
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cached.ETag, cached.WeakTag));
            }
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            response.Dispose();
            lock (_lock) Hits++;
            return cached.Replay();
        }

        if (response.IsSuccessStatusCode)
        {
            var etag = response.Headers.ETag;
            if (etag is not null && response.Content is not null)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var captured = CachedResponse.Capture(response, bytes);
                lock (_lock)
                {
                    _cache[key] = captured;
                    Misses++;
                }

                // Replace consumed content stream so Octokit can read the body.
                var replacement = new ByteArrayContent(bytes);
                CopyHeaders(response.Content.Headers, replacement.Headers);
                response.Content = replacement;
                return response;
            }
        }

        lock (_lock) Bypassed++;
        return response;
    }

    private static string BuildKey(HttpRequestMessage request)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var auth = request.Headers.Authorization?.ToString() ?? string.Empty;
        return $"{url}|{auth}";
    }

    private static void CopyHeaders(HttpContentHeaders src, HttpContentHeaders dst)
    {
        foreach (var h in src)
        {
            // ContentLength is auto-managed by ByteArrayContent — skipping avoids a
            // duplicate-add InvalidOperationException on certain runtimes.
            if (string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            dst.TryAddWithoutValidation(h.Key, h.Value);
        }
    }

    private sealed class CachedResponse
    {
        public string ETag { get; init; } = string.Empty;
        public bool WeakTag { get; init; }
        public byte[] Body { get; init; } = Array.Empty<byte>();
        public string? MediaType { get; init; }
        public Dictionary<string, IEnumerable<string>> ResponseHeaders { get; init; } = new();
        public Dictionary<string, IEnumerable<string>> ContentHeaders { get; init; } = new();
        public Version ProtocolVersion { get; init; } = HttpVersion.Version11;

        public static CachedResponse Capture(HttpResponseMessage src, byte[] body)
        {
            var entry = new CachedResponse
            {
                ETag = src.Headers.ETag?.Tag ?? string.Empty,
                WeakTag = src.Headers.ETag?.IsWeak ?? false,
                Body = body,
                MediaType = src.Content.Headers.ContentType?.MediaType,
                ProtocolVersion = src.Version
            };

            foreach (var h in src.Headers)
            {
                // ETag re-emitted explicitly on replay; everything else flows through.
                if (string.Equals(h.Key, "ETag", StringComparison.OrdinalIgnoreCase)) continue;
                entry.ResponseHeaders[h.Key] = h.Value.ToArray();
            }

            foreach (var h in src.Content.Headers)
            {
                if (string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                entry.ContentHeaders[h.Key] = h.Value.ToArray();
            }

            return entry;
        }

        public HttpResponseMessage Replay()
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Body),
                Version = ProtocolVersion
            };
            if (!string.IsNullOrEmpty(ETag))
            {
                msg.Headers.ETag = new EntityTagHeaderValue(ETag, WeakTag);
            }
            foreach (var h in ResponseHeaders)
            {
                msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            if (MediaType is not null)
            {
                msg.Content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
            }
            foreach (var h in ContentHeaders)
            {
                msg.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            return msg;
        }
    }
}
