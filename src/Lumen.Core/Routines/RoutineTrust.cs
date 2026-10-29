// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Routines;

/// <summary>
/// Approved routines on this PC (SQLite <c>routine_trust</c>, never synced): routine id → hash of the approved
/// content. A routine is trusted only while its file still has that hash; any change made outside Lumen (another
/// PC through the sync folder, an imported file, a text editor) needs a new review.
/// </summary>
public interface IRoutineTrustStore
{
    Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct);

    Task ApproveAsync(string routineId, string hash, CancellationToken ct);

    Task ForgetAsync(string routineId, CancellationToken ct);
}

public enum RoutineTrustState
{
    /// <summary>Approved and unchanged since: can run.</summary>
    Trusted,

    /// <summary>New, or changed outside Lumen: needs review before it can run.</summary>
    NeedsReview,

    /// <summary>The file has errors or a blocked step: can never run as it is.</summary>
    Invalid,
}
