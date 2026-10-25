// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Providers.Manual;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Manual;

public sealed class ManualItemsProviderTests
{
    private readonly InMemoryManualStore _store = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task ExistingExe_IsInstalled_WithArgsAndWorkingDirectory()
    {
        _fs.AddFile(@"C:\Games\Sample\sample.exe", "MZ");
        var item = new ManualItem(Guid.NewGuid(), ItemKind.Game, "Sample", @"C:\Games\Sample\sample.exe", null, "-windowed");
        _store.Items.Add(item);

        var entry = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single();

        entry.Ref.ShouldBe(item.Ref);
        entry.State.ShouldBe(InstallState.Installed);
        entry.InstallDirectory.ShouldBe(@"C:\Games\Sample");
        entry.Launch.ShouldBe(new LaunchSpec(LaunchKind.Executable, @"C:\Games\Sample\sample.exe", "-windowed", @"C:\Games\Sample"));
        entry.Install.ShouldBeNull();
    }

    [Fact]
    public async Task Shortcut_LaunchesWithoutArgs()
    {
        _fs.AddFile(@"C:\Links\tool.lnk", "L");
        _store.Items.Add(new ManualItem(Guid.NewGuid(), ItemKind.App, "Tool", @"C:\Links\tool.lnk", null, null));

        var entry = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single();

        entry.Launch.ShouldBe(new LaunchSpec(LaunchKind.Executable, @"C:\Links\tool.lnk"));
        entry.InstallDirectory.ShouldBeNull();
        entry.Kind.ShouldBe(ItemKind.App);
    }

    [Fact]
    public async Task MissingFile_IsListedButNotLaunchable()
    {
        _store.Items.Add(new ManualItem(Guid.NewGuid(), ItemKind.Game, "Gone", @"E:\Removed\gone.exe", null, null));

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);
        var entry = result.Entries.Single();

        result.Status.ShouldBe(ProviderStatus.Ok);
        entry.State.ShouldBe(InstallState.NotInstalled);
        entry.Launch.ShouldBeNull();
        entry.Install.ShouldBeNull();
        LaunchService.PrimaryAction(entry).ShouldBeNull();
    }

    [Fact]
    public async Task Url_IsAlwaysAvailable()
    {
        _store.Items.Add(new ManualItem(Guid.NewGuid(), ItemKind.App, "Site", null, "https://example.com", null));

        var entry = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single();

        entry.State.ShouldBe(InstallState.Installed);
        entry.Launch.ShouldBe(new LaunchSpec(LaunchKind.Uri, "https://example.com"));
    }

    [Fact]
    public async Task StoreFailure_IsReportedNotThrown()
    {
        _store.Throw = true;

        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.Failed);
    }

    private ManualItemsProvider Provider() => new(_store, _fs);

    private sealed class InMemoryManualStore : IManualItemStore
    {
        public List<ManualItem> Items { get; } = [];

        public bool Throw { get; set; }

        public Task<IReadOnlyList<ManualItem>> GetAllAsync(CancellationToken ct) =>
            Throw ? throw new IOException("database is locked") : Task.FromResult<IReadOnlyList<ManualItem>>(Items);

        public Task SaveAsync(ManualItem item, CancellationToken ct) => Task.CompletedTask;

        public Task RemoveAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
    }
}
