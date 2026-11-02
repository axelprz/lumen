// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Notifications;

namespace Lumen.Core.Tests.Notifications;

public sealed class NotificationCenterTests
{
    private static void Post(NotificationCenter center, string key, NotificationKind kind = NotificationKind.Update) =>
        center.Post(key, kind, () => "title " + key, () => "message " + key);

    [Fact]
    public void Post_PutsTheNewestFirst_AndReplacesTheSameKey()
    {
        var center = new NotificationCenter();
        Post(center, "a");
        Post(center, "b");
        Post(center, "a");

        center.Items.Select(n => n.Key).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Post_KeepsAtMostMaxItems_DroppingTheOldest()
    {
        var center = new NotificationCenter();
        for (var i = 0; i < NotificationCenter.MaxItems + 5; i++)
        {
            Post(center, "k" + i);
        }

        center.Items.Count.ShouldBe(NotificationCenter.MaxItems);
        center.Items[0].Key.ShouldBe("k" + (NotificationCenter.MaxItems + 4));
        center.Contains("k0").ShouldBeFalse();
    }

    [Fact]
    public void RemoveAndClear_RaiseChangedOnlyWhenSomethingChanged()
    {
        var center = new NotificationCenter();
        var changes = 0;
        center.Changed += (_, _) => changes++;
        Post(center, "a");

        center.Remove("missing").ShouldBeFalse();
        center.Remove("a").ShouldBeTrue();
        center.Clear();
        Post(center, "b");
        center.Clear();

        changes.ShouldBe(4); // post a, remove a, post b, clear b; removing a missing key and clearing an empty list are silent
    }

    [Fact]
    public void Texts_AreWrittenOnRead_SoTheyFollowTheLanguage()
    {
        var center = new NotificationCenter();
        var language = "en";
        center.Post("a", NotificationKind.Sync, () => language == "en" ? "Hello" : "Hola", () => "m");

        center.Items[0].Title().ShouldBe("Hello");
        language = "es";
        center.Items[0].Title().ShouldBe("Hola");
    }

    [Fact]
    public void Post_RecordsTheTimeFromTheClock()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var center = new NotificationCenter(new FixedTime(now));

        Post(center, "a");

        center.Items[0].At.ShouldBe(now);
    }

    [Fact]
    public async Task ConcurrentPosts_DoNotLoseOrDuplicateEntries()
    {
        var center = new NotificationCenter();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => Post(center, "k" + i))));

        center.Items.Select(n => n.Key).Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public void StoreProblems_OnlyWarningsAndErrorsCount_AndNonStoresAreSkipped()
    {
        var ok = Report("steam", "Steam", ProviderResult.FromScan([], []), ProviderResult.NotConfigured("no key"));
        var warned = Report(
            "gog",
            "GOG",
            ProviderResult.FromScan([], []),
            new ProviderResult(ProviderStatus.Partial, [], [new ProviderDiagnostic(DiagnosticSeverity.Warning, "private profile")]));
        var failed = Report("epic", "Epic", ProviderResult.Failed("boom"), ProviderResult.NotConfigured("no login"));
        var manual = Report("manual", "Manual", ProviderResult.Failed("x"), ProviderResult.FromScan([], []));

        var result = StoreProblems.From([ok, warned, failed, manual]);

        result.Select(p => (p.Source, p.HasProblem)).ShouldBe([("steam", false), ("gog", true), ("epic", true)]);
        StoreProblems.KeyFor("gog").ShouldBe("store:gog");
    }

    private static ProviderScanReport Report(string source, string name, ProviderResult installed, ProviderResult owned) =>
        new(source, name, installed, owned);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
