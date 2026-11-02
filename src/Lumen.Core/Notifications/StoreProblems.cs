// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Onboarding;

namespace Lumen.Core.Notifications;

/// <summary>Whether one store had a warning or an error in a scan.</summary>
public sealed record StoreProblem(string Source, string DisplayName, bool HasProblem);

public static class StoreProblems
{
    /// <summary>Notification key of a store's problem.</summary>
    public static string KeyFor(string source) => "store:" + source;

    /// <summary>
    /// One entry per store of a full scan. Only warnings and errors count (a store that isn't installed or isn't set up
    /// reports information, not a problem). Manual items and detected apps are not stores.
    /// </summary>
    public static IReadOnlyList<StoreProblem> From(IReadOnlyList<ProviderScanReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return reports
            .Where(r => r.Source != StoreFindings.ManualSource && !AppCuration.IsCandidateSource(r.Source))
            .Select(r => new StoreProblem(r.Source, r.DisplayName, r.Problems.Any()))
            .ToList();
    }
}
