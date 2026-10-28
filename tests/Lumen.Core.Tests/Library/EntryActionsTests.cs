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

    [Theory]
    [InlineData("steam", "1000010", "steam://uninstall/1000010", UninstallRoute.Store)]
    [InlineData("ubisoft", "4932", "uplay://uninstall/4932", UninstallRoute.Store)]
    [InlineData("epic", "SampleApp", "com.epicgames.launcher://store/library", UninstallRoute.StoreLibrary)]
    [InlineData("gog", "1", "ms-settings:appsfeatures", UninstallRoute.WindowsSettings)]
    [InlineData("ea", "Sample.Id", "ms-settings:appsfeatures", UninstallRoute.WindowsSettings)]
    [InlineData("battlenet", "s1", "ms-settings:appsfeatures", UninstallRoute.WindowsSettings)]
    [InlineData("xbox", "Contoso.Game_abc!App", "ms-settings:appsfeatures", UninstallRoute.WindowsSettings)]
    public void Uninstall_IsHandedToTheStoreOrWindows(string source, string id, string uri, UninstallRoute route)
    {
        EntryActions.Uninstall(Game(source, id, "Sample")).ShouldBe(new UninstallAction(new LaunchSpec(LaunchKind.Uri, uri), route));
    }

    [Theory]
    [InlineData("steam", "1", false)]
    [InlineData("manual", "00000000-0000-0000-0000-000000000000", true)]
    [InlineData("steam", "1&evil", true)]
    [InlineData("ubisoft", "../x", true)]
    public void Uninstall_NotOffered_WhenNotInstalledManualOrMalformed(string source, string id, bool installed)
    {
        EntryActions.Uninstall(Game(source, id, "Sample", installed)).ShouldBeNull();
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

        public IReadOnlyList<string> GetDirectories(string directory) => [];

        public Task<string> ReadAllTextAsync(string path, CancellationToken ct) => throw new FileNotFoundException();
    }
}
