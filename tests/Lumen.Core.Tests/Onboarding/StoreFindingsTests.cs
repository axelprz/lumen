// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Onboarding;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Onboarding;

public sealed class StoreFindingsTests
{
    private static readonly ProviderResult NoOwned = ProviderResult.NotConfigured("no login");

    [Fact]
    public void EachStore_IsSummarized_FoundFirst_ThenEmpty_Problem_NotInstalled()
    {
        var reports = new[]
        {
            Report("gog", ProviderResult.NotAvailable("GOG Galaxy is not installed.")),
            Report("epic", ProviderResult.FromScan([], [])),
            Report("ea", ProviderResult.Failed("EA: could not read.")),
            Report("steam", ProviderResult.FromScan([Game("steam", "1", "A"), Game("steam", "2", "B")], [])),
            Report("ubisoft", ProviderResult.FromScan([Game("ubisoft", "4", "C")], [])),
        };

        StoreFindings.From(reports).Select(f => (f.Source, f.State, f.Installed)).ShouldBe(
        [
            ("steam", StoreState.Found, 2),
            ("ubisoft", StoreState.Found, 1),
            ("epic", StoreState.Empty, 0),
            ("ea", StoreState.Problem, 0),
            ("gog", StoreState.NotInstalled, 0),
        ]);
    }

    [Fact]
    public void OwnedButNotInstalled_IsCounted_WithoutDoubleCountingInstalledOnes()
    {
        var installed = Game("steam", "1", "A");
        var owned = ProviderResult.FromScan([Game("steam", "1", "A", installed: false), Game("steam", "2", "B", installed: false)], []);

        var finding = StoreFindings.From([Report("steam", ProviderResult.FromScan([installed], []), owned)]).Single();

        finding.Installed.ShouldBe(1);
        finding.OwnedNotInstalled.ShouldBe(1);
    }

    [Fact]
    public void ManualItems_AreNotAStore()
    {
        StoreFindings.From([Report("manual", ProviderResult.FromScan([Game("manual", "x", "Mine")], []))]).ShouldBeEmpty();
    }

    private static ProviderScanReport Report(string source, ProviderResult installed, ProviderResult? owned = null) =>
        new(source, source.ToUpperInvariant(), installed, owned ?? NoOwned);
}
