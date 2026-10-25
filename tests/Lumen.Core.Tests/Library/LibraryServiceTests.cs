// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Microsoft.Extensions.Logging.Abstractions;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class LibraryServiceTests
{
    private readonly InMemoryCache _cache = new();

    [Fact]
    public async Task Scan_PublishesSortedEntriesAndPersistsPerSource()
    {
        var steam = new FakeProvider("steam").WithInstalled(Game("steam", "2", "beta"), Game("steam", "1", "Alpha"));
        var service = Service(steam);
        var changed = 0;
        service.Changed += (_, _) => changed++;

        await service.ScanAsync(CancellationToken.None);

        service.Entries.Select(e => e.Title).ShouldBe(["Alpha", "beta"]);
        _cache.BySource["steam"].Count.ShouldBe(2);
        changed.ShouldBe(1);
    }

    [Fact]
    public async Task InstalledWins_OwnedFillsTheRestAsNotInstalled()
    {
        var steam = new FakeProvider("steam")
            .WithInstalled(Game("steam", "1", "Installed One"))
            .WithOwned(Game("steam", "1", "Installed One", installed: false), Game("steam", "2", "Owned Two", installed: true));
        var service = Service(steam);

        await service.ScanAsync(CancellationToken.None);

        service.Entries.Single(e => e.Ref.ExternalId == "1").State.ShouldBe(InstallState.Installed);
        var owned = service.Entries.Single(e => e.Ref.ExternalId == "2");
        owned.State.ShouldBe(InstallState.NotInstalled);
        owned.InstallDirectory.ShouldBeNull();
    }

    [Fact]
    public async Task FailedProvider_KeepsItsCachedEntries_AndOthersStillUpdate()
    {
        _cache.BySource["steam"] = [Game("steam", "1", "Cached")];
        var steam = new FakeProvider("steam") { Installed = _ => Task.FromResult(ProviderResult.Failed("boom")) };
        var epic = new FakeProvider("epic").WithInstalled(Game("epic", "a", "Fresh"));
        var service = Service(steam, epic);
        await service.LoadCachedAsync(CancellationToken.None);

        var reports = await service.ScanAsync(CancellationToken.None);

        service.Entries.Select(e => e.Title).ShouldBe(["Cached", "Fresh"]);
        _cache.BySource["steam"].Single().Title.ShouldBe("Cached");
        reports.Single(r => r.Source == "steam").Problems.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ThrowingProvider_IsContained()
    {
        var bad = new FakeProvider("bad") { Installed = _ => throw new InvalidOperationException("bug") };
        var good = new FakeProvider("good").WithInstalled(Game("good", "1", "Works"));
        var service = Service(bad, good);

        var reports = await service.ScanAsync(CancellationToken.None);

        service.Entries.Single().Title.ShouldBe("Works");
        reports.Single(r => r.Source == "bad").Installed.Status.ShouldBe(ProviderStatus.Failed);
    }

    [Fact]
    public async Task SlowProvider_TimesOut_WithoutBlockingOthers()
    {
        var slow = new FakeProvider("slow")
        {
            Installed = async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return ProviderResult.FromScan([], []);
            },
        };
        var fast = new FakeProvider("fast").WithInstalled(Game("fast", "1", "Quick"));
        var service = Service([slow, fast], TimeSpan.FromMilliseconds(100));

        var reports = await service.ScanAsync(CancellationToken.None);

        reports.Single(r => r.Source == "slow").Installed.Status.ShouldBe(ProviderStatus.Failed);
        service.Entries.Single().Title.ShouldBe("Quick");
    }

    [Fact]
    public async Task StoreUninstalled_RemovesItsEntries()
    {
        _cache.BySource["steam"] = [Game("steam", "1", "Old")];
        var steam = new FakeProvider("steam") { Installed = _ => Task.FromResult(ProviderResult.NotAvailable("not installed")) };
        var service = Service(steam);
        await service.LoadCachedAsync(CancellationToken.None);

        await service.ScanAsync(CancellationToken.None);

        service.Entries.ShouldBeEmpty();
        _cache.BySource["steam"].ShouldBeEmpty();
    }

    [Fact]
    public void Combine_InstalledFailedButOwnedOk_KeepsCachedInstalled()
    {
        var report = new ProviderScanReport(
            "steam",
            "Steam",
            ProviderResult.Failed("io"),
            ProviderResult.FromScan([Game("steam", "2", "Owned", installed: false)], []));

        var combined = LibraryService.Combine(report, [Game("steam", "1", "Was installed"), Game("steam", "9", "Old owned", installed: false)]);

        combined!.Select(e => e.Ref.ExternalId).Order().ShouldBe(["1", "2"]);
    }

    [Fact]
    public void Combine_BothFailed_ReturnsNull()
    {
        var report = new ProviderScanReport("steam", "Steam", ProviderResult.Failed("a"), ProviderResult.Failed("b"));

        LibraryService.Combine(report, []).ShouldBeNull();
    }

    [Fact]
    public async Task ScanSource_OnlyRunsThatProvider()
    {
        var steamScans = 0;
        var steam = new FakeProvider("steam")
        {
            Installed = _ =>
            {
                steamScans++;
                return Task.FromResult(ProviderResult.FromScan([Game("steam", "1", "Steam game")], []));
            },
        };
        var manual = new FakeProvider("manual").WithInstalled(Game("manual", "a", "Manual one"));
        var service = Service(steam, manual);
        await service.ScanAsync(CancellationToken.None);

        manual.WithInstalled(Game("manual", "a", "Manual one"), Game("manual", "b", "Manual two"));
        var reports = await service.ScanSourceAsync("manual", CancellationToken.None);

        steamScans.ShouldBe(1);
        reports.Single().Source.ShouldBe("manual");
        service.Entries.Select(e => e.Title).ShouldBe(["Manual one", "Manual two", "Steam game"]);
    }

    [Fact]
    public async Task LoadCached_PublishesCacheContent()
    {
        _cache.BySource["steam"] = [Game("steam", "1", "From cache")];
        var service = Service();

        await service.LoadCachedAsync(CancellationToken.None);

        service.Entries.Single().Title.ShouldBe("From cache");
    }

    private LibraryService Service(params ILibraryProvider[] providers) => Service(providers, null);

    private LibraryService Service(ILibraryProvider[] providers, TimeSpan? timeout) =>
        new(providers, _cache, NullLogger<LibraryService>.Instance, timeout);
}
