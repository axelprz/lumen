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
    public async Task Items_MergeAcrossStores_AndFollowRuleChanges()
    {
        var steam = new FakeProvider("steam").WithInstalled(Game("steam", "1", "Sample"));
        var epic = new FakeProvider("epic").WithInstalled(Game("epic", "x", "Sample"));
        var rules = new InMemoryRules();
        var service = new LibraryService([steam, epic], _cache, NullLogger<LibraryService>.Instance, ruleStore: rules);
        await service.ScanAsync(CancellationToken.None);

        service.Items.ShouldHaveSingleItem().IsMerged.ShouldBeTrue();
        service.Entries.Count.ShouldBe(2);

        rules.Rules = new MergeRules(new HashSet<ItemRef> { new("steam", "1"), new("epic", "x") }, new HashSet<ItemRef>());
        await service.ReloadUserChoicesAsync(CancellationToken.None);

        service.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RescanWithNoChanges_DoesNotRaiseChanged()
    {
        var steam = new FakeProvider("steam").WithInstalled(Game("steam", "1", "Sample") with
        {
            Extra = new Dictionary<string, string> { ["appid"] = "1" },
        });
        var service = Service(steam);
        await service.ScanAsync(CancellationToken.None);
        var changed = 0;
        service.Changed += (_, _) => changed++;

        // A fresh scan builds new objects (new Extra dictionary) with the same content.
        steam.WithInstalled(Game("steam", "1", "Sample") with { Extra = new Dictionary<string, string> { ["appid"] = "1" } });
        await service.ScanAsync(CancellationToken.None);
        changed.ShouldBe(0);

        steam.WithInstalled(Game("steam", "1", "Sample", installed: false));
        await service.ScanAsync(CancellationToken.None);
        changed.ShouldBe(1);
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

    [Fact]
    public async Task Overrides_RenameAndHide_AndFollowChanges()
    {
        var steam = new FakeProvider("steam").WithInstalled(Game("steam", "1", "Alpha"), Game("steam", "2", "Beta"));
        var overrides = new InMemoryOverrides();
        var service = new LibraryService([steam], _cache, NullLogger<LibraryService>.Instance, overrideStore: overrides);
        await service.ScanAsync(CancellationToken.None);

        await overrides.SetTitleAsync([new ItemRef("steam", "1")], "  Zulu  ", CancellationToken.None);
        await overrides.SetHiddenAsync([new ItemRef("steam", "2")], true, CancellationToken.None);
        await service.ReloadUserChoicesAsync(CancellationToken.None);

        service.Items.Select(i => (i.Title, i.IsHidden)).ShouldBe([("Beta", true), ("Zulu", false)]);
        service.Items[1].OriginalTitle.ShouldBe("Alpha");

        await overrides.SetTitleAsync([new ItemRef("steam", "1")], null, CancellationToken.None);
        await service.ReloadUserChoicesAsync(CancellationToken.None);

        service.Items.Select(i => i.Title).ShouldBe(["Alpha", "Beta"]);
    }

    [Fact]
    public void ApplyOverrides_PrimaryTitleWins_AndAnyHiddenEntryHidesTheItem()
    {
        var steam = Game("steam", "1", "Sample");
        var epic = Game("epic", "x", "Sample");
        var item = new LibraryItem([steam, epic], steam);
        var overrides = new Dictionary<ItemRef, ItemOverride>
        {
            [epic.Ref] = new("From Epic", Hidden: true),
            [steam.Ref] = new("From Steam", Hidden: false),
        };

        var result = LibraryService.ApplyOverrides(item, overrides);

        result.Title.ShouldBe("From Steam");
        result.IsHidden.ShouldBeTrue();
        LibraryService.ApplyOverrides(item, new Dictionary<ItemRef, ItemOverride>()).ShouldBeSameAs(item);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Name ", "Name")]
    public void CleanTitle_TrimsAndTreatsEmptyAsNone(string? input, string? expected)
    {
        ItemOverride.CleanTitle(input).ShouldBe(expected);
    }

    [Fact]
    public void CleanTitle_CapsTheLength()
    {
        ItemOverride.CleanTitle(new string('a', 300))!.Length.ShouldBe(ItemOverride.MaxTitleLength);
    }

    [Fact]
    public async Task FullScans_AreRemembered_ScansOfOneStoreAreNot()
    {
        var service = Service(new FakeProvider("steam").WithInstalled(Game("steam", "1", "A")), new FakeProvider("manual"));
        var completed = 0;
        service.FullScanCompleted += (_, _) => completed++;
        service.LastFullScan.ShouldBeNull();

        await service.ScanAsync(CancellationToken.None);
        await service.ScanSourceAsync("manual", CancellationToken.None);

        completed.ShouldBe(1);
        service.LastFullScan.ShouldNotBeNull().Select(r => r.Source).ShouldBe(["steam", "manual"]);
    }

    [Fact]
    public async Task DetectedApps_ReachItemsOnlyOnceAdded()
    {
        var app = Game("startmenu", "Sample.lnk", "Sample App") with { Kind = ItemKind.App };
        var choices = new InMemoryAppChoices();
        var service = new LibraryService(
            [new FakeProvider("startmenu").WithInstalled(app), new FakeProvider("steam").WithInstalled(Game("steam", "1", "Game"))],
            _cache,
            NullLogger<LibraryService>.Instance,
            appChoiceStore: choices);
        await service.ScanAsync(CancellationToken.None);

        service.Entries.Count.ShouldBe(2);
        service.Items.Select(i => i.Title).ShouldBe(["Game"]);

        await choices.SetAsync([app.Ref], AppChoice.Added, CancellationToken.None);
        await service.ReloadUserChoicesAsync(CancellationToken.None);

        service.Items.Select(i => i.Title).ShouldBe(["Game", "Sample App"]);
        service.AppChoices[app.Ref].ShouldBe(AppChoice.Added);
    }

    [Theory]
    [InlineData(null, "1790000000")]
    [InlineData("1780000000", "1790000000")]
    [InlineData("1795000000", "1795000000")]
    public void InstalledGame_GainsPlaytime_AndTheLatestLastPlayed_FromTheOwnedList(string? localLast, string expectedLast)
    {
        var localExtra = new Dictionary<string, string> { ["appid"] = "1", ["library"] = @"C:\Steam" };
        if (localLast is not null)
        {
            localExtra["lastPlayed"] = localLast;
        }

        var installed = Game("steam", "1", "Sample") with { Extra = localExtra };
        var owned = Game("steam", "1", "Sample (owned name)", installed: false) with
        {
            Extra = new Dictionary<string, string> { ["appid"] = "1", ["playtimeMinutes"] = "600", ["lastPlayed"] = "1790000000" },
        };
        var report = new ProviderScanReport("steam", "Steam", ProviderResult.FromScan([installed], []), ProviderResult.FromScan([owned], []));

        var entry = LibraryService.Combine(report, []).ShouldNotBeNull().ShouldHaveSingleItem();

        entry.IsInstalled.ShouldBeTrue();
        entry.Title.ShouldBe("Sample");
        entry.Extra["library"].ShouldBe(@"C:\Steam");
        entry.Extra["playtimeMinutes"].ShouldBe("600");
        entry.Extra["lastPlayed"].ShouldBe(expectedLast);
    }

    [Fact]
    public async Task LumenPlaytime_IsAddedToEntries_AndReplacedOnReload()
    {
        var store = new MemoryTotals();
        store.Totals[new ItemRef("epic", "x")] = new PlaytimeTotal(90, DateTimeOffset.FromUnixTimeSeconds(1795000000));
        var service = new LibraryService([new FakeProvider("epic").WithInstalled(Game("epic", "x", "Sample"))], _cache, NullLogger<LibraryService>.Instance, playtimeStore: store);
        await service.LoadCachedAsync(CancellationToken.None);
        await service.ScanAsync(CancellationToken.None);

        LibraryFilter.PlaytimeMinutes(service.Items.Single()).ShouldBe(90);

        store.Totals[new ItemRef("epic", "x")] = new PlaytimeTotal(150, DateTimeOffset.FromUnixTimeSeconds(1796000000));
        await service.ReloadUserChoicesAsync(CancellationToken.None);

        LibraryFilter.PlaytimeMinutes(service.Items.Single()).ShouldBe(150);
        LibraryFilter.LastPlayed(service.Items.Single()).ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1796000000));
    }

    private sealed class MemoryTotals : IPlaytimeStore
    {
        public Dictionary<ItemRef, PlaytimeTotal> Totals { get; } = [];

        public Task<long> StartAsync(ItemRef itemRef, DateTimeOffset start, CancellationToken ct) => Task.FromResult(1L);

        public Task ExtendAsync(long sessionId, DateTimeOffset lastSeen, CancellationToken ct) => Task.CompletedTask;

        public Task FinishAsync(long sessionId, DateTimeOffset end, TimeSpan minimum, CancellationToken ct) => Task.CompletedTask;

        public Task CloseLeftoversAsync(TimeSpan minimum, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<ItemRef, PlaytimeTotal>> LoadTotalsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<ItemRef, PlaytimeTotal>>(new Dictionary<ItemRef, PlaytimeTotal>(Totals));
    }
}
