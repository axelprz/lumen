// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core;

namespace Lumen.Core.Tests;

public sealed class LumenPathsTests
{
    private static readonly string _root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly string _local = Path.Combine(_root, "users", "someone", "local");
    private static readonly string _roaming = Path.Combine(_root, "users", "someone", "roaming");

    [Fact]
    public void FromBaseDirectories_AppendsAppFolder()
    {
        var paths = LumenPaths.FromBaseDirectories(_local, _roaming);

        paths.LocalDataDirectory.ShouldBe(Path.Combine(_local, "Lumen"));
        paths.RoamingDataDirectory.ShouldBe(Path.Combine(_roaming, "Lumen"));
    }

    [Fact]
    public void DerivedPaths_LiveUnderTheRightRoot()
    {
        var paths = LumenPaths.FromBaseDirectories(_local, _roaming);
        var localRoot = Path.Combine(_local, "Lumen");

        paths.DatabasePath.ShouldBe(Path.Combine(localRoot, "lumen.db"));
        paths.LogsDirectory.ShouldBe(Path.Combine(localRoot, "logs"));
        paths.ArtworkCacheDirectory.ShouldBe(Path.Combine(localRoot, "cache", "artwork"));
        paths.DefaultPortableDirectory.ShouldBe(Path.Combine(_roaming, "Lumen", "portable"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public void RejectsEmptyOrRelativePaths(string bad)
    {
        Should.Throw<ArgumentException>(() => new LumenPaths(bad, _roaming));
        Should.Throw<ArgumentException>(() => LumenPaths.FromBaseDirectories(_local, bad));
    }
}
