// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Providers.Apps;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Apps;

public sealed class StartMenuProviderTests
{
    private readonly FakeKnownFolders _folders = new();
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeShortcuts _shortcuts = new();

    private string Common => _folders.CommonStartMenuPrograms;

    private string User => _folders.UserStartMenuPrograms;

    [Fact]
    public async Task Shortcuts_BecomeAppEntries_WithRelativeIds_AndOpenTheShortcut()
    {
        Link(Common + @"\Sample Tools\Sample Editor.lnk", @"C:\Program Files\Sample\editor.exe");

        var entry = (await Scan()).Entries.ShouldHaveSingleItem();

        entry.Ref.ToString().ShouldBe("startmenu:Sample Tools/Sample Editor.lnk");
        entry.Kind.ShouldBe(ItemKind.App);
        entry.Title.ShouldBe("Sample Editor");
        entry.InstallDirectory.ShouldBe(@"C:\Program Files\Sample");
        entry.Launch.ShouldBe(new LaunchSpec(LaunchKind.Executable, Common + @"\Sample Tools\Sample Editor.lnk"));
        entry.Extra["target"].ShouldBe(@"C:\Program Files\Sample\editor.exe");
    }

    [Fact]
    public async Task Junk_MissingTargets_AndSkippedFolders_AreLeftOut()
    {
        Link(Common + @"\Sample\Uninstall Sample.lnk", @"C:\Program Files\Sample\unins000.exe");
        Link(Common + @"\Sample\Sample Manual.lnk", @"C:\Program Files\Sample\manual.pdf");
        Link(Common + @"\Sample\Gone.lnk", @"C:\Program Files\Gone\gone.exe", createTarget: false);
        Link(Common + @"\Startup\Sample Agent.lnk", @"C:\Program Files\Sample\agent.exe");
        _shortcuts.Targets[Common + @"\Control.lnk"] = new ShortcutTarget(null, null, null);
        _fs.AddFile(Common + @"\Control.lnk", "binary");
        _fs.AddFile(Common + @"\Broken.lnk", "binary"); // unreadable as a shortcut

        (await Scan()).Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task UrlShortcuts_OnlyAppProtocols()
    {
        _fs.AddFixture(Common + @"\Website.url", "startmenu/website.url");
        _fs.AddFixture(Common + @"\Store Game.url", "startmenu/store-game.url");
        _fs.AddFixture(Common + @"\Sample App.url", "startmenu/app-protocol.url");

        var entry = (await Scan()).Entries.ShouldHaveSingleItem();

        entry.Title.ShouldBe("Sample App");
        entry.Extra["url"].ShouldBe("sampleapp://open");
    }

    [Fact]
    public async Task UserCopy_WinsOverAllUsersCopy_AndDuplicateTargetsCollapse()
    {
        Link(Common + @"\Sample.lnk", @"C:\Program Files\Sample\sample.exe");
        Link(User + @"\Sample.lnk", @"C:\Program Files\Sample\sample.exe");
        Link(Common + @"\Sample Folder\Sample.lnk", @"C:\Program Files\Sample\sample.exe");
        Link(Common + @"\Sample Folder\Sample Safe Mode.lnk", @"C:\Program Files\Sample\sample.exe", arguments: "--safe");

        var entries = (await Scan()).Entries;

        entries.Select(e => e.Ref.ExternalId).ShouldBe(["Sample.lnk", "Sample Folder/Sample Safe Mode.lnk"], ignoreOrder: true);
        entries.Single(e => e.Ref.ExternalId == "Sample.lnk").Extra["shortcut"].ShouldBe(User + @"\Sample.lnk");
    }

    [Fact]
    public async Task TranslatedNames_ComeFromDesktopIni()
    {
        Link(Common + @"\Accessories\Character Map.lnk", @"C:\Windows\system32\charmap.exe");
        _fs.AddFixture(Common + @"\Accessories\desktop.ini", "startmenu/desktop.ini");
        var names = new FakeNames { ["@%SystemRoot%\\system32\\shell32.dll,-22020"] = "Mapa de caracteres" };

        var entry = (await Scan(names)).Entries.ShouldHaveSingleItem();

        entry.Title.ShouldBe("Mapa de caracteres");
        entry.Ref.ExternalId.ShouldBe("Accessories/Character Map.lnk"); // the id stays the file path
    }

    [Fact]
    public async Task NoStartMenu_IsAnEmptyOkScan()
    {
        var result = await Scan();

        result.Status.ShouldBe(ProviderStatus.Ok);
        result.Entries.ShouldBeEmpty();
    }

    private Task<ProviderResult> Scan(IIndirectStrings? names = null) =>
        new StartMenuProvider(_folders, _fs, _shortcuts, names).ScanInstalledAsync(CancellationToken.None);

    private void Link(string path, string target, bool createTarget = true, string? arguments = null)
    {
        _fs.AddFile(path, "binary");
        _shortcuts.Targets[path] = new ShortcutTarget(target, arguments, null);
        if (createTarget)
        {
            _fs.AddFile(target, "exe");
        }
    }

    private sealed class FakeShortcuts : IShortcutReader
    {
        public Dictionary<string, ShortcutTarget> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ShortcutTarget? Read(string shortcutPath) => Targets.GetValueOrDefault(shortcutPath);
    }

    private sealed class FakeNames : Dictionary<string, string>, IIndirectStrings
    {
        public string? Resolve(string indirect) => TryGetValue(indirect, out var value) ? value : null;
    }
}
