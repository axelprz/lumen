// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Sync;

/// <summary>What to do the first time, when the folder and this PC both already have data.</summary>
public enum SyncStart
{
    /// <summary>This PC takes what the folder has; what only this PC had is removed here.</summary>
    UseFolder,

    /// <summary>The folder takes what this PC has; what only the folder had is removed there.</summary>
    UseThisPc,

    /// <summary>Both are joined; for the same thing, the folder's version wins. Nothing is lost.</summary>
    Merge,
}

public enum SyncStatus
{
    Off,
    Active,

    /// <summary>The folder isn't there right now (a drive that is away, a cloud folder not mounted): nothing is touched.</summary>
    FolderUnavailable,

    Failed,
}

/// <summary>How much one side has, for the "folder or this PC?" question.</summary>
public sealed record SyncSummary(int Collections, int ManualItems, int Renames, int IgnoredApps)
{
    public bool HasData => Collections + ManualItems + Renames + IgnoredApps > 0;
}

public sealed record SyncPairing(SyncSummary Folder, SyncSummary ThisPc)
{
    /// <summary>Both sides have something: the user must say which wins.</summary>
    public bool NeedsChoice => Folder.HasData && ThisPc.HasData;
}

/// <summary>Raised after data in the folder was applied to this PC, so the pages can reload.</summary>
public sealed class SyncAppliedEventArgs(bool configChanged, bool collectionsChanged, IReadOnlyList<string> manualItemsReceived) : EventArgs
{
    public bool ConfigChanged { get; } = configChanged;

    public bool CollectionsChanged { get; } = collectionsChanged;

    /// <summary>Titles of manual items that came from the folder; shown to the user, never run on their own.</summary>
    public IReadOnlyList<string> ManualItemsReceived { get; } = manualItemsReceived;
}

public sealed record SyncOptions
{
    /// <summary>Wait after a change before syncing, so a burst of writes is one pass.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Look at the folder again this often even without an event (cloud clients don't always raise one).</summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>False in tests: passes only run when asked.</summary>
    public bool Watch { get; init; } = true;
}

/// <summary>Tells when something in a folder (at any depth) changed.</summary>
public interface IFolderWatcher
{
    /// <summary>Calls <paramref name="changed"/> with the path of each file that changed. Dispose to stop.</summary>
    IDisposable Watch(string folder, Action<string> changed);
}

/// <summary>
/// The sync folder (PLAN-v1 §4.5): <c>config.json</c> and <c>collections/*.json</c> are kept in step with this PC both
/// ways, and a change to <c>routines/</c> is announced. For each document Lumen remembers the hash it last saw in step
/// ("baseline"): if only the folder changed it is applied here, if only this PC changed it is written there, and if
/// both changed the folder's version wins (a collection deleted in the folder but edited here is kept).
/// Rules that protect the user's data:
/// <list type="bullet">
/// <item>A folder that isn't there, or looks wiped (no config and no collections), never deletes anything here; this PC restores it.</item>
/// <item>A file that can't be read, is half written, or was written by a newer Lumen is left alone.</item>
/// <item>Only <c>config.json</c> and <c>collections/{id}.json</c> are ever written, atomically, inside the chosen folder. Secrets, playtime and routine approvals never go in.</item>
/// <item>Keys in a file that Lumen doesn't manage are kept when it rewrites the file.</item>
/// </list>
/// </summary>
public sealed partial class SyncFolderService : IDisposable
{
    public const string ConfigFile = "config.json";
    public const string CollectionsFolder = "collections";
    public const string RoutinesFolder = "routines";

    private const string ConfigKey = "config";
    private const string CollectionPrefix = "collection:";

    private readonly IFileSystem _files;
    private readonly Func<string, IUserFiles> _userFilesFor;
    private readonly LocalPortableState _state;
    private readonly ILocalSettings _settings;
    private readonly IFolderWatcher _watcher;
    private readonly SyncOptions _options;
    private readonly ILogger<SyncFolderService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _signal = new(0);
    private CancellationTokenSource? _loopCts;
    private IDisposable? _watch;
    private string? _watching;
    private int _routinesDirty;

