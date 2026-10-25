// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Reflection;

namespace Lumen.Core.Tests.RepositoryChecks;

internal static class RepoRoot
{
    private const string SolutionFile = "Lumen.slnx";

    /// <summary>
    /// The directory containing Lumen.slnx: taken from build metadata (works when build output lives outside
    /// the repo), falling back to walking up from the test output folder.
    /// </summary>
    public static string Find()
    {
        var fromBuild = typeof(RepoRoot).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LumenRepoRoot")?.Value;
        if (fromBuild is not null && File.Exists(Path.Combine(fromBuild, SolutionFile)))
        {
            return fromBuild;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find the repository root ({SolutionFile}).");
    }
}
