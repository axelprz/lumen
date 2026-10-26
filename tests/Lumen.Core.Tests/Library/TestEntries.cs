// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Tests.Library;

internal static class TestEntries
{
    public static LibraryEntry Game(string source, string id, string title, bool installed = true) =>
        new(
            new ItemRef(source, id),
            ItemKind.Game,
            title,
            installed ? InstallState.Installed : InstallState.NotInstalled,
            installed ? $@"C:\Games\{title}" : null,
            new LaunchSpec(LaunchKind.Uri, $"{source}://run/{id}"),
            new LaunchSpec(LaunchKind.Uri, $"{source}://install/{id}"),
            LibraryEntry.NoExtra);
}

internal sealed class FakeProvider(string source) : ILibraryProvider
{
    public Func<CancellationToken, Task<ProviderResult>> Installed { get; set; } =
        _ => Task.FromResult(ProviderResult.FromScan([], []));

    public Func<CancellationToken, Task<ProviderResult>> Owned { get; set; } =
        _ => Task.FromResult(ProviderResult.NotConfigured("no key"));

    public string Source => source;

    public string DisplayName => source.ToUpperInvariant();

    public ProviderCapabilities Capabilities { get; set; } = ProviderCapabilities.InstalledScan | ProviderCapabilities.OwnedLibrary;

    public Task<ProviderResult> ScanInstalledAsync(CancellationToken ct) => Installed(ct);

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) => Owned(ct);

    public FakeProvider WithInstalled(params LibraryEntry[] entries)
    {
        Installed = _ => Task.FromResult(ProviderResult.FromScan(entries, []));
        return this;
    }

    public FakeProvider WithOwned(params LibraryEntry[] entries)
    {
        Owned = _ => Task.FromResult(ProviderResult.FromScan(entries, []));
        return this;
    }
}

internal sealed class InMemoryRules : IMergeRuleStore
{
    public MergeRules Rules { get; set; } = MergeRules.None;

    public Task<MergeRules> LoadAsync(CancellationToken ct) => Task.FromResult(Rules);

    public Task SeparateAsync(IEnumerable<ItemRef> refs, CancellationToken ct) => Task.CompletedTask;

    public Task SetPreferredAsync(ItemRef preferred, IEnumerable<ItemRef> others, CancellationToken ct) => Task.CompletedTask;

    public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
}

internal sealed class InMemoryCache : ILibraryCache
{
    public Dictionary<string, IReadOnlyList<LibraryEntry>> BySource { get; } = [];

    public int Writes { get; private set; }

    public Task<IReadOnlyList<LibraryEntry>> LoadAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryEntry>>(BySource.Values.SelectMany(v => v).ToList());

    public Dictionary<ItemRef, DateTimeOffset> FirstSeen { get; } = [];

    public Task ReplaceSourceAsync(string source, IReadOnlyList<LibraryEntry> entries, CancellationToken ct)
    {
        BySource[source] = entries;
        foreach (var entry in entries)
        {
            FirstSeen.TryAdd(entry.Ref, DateTimeOffset.UtcNow);
        }

        Writes++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<ItemRef, DateTimeOffset>> LoadFirstSeenAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<ItemRef, DateTimeOffset>>(new Dictionary<ItemRef, DateTimeOffset>(FirstSeen));
}
