// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class EntryActionsTests
{
    [Fact]
    public void SteamStorePage_OpensInTheSteamClient()
    {
        EntryActions.StorePage(Game("steam", "1000010", "Sample"))
            .ShouldBe(new LaunchSpec(LaunchKind.Uri, "steam://store/1000010"));
    }

    [Fact]
    public void XboxStorePage_UsesThePackageFamilyName()
    {
        var entry = Game("xbox", "Contoso.Game_abc!App", "Sample") with
        {
            Extra = new Dictionary<string, string> { ["packageFamilyName"] = "Contoso.Game_abc" },
        };

        EntryActions.StorePage(entry).ShouldBe(new LaunchSpec(LaunchKind.Uri, "ms-windows-store://pdp/?PFN=Contoso.Game_abc"));
    }

    [Theory]
    [InlineData("epic", "SampleApp")]
    [InlineData("gog", "1")]
    [InlineData("manual", "00000000-0000-0000-0000-000000000000")]
    [InlineData("steam", "not-a-number")]
    public void UnsupportedOrMalformed_HasNoStorePage(string source, string id)
    {
        EntryActions.StorePage(Game(source, id, "Sample")).ShouldBeNull();
    }

    [Fact]
    public void XboxWithSuspiciousFamilyName_HasNoStorePage()
    {
        var entry = Game("xbox", "x!App", "Sample") with
        {
            Extra = new Dictionary<string, string> { ["packageFamilyName"] = "evil&cmd=calc" },
        };

        EntryActions.StorePage(entry).ShouldBeNull();
    }

    [Fact]
    public void OpenFolder_QuotesThePath_AndRequiresItToExist()
    {
        var entry = Game("steam", "1", "Sample") with { InstallDirectory = @"C:\Games\Sample Game\" };

        EntryActions.OpenFolder(entry, new Dirs(@"C:\Games\Sample Game\"))
            .ShouldBe(new LaunchSpec(LaunchKind.ProcessWithArgs, "explorer.exe", @"""C:\Games\Sample Game"""));
        EntryActions.OpenFolder(entry, new Dirs()).ShouldBeNull();
        EntryActions.OpenFolder(entry with { InstallDirectory = null }, new Dirs(@"C:\x")).ShouldBeNull();
    }

    private sealed class Dirs(params string[] existing) : IFileSystem
    {
        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path) => existing.Contains(path);

        public IReadOnlyList<string> GetFiles(string directory, string pattern) => [];

        public Task<string> ReadAllTextAsync(string path, CancellationToken ct) => throw new FileNotFoundException();
    }
}