    public SyncFolderService(
        IFileSystem files,
        Func<string, IUserFiles> userFilesFor,
        LocalPortableState state,
        ILocalSettings settings,
        IFolderWatcher watcher,
        ILogger<SyncFolderService> logger,
        SyncOptions? options = null)
    {
        _files = files;
        _userFilesFor = userFilesFor;
        _state = state;
        _settings = settings;
        _watcher = watcher;
        _logger = logger;
        _options = options ?? new SyncOptions();
    }

    /// <summary>The folder in use; null when sync is off.</summary>
    public string? Folder { get; private set; }

    public SyncStatus Status { get; private set; } = SyncStatus.Off;

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Files the last pass skipped and why (technical English), for the details in Settings.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>The folder that holds the routines, or null when sync is off.</summary>
    public string? RoutinesPath => Folder is null ? null : Path.Combine(Folder, RoutinesFolder);

    public event EventHandler<SyncAppliedEventArgs>? Applied;

    /// <summary>Something in <c>routines/</c> changed: reload the routines.</summary>
    public event EventHandler? RoutinesChanged;

    public event EventHandler? StatusChanged;

    /// <summary>Counts what the folder and this PC have, to decide how to start.</summary>
    public async Task<SyncPairing> InspectAsync(string folder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var snapshot = await ReadFolderAsync(folder, ct).ConfigureAwait(false);
        var local = await ReadLocalAsync(snapshot.Docs.ContainsKey(ConfigKey), ct).ConfigureAwait(false);
        return new SyncPairing(Summarize(snapshot.Docs), Summarize(local));
    }

    /// <summary>
    /// Starts syncing with <paramref name="folder"/>. When both sides have data <paramref name="start"/> is required
    /// (see <see cref="InspectAsync"/>); otherwise it is chosen here and the argument is ignored.
    /// </summary>
    public async Task AttachAsync(string folder, SyncStart? start, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        SyncAppliedEventArgs? applied;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadFolderAsync(folder, ct).ConfigureAwait(false);
            var local = await ReadLocalAsync(snapshot.Docs.ContainsKey(ConfigKey), ct).ConfigureAwait(false);
            var pairing = new SyncPairing(Summarize(snapshot.Docs), Summarize(local));
            var mode = pairing.NeedsChoice
                ? start ?? throw new InvalidOperationException("The folder and this PC both have data: say which one to use.")
                : pairing.Folder.HasData ? SyncStart.UseFolder : SyncStart.UseThisPc;

            var received = new List<string>();
            var configChanged = false;
            var collectionsChanged = false;
            Dictionary<string, string> baseline;
            switch (mode)
            {
                case SyncStart.UseFolder:
                    // As if this PC was in step with the folder before: whatever differs now counts as the folder's change.
                    baseline = local.ToDictionary(kv => kv.Key, kv => kv.Value.Hash, StringComparer.Ordinal);
                    break;
                case SyncStart.UseThisPc:
                    // As if the folder was in step with this PC before: whatever differs now counts as this PC's change.
                    baseline = snapshot.Docs.ToDictionary(kv => kv.Key, kv => kv.Value.Hash, StringComparer.Ordinal);
                    break;
                default:
                    (configChanged, collectionsChanged) = await MergeIntoLocalAsync(snapshot, local, received, ct).ConfigureAwait(false);
                    baseline = snapshot.Docs.ToDictionary(kv => kv.Key, kv => kv.Value.Hash, StringComparer.Ordinal);
                    break;
            }

            await _settings.SetAsync(LocalSettingKeys.SyncFolder, folder, ct).ConfigureAwait(false);
            await SaveBaselineAsync(baseline, ct).ConfigureAwait(false);
            Folder = folder;
            applied = await ReconcileAsync(folder, ct).ConfigureAwait(false);
            applied = Combine(applied, configChanged, collectionsChanged, received);
        }
        finally
        {
            _gate.Release();
        }

