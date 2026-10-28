// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Onboarding;

/// <summary>Declared in the order the wizard lists stores.</summary>
public enum StoreState
{
    /// <summary>The store app is installed and has games installed.</summary>
    Found,

    /// <summary>The store app is installed but has no games installed.</summary>
    Empty,

    /// <summary>Lumen couldn't read it (see the log); it may still work after a rescan.</summary>
    Problem,

    /// <summary>The store app isn't on this PC.</summary>
    NotInstalled,
}

/// <summary>What the first-run wizard says about one store.</summary>
/// <param name="OwnedNotInstalled">Games owned but not installed (Steam with a key), shown as extra info.</param>
public sealed record StoreFinding(string Source, string DisplayName, StoreState State, int Installed, int OwnedNotInstalled);

public static class StoreFindings
{
    /// <summary>Manual items are the user's own, not a store.</summary>
    public const string ManualSource = "manual";

    /// <summary>
    /// Summarizes a full scan per store: found stores first (most games first), then empty, problems, and stores that
    /// aren't installed, each group by name.
    /// </summary>
    public static IReadOnlyList<StoreFinding> From(IReadOnlyList<ProviderScanReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return reports
            .Where(r => r.Source != ManualSource)
            .Select(Summarize)
            .OrderBy(f => f.State)
            .ThenByDescending(f => f.Installed)
            .ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static StoreFinding Summarize(ProviderScanReport report)
    {
        var installed = report.Installed.Entries.Count(e => e.IsInstalled);
        var owned = report.Owned.IsUsable ? report.Owned.Entries.Count(e => !report.Installed.Entries.Any(i => i.Ref == e.Ref)) : 0;
        var state = report.Installed.Status switch
        {
            ProviderStatus.NotAvailable => StoreState.NotInstalled,
            ProviderStatus.Failed => StoreState.Problem,
            _ when installed > 0 => StoreState.Found,
            _ => StoreState.Empty,
        };
        return new StoreFinding(report.Source, report.DisplayName, state, installed, owned);
    }
}
