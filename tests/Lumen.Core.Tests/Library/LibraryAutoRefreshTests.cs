// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Microsoft.Extensions.Logging.Abstractions;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

/// <summary>Uses a temp folder standing in for a store's manifest folder (Lumen never writes to real store folders).</summary>
public sealed class LibraryAutoRefreshTests : IDisposable
{
    private readonly string _storeFolder = Path.Combine(Path.GetTempPath(), "lumen-tests", "fake-store-" + Guid.NewGuid().ToString("N"));
    private readonly WatchedProvider _provider;
    private readonly LibraryService _library;

    public LibraryAutoRefreshTests()
    {
        Directory.CreateDirectory(_storeFolder);
        _provider = new WatchedProvider(_storeFolder);
        _library = new LibraryService([_provider], new InMemoryCache(), NullLogger<LibraryService>.Instance);
    }

    public void Dispose() => Directory.Delete(_storeFolder, recursive: true);

    [Fact]
    public async Task ManifestAppearing_TriggersARescan()
    {
        await _library.ScanAsync(CancellationToken.None);
        using var refresh = new LibraryAutoRefresh(_library, NullLogger<LibraryAutoRefresh>.Instance, TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
        await refresh.StartAsync(CancellationToken.None);

        // "Install finished": the store writes a manifest, and the provider now reports the game as installed.
        _provider.Installed = true;
        await File.WriteAllTextAsync(Path.Combine(_storeFolder, "game.manifest"), "installed");

        await WaitUntil(() => _library.Entries.SingleOrDefault()?.IsInstalled == true);
    }

    [Fact]
    public async Task BurstOfChanges_IsCoalesced()
    {
        await _library.ScanAsync(CancellationToken.None);
        var before = _provider.Scans;
        using var refresh = new LibraryAutoRefresh(_library, NullLogger<LibraryAutoRefresh>.Instance, TimeSpan.FromMilliseconds(300), TimeSpan.Zero);
        await refresh.StartAsync(CancellationToken.None);

        for (var i = 0; i < 20; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_storeFolder, "game.manifest"), $"progress {i}");
        }

        await WaitUntil(() => _provider.Scans > before);
        await Task.Delay(800);
        (_provider.Scans - before).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task WindowActivation_TriggersARescan()
    {
        await _library.ScanAsync(CancellationToken.None);
        var before = _provider.Scans;
        using var refresh = new LibraryAutoRefresh(_library, NullLogger<LibraryAutoRefresh>.Instance, TimeSpan.FromMilliseconds(50), TimeSpan.Zero);

        refresh.OnWindowActivated();

        await WaitUntil(() => _provider.Scans > before);
    }

    [Fact]
    public async Task ScansAreSpacedOut()
    {
        using var refresh = new LibraryAutoRefresh(_library, NullLogger<LibraryAutoRefresh>.Instance, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(30));
        refresh.OnWindowActivated();
        await WaitUntil(() => _provider.Scans == 1);

        refresh.OnWindowActivated();
        await Task.Delay(600);

        _provider.Scans.ShouldBe(1); // the second request waits for the 30 s gap
    }

    [Fact]
    public async Task AfterDispose_NothingRuns()
    {
        var refresh = new LibraryAutoRefresh(_library, NullLogger<LibraryAutoRefresh>.Instance, TimeSpan.FromMilliseconds(10), TimeSpan.Zero);
        await refresh.StartAsync(CancellationToken.None);
        refresh.Dispose();

        refresh.OnWindowActivated();
        await File.WriteAllTextAsync(Path.Combine(_storeFolder, "game.manifest"), "x");
        await Task.Delay(300);

        _provider.Scans.ShouldBe(0);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class WatchedProvider(string folder) : ILibraryProvider, IWatchableProvider
    {
        private int _scans;

        public bool Installed { get; set; }

        public int Scans => Volatile.Read(ref _scans);

        public string Source => "fake";

        public string DisplayName => "Fake";

        public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

        public Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _scans);
            return Task.FromResult(ProviderResult.FromScan([Game("fake", "1", "Sample", installed: Installed)], []));
        }

        public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) => Task.FromResult(ProviderResult.NotConfigured("n/a"));

        public Task<IReadOnlyList<WatchTarget>> GetWatchTargetsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WatchTarget>>([new WatchTarget(folder, "*.manifest")]);
    }
}
