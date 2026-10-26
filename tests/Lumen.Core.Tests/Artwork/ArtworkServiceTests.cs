// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Microsoft.Extensions.Logging.Abstractions;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Artwork;

public sealed class ArtworkServiceTests : IDisposable
{
    private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "lumen-tests", "artwork-" + Guid.NewGuid().ToString("N"));
    private readonly RoutingHandler _http = new();
    private readonly FakeTime _time = new();
    private readonly LibraryEntry _game = Game("test", "1", "Sample");

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadsOnce_ThenServesFromCache()
    {
        _http.Routes["https://cdn.test/1/cover"] = () => Ok(_jpeg);

        var first = await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None);
        var second = await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None);

        first.ShouldNotBeNull();
        first.ShouldEndWith(".jpg");
        File.ReadAllBytes(first).ShouldBe(_jpeg);
        second.ShouldBe(first);
        _http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task FallsBackToTheNextCandidate()
    {
        _http.Routes["https://cdn.test/1/cover"] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        _http.Routes["https://cdn.test/1/cover-alt"] = () => Ok(_png);

        var path = await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None);

        path!.ShouldEndWith(".png");
    }

    [Fact]
    public async Task NonImageContent_IsRejected()
    {
        _http.Routes["https://cdn.test/1/cover"] = () => Ok("<html>not an image</html>"u8.ToArray());

        (await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None)).ShouldBeNull();
        Directory.Exists(_cache).ShouldBeTrue(); // a miss marker, but never the HTML
        Directory.GetFiles(_cache, "*.jpg", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task OversizedContent_IsRejected()
    {
        _http.Routes["https://cdn.test/1/cover"] = () =>
        {
            var big = new byte[ArtworkService.MaxImageBytes + 1];
            _jpeg.CopyTo(big, 0);
            return Ok(big);
        };

        (await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task NotFound_IsRemembered_ThenRetriedAfterAWeek()
    {
        _http.Routes["https://cdn.test/1/cover"] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        _http.Routes["https://cdn.test/1/cover-alt"] = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None);
        await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None);
        _http.Calls.ShouldBe(2); // both candidates once, then the remembered miss

        _time.Now += ArtworkService.MissRetryAfter + TimeSpan.FromMinutes(1);
        _http.Routes["https://cdn.test/1/cover"] = () => Ok(_jpeg);

        (await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task NetworkErrors_AreNotRememberedAsMisses()
    {
        _http.Routes["https://cdn.test/1/cover"] = () => throw new HttpRequestException("offline");
        _http.Routes["https://cdn.test/1/cover-alt"] = () => throw new HttpRequestException("offline");

        await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None);
        _http.Routes["https://cdn.test/1/cover"] = () => Ok(_jpeg);

        (await Service().GetAsync(_game, ArtworkKind.Cover, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ConcurrentRequests_ShareOneDownload()
    {
        var gate = new TaskCompletionSource();
        _http.Routes["https://cdn.test/1/cover"] = () => Ok(_jpeg);
        _http.Gate = gate.Task;
        var service = Service();

        var requests = Enumerable.Range(0, 5).Select(_ => service.GetAsync(_game, ArtworkKind.Cover, CancellationToken.None)).ToList();
        gate.SetResult();
        var paths = await Task.WhenAll(requests);

        paths.Distinct().ShouldHaveSingleItem().ShouldNotBeNull();
        _http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task NoSource_ReturnsNull_WithoutNetwork()
    {
        var other = Game("nosource", "1", "X");

        (await Service().GetAsync(other, ArtworkKind.Cover, CancellationToken.None)).ShouldBeNull();
        _http.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task MergedItem_UsesAnotherStoresArt_WhenThePrimaryHasNone()
    {
        var manual = Game("nosource", "m", "Sample");
        var item = new LibraryItem([manual, _game], manual);
        _http.Routes["https://cdn.test/1/cover"] = () => Ok(_jpeg);

        (await Service().GetForItemAsync(item, ArtworkKind.Cover, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ClearCache_RemovesFiles()
    {
        _http.Routes["https://cdn.test/1/cover"] = () => Ok(_jpeg);
        var service = Service();
        await service.GetAsync(_game, ArtworkKind.Cover, CancellationToken.None);

        service.ClearCache();

        Directory.Exists(_cache).ShouldBeFalse();
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, ".webp")]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, null)] // "MZ": an executable, never an image
    [InlineData(new byte[] { }, null)]
    public void DetectImageExtension(byte[] header, string? expected)
    {
        ArtworkService.DetectImageExtension(header).ShouldBe(expected);
    }

    private ArtworkService Service() => new(new HttpClient(_http), [new TestSource()], _cache, NullLogger<ArtworkService>.Instance, _time);

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private sealed class TestSource : IArtworkSource
    {
        public bool CanHandle(LibraryEntry entry) => entry.Ref.Source == "test";

        public IReadOnlyList<Uri> Candidates(LibraryEntry entry, ArtworkKind kind) =>
            [new($"https://cdn.test/{entry.Ref.ExternalId}/{kind.ToString().ToLowerInvariant()}"), new($"https://cdn.test/{entry.Ref.ExternalId}/{kind.ToString().ToLowerInvariant()}-alt")];
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private int _calls;

        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];

        public Task Gate { get; set; } = Task.CompletedTask;

        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Gate;
            return Routes.TryGetValue(request.RequestUri!.ToString(), out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
