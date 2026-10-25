// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Microsoft.Data.Sqlite;

namespace Lumen.Data.Tests;

/// <summary>A database file in its own temp folder, deleted on dispose.</summary>
internal sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"lumen-tests", Guid.NewGuid().ToString("N"));
        // The factory must create missing folders itself, so only the path is chosen here.
        Path = System.IO.Path.Combine(Directory, "nested", "lumen.db");
        Factory = new SqliteConnectionFactory(Path);
    }

    public string Directory { get; }

    public string Path { get; }

    public SqliteConnectionFactory Factory { get; }

    public void Dispose()
    {
        // Pooled connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
