// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Data.Migrations;

/// <summary>The database was created by a newer version of Lumen than the one running.</summary>
public sealed class DatabaseVersionException : Exception
{
    public DatabaseVersionException()
    {
    }

    public DatabaseVersionException(string message)
        : base(message)
    {
    }

    public DatabaseVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DatabaseVersionException(int databaseVersion, int supportedVersion)
        : base($"The database schema version ({databaseVersion}) is newer than this Lumen supports ({supportedVersion}).")
    {
        DatabaseVersion = databaseVersion;
        SupportedVersion = supportedVersion;
    }

    public int DatabaseVersion { get; }

    public int SupportedVersion { get; }
}