        StartWatching();
        Raise(applied);
    }

    /// <summary>Stops syncing. The folder's files and this PC's data stay exactly as they are.</summary>
    public async Task DetachAsync(CancellationToken ct)
    {
        StopWatching();
        await _settings.RemoveAsync(LocalSettingKeys.SyncFolder, ct).ConfigureAwait(false);
        await _settings.RemoveAsync(LocalSettingKeys.SyncBaseline, ct).ConfigureAwait(false);
        Folder = null;
        SetStatus(SyncStatus.Off);
    }

    /// <summary>At start-up: picks up the folder chosen before and syncs it.</summary>
    public async Task ResumeAsync(CancellationToken ct)
    {
        var folder = await _settings.GetAsync(LocalSettingKeys.SyncFolder, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(folder))
        {
            SetStatus(SyncStatus.Off);
            return;
        }

        Folder = folder;
        await SyncNowAsync(ct).ConfigureAwait(false);
        StartWatching();
    }

    /// <summary>One pass now.</summary>
    public async Task SyncNowAsync(CancellationToken ct)
    {
        SyncAppliedEventArgs? applied = null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Folder is not { } folder)
            {
                return;
            }

            if (!_files.DirectoryExists(folder))
            {
                SetStatus(SyncStatus.FolderUnavailable);
                return;
            }

            applied = await ReconcileAsync(folder, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPassFailed(ex);
            Problems = [$"Sync failed: {ex.GetType().Name}"];
            SetStatus(SyncStatus.Failed);
        }
        finally
        {
            _gate.Release();
        }

        Raise(applied);
    }

    /// <summary>Call when something that syncs changed on this PC; the pass runs after a short pause.</summary>
    public void NotifyLocalChanged() => Wake();

    public void Dispose()
    {
        StopWatching();
        _gate.Dispose();
        _signal.Dispose();
    }

    // ── the pass ────────────────────────────────────────────────────────────────────────────────

    private async Task<SyncAppliedEventArgs?> ReconcileAsync(string folder, CancellationToken ct)
    {
        var snapshot = await ReadFolderAsync(folder, ct).ConfigureAwait(false);
        var local = await ReadLocalAsync(snapshot.Docs.ContainsKey(ConfigKey), ct).ConfigureAwait(false);
        var baseline = await LoadBaselineAsync(ct).ConfigureAwait(false);
        var updated = new Dictionary<string, string>(baseline, StringComparer.Ordinal);
        var userFiles = _userFilesFor(folder);

        var keys = snapshot.Docs.Keys.Concat(local.Keys).Concat(baseline.Keys)
            .Where(k => !snapshot.Unreadable.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k == ConfigKey ? 0 : 1)
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToList();

        var configChanged = false;
        var collectionsChanged = false;
        var received = new List<string>();
        try
        {
            foreach (var key in keys)
            {
                snapshot.Docs.TryGetValue(key, out var inFolder);
                local.TryGetValue(key, out var here);
                baseline.TryGetValue(key, out var known);

                if (inFolder?.Hash == here?.Hash)
                {
                    SetOrRemove(updated, key, inFolder?.Hash);
                    continue;
                }

                var folderChanged = inFolder?.Hash != known;
                var localChanged = here?.Hash != known;

                // Only the folder changed → take it. Only this PC changed → write it. Both → the folder wins,
                // except that an edit here beats a deletion there.
                var import = !localChanged || (folderChanged && inFolder is not null);
                if (import && inFolder is null && (key == ConfigKey || snapshot.LooksEmpty))
                {
                    import = false; // never wipe this PC because the config is gone or the folder looks emptied: restore the folder
                }

                if (import)
                {
                    if (inFolder is not null)
                    {
                        var result = await ImportAsync(key, inFolder, ct).ConfigureAwait(false);
                        configChanged |= key == ConfigKey && result.Changed;
                        collectionsChanged |= key != ConfigKey;
                        received.AddRange(result.ManualItemsReceived);
                        updated[key] = inFolder.Hash;
                    }
                    else
                    {
                        await _state.DeleteCollectionAsync(key[CollectionPrefix.Length..], ct).ConfigureAwait(false);
                        collectionsChanged = true;
                        updated.Remove(key);
                    }
                }
                else
                {
                    await ExportAsync(userFiles, folder, key, here, snapshot, updated, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await SaveBaselineAsync(updated, ct).ConfigureAwait(false);
        }

        Problems = snapshot.Problems;
        LastSyncedAt = DateTimeOffset.UtcNow;
        SetStatus(SyncStatus.Active);
        return configChanged || collectionsChanged ? new SyncAppliedEventArgs(configChanged, collectionsChanged, received) : null;
    }

    private async Task<ConfigApplyResult> ImportAsync(string key, Doc doc, CancellationToken ct)
    {
        if (key == ConfigKey)
        {
            return await _state.ApplyConfigAsync((PortableConfig)doc.Value, ct).ConfigureAwait(false);
        }

        await _state.UpsertCollectionAsync((Collection)doc.Value, ct).ConfigureAwait(false);
        return new ConfigApplyResult(true, []);
    }

    private async Task ExportAsync(IUserFiles userFiles, string folder, string key, Doc? here, FolderSnapshot snapshot, Dictionary<string, string> updated, CancellationToken ct)
    {
        var path = key == ConfigKey ? Path.Combine(folder, ConfigFile) : CollectionPath(folder, key[CollectionPrefix.Length..]);
        if (here is null)
        {
            userFiles.Delete(path); // removed on this PC
            updated.Remove(key);
            return;
        }

        snapshot.Texts.TryGetValue(key, out var existing);
        var text = key == ConfigKey
            ? PortableConfigJson.Write((PortableConfig)here.Value, existing)
            : PortableCollectionJson.Write((Collection)here.Value, existing);
        if (text != existing)
        {
            await userFiles.WriteTextAsync(path, text, ct).ConfigureAwait(false);
        }

        // What a reader will see in the file, not what this PC had: an entry the rules refuse can't make the two differ forever.
        var back = key == ConfigKey
            ? PortableConfigJson.Parse(text).Config is { } c ? PortableConfigJson.Hash(c) : null
            : PortableCollectionJson.Parse(text, key[CollectionPrefix.Length..]).Collection is { } col ? PortableCollectionJson.Hash(col) : null;
        SetOrRemove(updated, key, back);
    }

    // The "combine" start: this PC first receives the union, so the normal pass then writes the difference to the folder.
    private async Task<(bool Config, bool Collections)> MergeIntoLocalAsync(FolderSnapshot snapshot, Dictionary<string, Doc> local, List<string> received, CancellationToken ct)
    {
        var configChanged = false;
        var collectionsChanged = false;
        if (snapshot.Docs.TryGetValue(ConfigKey, out var folderConfig))
        {
            var here = local.TryGetValue(ConfigKey, out var h) ? (PortableConfig)h.Value : new PortableConfig();
            var merged = SyncMerge.MergeConfig((PortableConfig)folderConfig.Value, here);
            var result = await _state.ApplyConfigAsync(merged, ct).ConfigureAwait(false);
            configChanged = result.Changed;
            received.AddRange(result.ManualItemsReceived);
        }

        var folderCollections = snapshot.Docs.Values.Select(d => d.Value).OfType<Collection>().ToList();
        var localCollections = local.Values.Select(d => d.Value).OfType<Collection>().ToList();
        foreach (var merged in SyncMerge.MergeCollections(folderCollections, localCollections))
        {
            if (localCollections.FirstOrDefault(c => c.Id == merged.Id) is { } before && PortableCollectionJson.Hash(before) == PortableCollectionJson.Hash(merged))
            {
                continue;
            }

            await _state.UpsertCollectionAsync(merged, ct).ConfigureAwait(false);
            collectionsChanged = true;
        }

        return (configChanged, collectionsChanged);
    }

    // ── reading ─────────────────────────────────────────────────────────────────────────────────

    private sealed record Doc(string Hash, object Value);

    private sealed class FolderSnapshot
    {
        public Dictionary<string, Doc> Docs { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

        public List<string> Problems { get; } = [];

        /// <summary>No config and no collection at all: a new folder, or one that was emptied.</summary>
        public bool LooksEmpty => Docs.Count == 0 && Unreadable.Count == 0;
    }

    private async Task<FolderSnapshot> ReadFolderAsync(string folder, CancellationToken ct)
    {
        var snapshot = new FolderSnapshot();
        var configPath = Path.Combine(folder, ConfigFile);
        if (_files.FileExists(configPath))
        {
            var text = await TryReadAsync(configPath, ct).ConfigureAwait(false);
            var parsed = text is null ? null : PortableConfigJson.Parse(text);
            if (parsed?.Config is { } config)
            {
                snapshot.Docs[ConfigKey] = new Doc(PortableConfigJson.Hash(config), config);
                snapshot.Texts[ConfigKey] = text!;
                snapshot.Problems.AddRange(parsed.Problems.Select(p => $"{ConfigFile}: {p}"));
            }
            else
            {
                snapshot.Unreadable.Add(ConfigKey);
                snapshot.Problems.Add($"{ConfigFile}: {(parsed?.Problems.FirstOrDefault() ?? "could not be read")}; left as it is.");
            }
        }

        foreach (var file in _files.GetFiles(Path.Combine(folder, CollectionsFolder), "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var key = CollectionPrefix + id;
            var text = await TryReadAsync(file, ct).ConfigureAwait(false);
            var parsed = text is null ? null : PortableCollectionJson.Parse(text, id);
            if (parsed?.Collection is { } collection)
            {
                snapshot.Docs[key] = new Doc(PortableCollectionJson.Hash(collection), collection);
                snapshot.Texts[key] = text!;
                snapshot.Problems.AddRange(parsed.Problems.Select(p => $"{CollectionsFolder}/{id}.json: {p}"));
            }
            else
            {
                snapshot.Unreadable.Add(key);
                snapshot.Problems.Add($"{CollectionsFolder}/{Path.GetFileName(file)}: {(parsed?.Problems.FirstOrDefault() ?? "could not be read")}; left as it is.");
            }
        }

        return snapshot;
    }

    // A file that is being written by a cloud client can fail to open for a moment: the next pass tries again.
    private async Task<string?> TryReadAsync(string path, CancellationToken ct)
    {
        try
        {
            return await _files.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // An empty config is only a document when the folder has a config.json too: then "nothing here" is a value to compare.
    private async Task<Dictionary<string, Doc>> ReadLocalAsync(bool folderHasConfig, CancellationToken ct)
    {
        var docs = new Dictionary<string, Doc>(StringComparer.Ordinal);
        var config = await _state.ReadConfigAsync(ct).ConfigureAwait(false);
        if (folderHasConfig || config.Language is not null || config.MinimizeToTray is not null || config.Hotkey is not null || config.HasUserData)
        {
            docs[ConfigKey] = new Doc(PortableConfigJson.Hash(config), config);
        }

        foreach (var collection in await _state.ReadCollectionsAsync(ct).ConfigureAwait(false))
        {
            docs[CollectionPrefix + collection.Id] = new Doc(PortableCollectionJson.Hash(collection), collection);
        }

        return docs;
    }

    private static SyncSummary Summarize(Dictionary<string, Doc> docs)
    {
        var config = docs.TryGetValue(ConfigKey, out var c) ? (PortableConfig)c.Value : new PortableConfig();
        var collections = docs.Values.Select(d => d.Value).OfType<Collection>().Count(x => x.Items.Count > 0 || !x.IsFavorites);
        return new SyncSummary(collections, config.ManualItems.Count, config.Overrides.Count, config.IgnoredApps.Count);
    }

    private static string CollectionPath(string folder, string id) => Path.Combine(folder, CollectionsFolder, id + ".json");

    private static void SetOrRemove(Dictionary<string, string> map, string key, string? hash)
    {
        if (hash is null)
        {
            map.Remove(key);
        }
        else
        {
            map[key] = hash;
        }
    }

    private async Task<Dictionary<string, string>> LoadBaselineAsync(CancellationToken ct)
    {
        var json = await _settings.GetAsync(LocalSettingKeys.SyncBaseline, ct).ConfigureAwait(false);
        try
        {
            return string.IsNullOrEmpty(json)
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [], StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal); // unreadable: treated as never synced, nothing is deleted
        }
    }

    private Task SaveBaselineAsync(Dictionary<string, string> baseline, CancellationToken ct) =>
        _settings.SetAsync(LocalSettingKeys.SyncBaseline, JsonSerializer.Serialize(baseline.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value)), ct);

    // ── watching ────────────────────────────────────────────────────────────────────────────────

    private void StartWatching()
    {
        if (!_options.Watch || Folder is null || _loopCts is not null)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _ = RunLoopAsync(_loopCts.Token);
    }

    private void StopWatching()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
        _watch?.Dispose();
        _watch = null;
        _watching = null;
    }

    private void EnsureWatcher()
    {
        if (Folder is not { } folder || _watching == folder || !_files.DirectoryExists(folder))
        {
            return;
        }

        _watch?.Dispose();
        try
        {
            _watch = _watcher.Watch(folder, OnFolderChanged);
            _watching = folder;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            LogWatchFailed(ex); // the periodic rescan still works
            _watch = null;
            _watching = null;
        }
    }

    private void OnFolderChanged(string path)
    {
        if (Folder is { } folder && path.StartsWith(Path.Combine(folder, RoutinesFolder), StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _routinesDirty, 1);
        }

        Wake();
    }

    private void Wake()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
                // Closing down.
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                EnsureWatcher();
                if (await _signal.WaitAsync(_options.RescanInterval, ct).ConfigureAwait(false))
                {
                    await Task.Delay(_options.Debounce, ct).ConfigureAwait(false);
                    while (_signal.CurrentCount > 0 && await _signal.WaitAsync(0, ct).ConfigureAwait(false))
                    {
                    }
                }

                await SyncNowAsync(ct).ConfigureAwait(false);
                if (Interlocked.Exchange(ref _routinesDirty, 0) == 1)
                {
                    RoutinesChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (ObjectDisposedException)
        {
            // Closing down.
        }
    }

    // ── small helpers ───────────────────────────────────────────────────────────────────────────

    private static SyncAppliedEventArgs? Combine(SyncAppliedEventArgs? pass, bool configChanged, bool collectionsChanged, List<string> received)
    {
        var config = configChanged || pass?.ConfigChanged == true;
        var collections = collectionsChanged || pass?.CollectionsChanged == true;
        return config || collections
            ? new SyncAppliedEventArgs(config, collections, received.Concat(pass?.ManualItemsReceived ?? []).Distinct(StringComparer.Ordinal).ToList())
            : null;
    }

    private void Raise(SyncAppliedEventArgs? applied)
    {
        if (applied is not null)
        {
            Applied?.Invoke(this, applied);
        }
    }

    private void SetStatus(SyncStatus status)
    {
        if (Status != status)
        {
            Status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync pass failed")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not watch the sync folder; relying on the periodic rescan")]
    private partial void LogWatchFailed(Exception exception);
}

/// <summary>The real disk: <see cref="FileSystemWatcher"/> over the folder and its subfolders. Half-written temp files are ignored.</summary>
public sealed class PhysicalFolderWatcher : IFolderWatcher
{
    public IDisposable Watch(string folder, Action<string> changed)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
        };

        void Report(string? path)
        {
            if (path is not null && !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                changed(path);
            }
        }

        watcher.Created += (_, e) => Report(e.FullPath);
        watcher.Changed += (_, e) => Report(e.FullPath);
        watcher.Deleted += (_, e) => Report(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            Report(e.OldFullPath);
            Report(e.FullPath);
        };
        watcher.Error += (_, _) => changed(folder); // buffer overflow etc.: look at everything again
        watcher.EnableRaisingEvents = true;
        return watcher;
    }
}
