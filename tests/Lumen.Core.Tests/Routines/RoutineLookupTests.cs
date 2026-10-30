// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Routines;

namespace Lumen.Core.Tests.Routines;

public sealed class RoutineLookupTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Usable_SkipsBrokenFiles_AndSortsByName()
    {
        var entries = new[] { Entry("zeta", "Zeta"), Entry("alpha", "Alpha", RoutineTrustState.NeedsReview), Broken("bad") };

        var usable = RoutineLookup.Usable(entries, English);

        usable.Select(e => e.Id).ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public void Pinned_OnlyPinnedAndUsable()
    {
        var entries = new[] { Entry("a", "A", pinned: true), Entry("b", "B"), Broken("c") };

        RoutineLookup.Pinned(entries, English).Select(e => e.Id).ShouldBe(["a"]);
    }

    [Fact]
    public void Find_EmptyText_FindsNothing() =>
        RoutineLookup.Find([Entry("a", "Gaming night")], "  ", English).ShouldBeEmpty();

    [Fact]
    public void Find_IgnoresCaseAndAccents_AndListsNamesStartingWithTheTextFirst()
    {
        var entries = new[] { Entry("one", "Prepará la noche"), Entry("two", "Noche de juegos"), Entry("three", "Otra cosa") };

        RoutineLookup.Find(entries, "NOCHE", English).Select(e => e.Id).ShouldBe(["two", "one"]);
        RoutineLookup.Find(entries, "prepara", English).Select(e => e.Id).ShouldBe(["one"]);
    }

    [Fact]
    public void Find_RespectsTheLimit() =>
        RoutineLookup.Find([Entry("a", "Go 1"), Entry("b", "Go 2"), Entry("c", "Go 3")], "go", English, max: 2).Count.ShouldBe(2);

    private static RoutineEntry Entry(string id, string name, RoutineTrustState state = RoutineTrustState.Trusted, bool pinned = false)
    {
        var routine = new Routine(id, new RoutineName(name), [new UrlAction("https://example.com/")]) { Pinned = pinned };
        return new RoutineEntry($"/r/{id}.json", id, new RoutineParseResult(routine, [], "hash"), state, null);
    }

    private static RoutineEntry Broken(string id) =>
        new($"/r/{id}.json", id, new RoutineParseResult(null, ["bad"], null), RoutineTrustState.Invalid, null);
}
