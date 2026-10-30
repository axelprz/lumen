// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
using Lumen.Core.Sync;
using Lumen.Core.Tests.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Core.Tests.Sync;

/// <summary>A folder in memory shared by several fake PCs (the "cloud"). Paths compare with / and \ alike.</summary>
internal sealed class MemoryFolder : IFileSystem, IUserFiles
{
    public Dictionary<string, string> Text { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Exists { get; set; } = true;

    public int Writes { get; private set; }

    public string this[string relative]
    {
        get => Text[Key("/sync/" + relative)];
        set => Text[Key("/sync/" + relative)] = value;
    }

    public bool Has(string relative) => Text.ContainsKey(Key("/sync/" + relative));

    public bool FileExists(string path) => Exists && Text.ContainsKey(Key(path));

    public bool DirectoryExists(string path) => Exists;

    public IReadOnlyList<string> GetFiles(string directory, string pattern) =>
        !Exists ? [] : Text.Keys.Where(k => Parent(k) == Key(directory) && k.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct) =>
        Text.TryGetValue(Key(path), out var text) ? Task.FromResult(text) : throw new FileNotFoundException(path);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => throw new FileNotFoundException(path);

    public IReadOnlyList<string> GetDirectories(string directory) => [];

    public Task WriteTextAsync(string path, string text, CancellationToken ct)
    {
        Text[Key(path)] = text;
        Writes++;
        return Task.CompletedTask;
    }

    public Task WriteChosenFileAsync(string path, string text, CancellationToken ct) => WriteTextAsync(path, text, ct);

    public void Delete(string path) => Text.Remove(Key(path));

    private static string Key(string path) => path.Replace('\\', '/');

    private static string Parent(string key) => key[..key.LastIndexOf('/')];
}

internal sealed class NoWatcher : IFolderWatcher
{
    public IDisposable Watch(string folder, Action<string> changed) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class MapSettings : ILocalSettings
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult(Values.GetValueOrDefault(key));

    public Task SetAsync(string key, string value, CancellationToken ct)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        Values.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class MemoryManualItems : IManualItemStore
{
    public Dictionary<Guid, ManualItem> Items { get; } = [];

    public Task<IReadOnlyList<ManualItem>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ManualItem>>(Items.Values.ToList());

    public Task SaveAsync(ManualItem item, CancellationToken ct)
    {
        Items[item.Id] = item;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken ct)
    {
        Items.Remove(id);
        return Task.CompletedTask;
    }
}

internal sealed class MemoryCollections : ICollectionStore
{
    private readonly List<(string Id, string Name, List<ItemRef> Items)> _all = [(Collection.FavoritesId, "Favorites", [])];

    public Task<IReadOnlyList<Collection>> LoadAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Collection>>(_all
            .OrderBy(c => c.Id == Collection.FavoritesId ? 0 : 1).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new Collection(c.Id, c.Name, c.Items.ToList())).ToList());

    public Task CreateAsync(string id, string name, CancellationToken ct)
    {
        _all.Add((id, name, []));
        return Task.CompletedTask;
    }

    public Task RenameAsync(string id, string name, CancellationToken ct)
    {
        var i = _all.FindIndex(c => c.Id == id);
        _all[i] = _all[i] with { Name = name };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        _all.RemoveAll(c => c.Id == id && c.Id != Collection.FavoritesId);
        return Task.CompletedTask;
    }

    public Task AddAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct)
    {
        var items = _all.Single(c => c.Id == id).Items;
        items.AddRange(refs.Where(r => !items.Contains(r)));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct)
    {
        _all.Single(c => c.Id == id).Items.RemoveAll(refs.Contains);
        return Task.CompletedTask;
    }
}

/// <summary>One PC: its own local stores and its own sync service, pointed at a shared folder.</summary>
internal sealed class FakePc
{
    public const string FolderPath = "/sync";

    public FakePc(MemoryFolder folder)
    {
        Folder = folder;
        State = new LocalPortableState(Settings, Manual, Overrides, Choices, Collections);
        Service = new SyncFolderService(folder, _ => folder, State, Settings, new NoWatcher(), NullLogger<SyncFolderService>.Instance, new SyncOptions { Watch = false });
        Service.Applied += (_, e) => AppliedEvents.Add(e);
    }

    public MemoryFolder Folder { get; }

    public MapSettings Settings { get; } = new();

    public MemoryManualItems Manual { get; } = new();

    public InMemoryOverrides Overrides { get; } = new();

    public InMemoryAppChoices Choices { get; } = new();

    public MemoryCollections Collections { get; } = new();

    public LocalPortableState State { get; }

    public SyncFolderService Service { get; }

    public List<SyncAppliedEventArgs> AppliedEvents { get; } = [];

    public Task AttachAsync(SyncStart? start = null) => Service.AttachAsync(FolderPath, start, CancellationToken.None);

    public Task SyncAsync() => Service.SyncNowAsync(CancellationToken.None);

    public async Task<Collection?> CollectionAsync(string id) =>
        (await Collections.LoadAsync(CancellationToken.None)).FirstOrDefault(c => c.Id == id);

    public Task AddToCollectionAsync(string id, params string[] refs) =>
        Collections.AddAsync(id, refs.Select(ItemRef.Parse), CancellationToken.None);
}
