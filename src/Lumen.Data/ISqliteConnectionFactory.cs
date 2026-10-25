// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Microsoft.Data.Sqlite;

namespace Lumen.Data;

public interface ISqliteConnectionFactory
{
    /// <summary>Opens a new connection with Lumen's pragmas applied. The caller disposes it.</summary>
    Task<SqliteConnection> OpenAsync(CancellationToken ct);
}
