// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core;

/// <summary>
/// Well-known locations used by Lumen. Pure: it only combines paths and never touches the disk.
/// The OS-specific base directories are supplied by the platform layer.
/// </summary>
public sealed record LumenPaths
{
    public const string AppFolderName = "Lumen";

    public LumenPaths(string localDataDirectory, string roamingDataDirectory)
    {
        LocalDataDirectory = RequireRooted(localDataDirectory, nameof(localDataDirectory));
        RoamingDataDirectory = RequireRooted(roamingDataDirectory, nameof(roamingDataDirectory));
    }

    /// <summary>Machine-local data (database, logs, caches). On Windows: <c>%LOCALAPPDATA%\Lumen</c>.</summary>
    public string LocalDataDirectory { get; }

    /// <summary>Per-user roaming data. On Windows: <c>%APPDATA%\Lumen</c>.</summary>
    public string RoamingDataDirectory { get; }

    public string DatabasePath => Path.Combine(LocalDataDirectory, "lumen.db");

    public string LogsDirectory => Path.Combine(LocalDataDirectory, "logs");

    public string ArtworkCacheDirectory => Path.Combine(LocalDataDirectory, "cache", "artwork");

    /// <summary>Where portable JSON lives when the user has not chosen a sync folder.</summary>
    public string DefaultPortableDirectory => Path.Combine(RoamingDataDirectory, "portable");

    /// <summary>Builds the paths from the OS base folders (e.g. LocalApplicationData and ApplicationData).</summary>
    public static LumenPaths FromBaseDirectories(string localBaseDirectory, string roamingBaseDirectory) =>
        new(
            Path.Combine(RequireRooted(localBaseDirectory, nameof(localBaseDirectory)), AppFolderName),
            Path.Combine(RequireRooted(roamingBaseDirectory, nameof(roamingBaseDirectory)), AppFolderName));

    private static string RequireRooted(string path, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("Path must be absolute.", paramName);
        }

        return path;
    }
}
