using System.Net;
using System.Text;
using TheSingularityWorkshop.CiAi;

internal static class NuGetV3VersionCatalogContractTests
{
    public static int Run()
    {
        var passed = 0;
        Check("NuGet V3 catalog returns exact versions with feed and observation provenance", () =>
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"versions\":[\"1.0.0\",\"1.1.0-beta.1\",\"1.1.0\"]}",
                    Encoding.UTF8, "application/json")
            });
            using var client = new HttpClient(handler);
            var observedAt = new DateTimeOffset(2026, 10, 27, 3, 0, 0, TimeSpan.Zero);
            var catalog = new NuGetV3VersionCatalog(client, timeProvider: new FixedTimeProvider(observedAt));
            var result = catalog.ObserveAsync("Example.Package").GetAwaiter().GetResult();

            Assert(result.PackageId == "Example.Package", "requested package ID was not preserved");
            Assert(result.Feed.AbsoluteUri == "https://api.nuget.org/v3-flatcontainer/",
                "default NuGet feed provenance was incorrect");
            Assert(result.Resource.AbsoluteUri ==
                "https://api.nuget.org/v3-flatcontainer/example.package/index.json",
                "package resource was not normalized for NuGet's flat container");
            Assert(result.ObservedAtUtc == observedAt, "observation timestamp was not recorded");
            Assert(result.Versions.SequenceEqual(new[] { "1.0.0", "1.1.0-beta.1", "1.1.0" }),
                "published version strings were changed or reordered");
            Assert(handler.LastRequest?.Method == HttpMethod.Get, "catalog did not use a read-only GET");
        });

        Check("NuGet V3 catalog rejects malformed index payloads", () =>
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"notVersions\":[]}", Encoding.UTF8, "application/json")
            });
            using var client = new HttpClient(handler);
            var catalog = new NuGetV3VersionCatalog(client);
            Throws<InvalidDataException>(() => catalog.ObserveAsync("Example.Package").GetAwaiter().GetResult());
        });

        Check("NuGet V3 catalog rejects empty version lists", () =>
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"versions\":[]}", Encoding.UTF8, "application/json")
            });
            using var client = new HttpClient(handler);
            var catalog = new NuGetV3VersionCatalog(client);
            Throws<InvalidDataException>(() => catalog.ObserveAsync("Example.Package").GetAwaiter().GetResult());
        });

        Check("NuGet V3 catalog rejects package IDs with surrounding whitespace", () =>
        {
            using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Should not send")));
            var catalog = new NuGetV3VersionCatalog(client);
            Throws<ArgumentException>(() => catalog.ObserveAsync(" Example.Package ").GetAwaiter().GetResult());
        });

        Check("NuGet V3 catalog requires an absolute HTTPS base URI ending in slash", () =>
        {
            using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Should not send")));
            Throws<ArgumentException>(() => new NuGetV3VersionCatalog(client,
                new Uri("http://example.com/flatcontainer/")));
            Throws<ArgumentException>(() => new NuGetV3VersionCatalog(client,
                new Uri("https://example.com/flatcontainer")));
        });

        return passed;

        void Check(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine($"PASS NuGet V3 version catalog: {name}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
