using System.Net;
using System.Net.Http;
using System.Text;
using LazyCrafter.Adapters;

namespace LazyCrafter.Harness;

/// <summary>
/// The 0.1.7.9 Universalis log-level fix (card t-plugerr-06b09cbcf75d): 429/5xx/timeout retries are
/// the client's designed, handled response and must log at Debug; only giving up after all attempts
/// (quotes left stale) is a Warning. Pinned here against a scripted HTTP transport so the real
/// retry loop, Retry-After honouring, batch parsing and disk-cache paths all run. Also pins the
/// 60s request timeout (20s cancelled slow-but-alive aggregated queries mid-flight) and the
/// log-text summarizer that keeps hundred-item id lists out of log lines.
/// </summary>
internal static class UniversalisClientTests
{
    public const uint Iron = 5099, Coke = 5100;

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _routes = new();

        public List<string> Paths { get; } = new();

        public ScriptedHandler Route(string path, params Func<HttpResponseMessage>[] responses)
        {
            _routes[path] = new Queue<Func<HttpResponseMessage>>(responses);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            path = path.StartsWith("api/v2/", StringComparison.Ordinal) ? path["api/v2/".Length..] : path;
            Paths.Add(path);
            if (!_routes.TryGetValue(path, out var q) || q.Count == 0)
                throw new InvalidOperationException($"no scripted response for {path}");
            return Task.FromResult(q.Dequeue()());
        }
    }

    private static Func<HttpResponseMessage> Json(int status, string body) =>
        () => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static Func<HttpResponseMessage> Retryable(int status, double seconds) => () =>
    {
        var r = new HttpResponseMessage((HttpStatusCode)status);
        r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return r;
    };

    private static Func<HttpResponseMessage> Timeout() =>
        () => throw new OperationCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 20 seconds elapsing.");

    private const string AggOk =
        """{"results":[{"itemId":5099,"nq":{"minListing":{"dc":{"price":100}},"averageSalePrice":{"dc":{"price":150}},"dailySaleVelocity":{"dc":{"quantity":2}}}}]}""";
    private const string CurOk =
        """{"items":{"5099":{"listingsCount":3,"lastUploadTime":1700000000000}}}""";

    private static (UniversalisClient Client, List<string> Warn, List<string> Debug, ScriptedHandler Handler) Make(
        ScriptedHandler handler, string? configDir = null)
    {
        var warn = new List<string>();
        var debug = new List<string>();
        var dir = configDir ?? Path.Combine(Path.GetTempPath(), "lcz-uc-" + Guid.NewGuid().ToString("N"));
        var client = new UniversalisClient(dir, "0.1.7.9-test",
            line => warn.Add(line), line => debug.Add(line), handler);
        return (client, warn, debug, handler);
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("uc: the HTTP timeout is 60s - 20s cancelled slow aggregated queries mid-flight",
            () => UniversalisClient.RequestTimeout == TimeSpan.FromSeconds(60)),

        ("uc: Describe summarizes id lists and passes other URLs through",
            () => UniversalisClient.Describe("aggregated/Hyperion/5099,5100,5098") == "aggregated/Hyperion/3 ids"
               && UniversalisClient.Describe("Hyperion/5099,5100?entries=0&fields=x") == "Hyperion/2 ids?entries=0&fields=x"
               && UniversalisClient.Describe("marketable") == "marketable"
               && UniversalisClient.Describe("tax-rates?world=Hyperion") == "tax-rates?world=Hyperion"),

        ("uc: a 504 with retries that then succeeds logs at Debug only, and still parses the quote",
            () =>
            {
                var h = new ScriptedHandler()
                    .Route("marketable", Json(200, "[5099,5100]"))
                    .Route($"aggregated/Hyperion/{Iron},{Coke}",
                        Retryable(504, 1), Retryable(504, 1), Json(200, AggOk))
                    .Route($"Hyperion/{Iron},{Coke}", Json(200, CurOk));
                var (c, warn, debug, _) = Make(h);
                c.Scope = "Hyperion";
                var n = c.PrimeAsync(new uint[] { Iron, Coke }).GetAwaiter().GetResult();
                var q = c.Get(Iron);
                return n == 2
                    && warn.Count == 0
                    && debug.Count(l => l.Contains("-> 504") && l.Contains("retry 1/4")) == 1
                    && debug.Count(l => l.Contains("-> 504") && l.Contains("retry 2/4")) == 1
                    && !debug.Any(l => l.Contains("retry 3/4"))
                    && q is not null && q.MinListingNq == 100 && q.ListingsCount == 3 && q.VelocityNq == 2
                    && c.Get(Coke) is not null;   // unknown to Universalis -> empty quote, not refetch loop
            }),

        ("uc: a mid-retry timeout attempt logs at Debug only",
            () =>
            {
                var h = new ScriptedHandler()
                    .Route("marketable", Json(200, $"[{Iron}]"))
                    .Route($"aggregated/Hyperion/{Iron}", Timeout(), Json(200, AggOk))
                    .Route($"Hyperion/{Iron}", Json(200, CurOk));
                var (c, warn, debug, _) = Make(h);
                c.Scope = "Hyperion";
                c.PrimeAsync(new uint[] { Iron }).GetAwaiter().GetResult();
                return warn.Count == 0
                    && debug.Any(l => l.Contains("attempt 1 failed") && l.Contains("HttpClient.Timeout"))
                    && c.Get(Iron)?.MinListingNq == 100;
            }),

        ("uc: giving up after all four attempts warns exactly once, retries stay Debug",
            () =>
            {
                var h = new ScriptedHandler()
                    .Route("marketable", Json(200, $"[{Iron}]"))
                    .Route($"aggregated/Hyperion/{Iron}",
                        Retryable(504, 1), Retryable(504, 1), Retryable(504, 1), Retryable(504, 1));
                var (c, warn, debug, _) = Make(h);
                c.Scope = "Hyperion";
                c.PrimeAsync(new uint[] { Iron }).GetAwaiter().GetResult();
                return warn.Count == 1 && warn[0].Contains("gave up after 4 attempts")
                    && debug.Count(l => l.Contains("-> 504")) == 3       // the fourth attempt retries nothing
                    && c.Get(Iron) is null;                              // nothing cached: next prime asks again
            }),

        ("uc: the disk-cache load line logs at Debug, not Warning",
            () =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "lcz-uc-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var fetched = DateTime.UtcNow.AddMinutes(-1).ToString("o");
                File.WriteAllText(Path.Combine(dir, "prices.json"),
                    $$"""{"Version":1,"Scope":"Hyperion","Entries":[{"Quote":{"ItemId":5099,"MinListingNq":100,"VelocityNq":2},"FetchedAt":"{{fetched}}"}]}""");
                var h = new ScriptedHandler();
                var (c, warn, debug, _) = Make(h, dir);
                return warn.Count == 0
                    && debug.Any(l => l.Contains("disk cache: 1 fresh quotes loaded"))
                    && c.Get(Iron)?.MinListingNq == 100;
            }),
    };
}
