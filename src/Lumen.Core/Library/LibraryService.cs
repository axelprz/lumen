// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Microsoft.Extensions.Logging;

namespace Lumen.Core.Library;

/// <summary>What one provider reported in a scan.</summary>
public sealed record ProviderScanReport(string Source, string DisplayName, ProviderResult Installed, ProviderResult Owned)
{
    /// <summary>Warnings and errors from both scans (info messages such as "not configured" are left out).</summary>
    public IEnumerable<ProviderDiagnostic> Problems =>
        Installed.Diagnostics.Concat(Owned.Diagnostics).Where(d => d.Severity >= DiagnosticSeverity.Warning);
}

/// <summary>
/// Runs every provider (in parallel, each with a timeout), combines installed + owned entries per source,
/// persists them to the cache and exposes the current library. A failing provider never affects the others
/// and never wipes what was cached for it.
/// </summary>
public sealed partial class LibraryService
{
    public static readonly TimeSpan DefaultProviderTimeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<ILibraryProvider> _providers;
    private readonly ILibraryCache _cache;
    private readonly ILogger<LibraryService> _logger;
    private readonly TimeSpan _providerTimeout;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly LibraryMerger _merger;
    private readonly IMergeRuleStore? _ruleStore;
    private readonly IItemOverrideStore? _overrideStore;
    private readonly IAppChoiceStore? _appChoiceStore;
    private Snapshot _snapshot = new([], [], new UserChoices(MergeRules.None, NoOverrides, NoAppChoices));
    private IReadOnlyDictionary<ItemRef, DateTimeOffset> _firstSeen = new Dictionary<ItemRef, DateTimeOffset>();

    public LibraryService(
        IEnumerable<ILibraryProvider> providers,
        ILibraryCache cache,
        ILogger<LibraryService> logger,
        TimeSpan? providerTimeout = null,
        LibraryMerger? merger = null,
        IMergeRuleStore? ruleStore = null,
        IItemOverrideStore? overrideStore = null,
        IAppChoiceStore? appChoiceStore = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerTimeout = providerTimeout ?? DefaultProviderTimeout;
        _merger = merger ?? new LibraryMerger(new TitleNormalizer());
        _ruleStore = ruleStore;
        _overrideStore = overrideStore;
        _appChoiceStore = appChoiceStore;
    }

    private static readonly IReadOnlyDictionary<ItemRef, ItemOverride> NoOverrides = new Dictionary<ItemRef, ItemOverride>();

    private static readonly IReadOnlyDictionary<ItemRef, AppChoice> NoAppChoices = new Dictionary<ItemRef, AppChoice>();

    /// <summary>Raised (on any thread) after <see cref="Entries"/> or <see cref="Items"/> change.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Current entries, sorted by title, including detected apps the user hasn't added (see <see cref="AppCuration"/>).
    /// Replaced as a whole, so it is safe to read from any thread.
    /// </summary>
    public IReadOnlyList<LibraryEntry> Entries => Volatile.Read(ref _snapshot).Entries;

    /// <summary>The user's choices about detected apps (added / ignored), as of the last publish.</summary>
    public IReadOnlyDictionary<ItemRef, AppChoice> AppChoices => Volatile.Read(ref _snapshot).Choices.Apps;

    /// <summary>Entries merged into items (one per game across stores), sorted by title. Only added apps.</summary>
    public IReadOnlyList<LibraryItem> Items => Volatile.Read(ref _snapshot).Items;

    /// <summary>What each provider reported in the last scan of every store (null until one finishes).</summary>
    public IReadOnlyList<ProviderScanReport>? LastFullScan { get; private set; }

    /// <summary>Raised (on any thread) after a scan of every store finishes, even when nothing changed.</summary>
    public event EventHandler? FullScanCompleted;

    /// <summary>When each entry first appeared in Lumen ("Recently added").</summary>
    public IReadOnlyDictionary<ItemRef, DateTimeOffset> FirstSeen => Volatile.Read(ref _firstSeen);

    public async Task LoadCachedAsync(CancellationToken ct)
    {
        var choices = await LoadChoicesAsync(ct).ConfigureAwait(false);
        var cached = await _cache.LoadAllAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _firstSeen, await _cache.LoadFirstSeenAsync(ct).ConfigureAwait(false));
        Publish(cached, choices);
    }

    /// <summary>
    /// Re-applies the user's choices without rescanning: after merging, separating, picking a store, renaming,
    /// hiding, or resetting them.
    /// </summary>
    public async Task ReloadUserChoicesAsync(CancellationToken ct) =>
        Publish(Entries, await LoadChoicesAsync(ct).ConfigureAwait(false));

    private async Task<UserChoices> LoadChoicesAsync(CancellationToken ct) => new(
        _ruleStore is null ? MergeRules.None : await _ruleStore.LoadAsync(ct).ConfigureAwait(false),
        _overrideStore is null ? NoOverrides : await _overrideStore.LoadAsync(ct).ConfigureAwait(false),
        _appChoiceStore is null ? NoAppChoices : await _appChoiceStore.LoadAsync(ct).ConfigureAwait(false));

    /// <summary>Scans every provider.</summary>
    public Task<IReadOnlyList<ProviderScanReport>> ScanAsync(CancellationToken ct) => ScanCoreAsync(_providers, ct);

    /// <summary>A manual "Rescan": like <see cref="ScanAsync"/>, but providers first drop cached remote results.</summary>
    public Task<IReadOnlyList<ProviderScanReport>> RefreshAsync(CancellationToken ct)
    {
        foreach (var caching in _providers.OfType<ICachingProvider>())
        {
            caching.InvalidateCache();
        }

        return ScanCoreAsync(_providers, ct);
    }

    /// <summary>Folders to watch for installs/uninstalls, from every provider that knows them.</summary>
    public async Task<IReadOnlyList<WatchTarget>> GetWatchTargetsAsync(CancellationToken ct)
    {
        var targets = new List<WatchTarget>();
        foreach (var provider in _providers.OfType<IWatchableProvider>())
        {
            try
            {
                targets.AddRange(await provider.GetWatchTargetsAsync(ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Watching is a convenience; the next manual or focus-triggered rescan still works.
                LogProviderThrew(ex, ((ILibraryProvider)provider).Source, "watch");
            }
        }

        return targets;
    }

    /// <summary>Scans only the provider for <paramref name="source"/> (e.g. after editing manual items), leaving the rest as they are.</summary>
    public Task<IReadOnlyList<ProviderScanReport>> ScanSourceAsync(string source, CancellationToken ct) =>
        ScanCoreAsync(_providers.Where(p => p.Source == source).ToList(), ct);

    private async Task<IReadOnlyList<ProviderScanReport>> ScanCoreAsync(IReadOnlyList<ILibraryProvider> providers, CancellationToken ct)
    {
        await _scanLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var reports = await Task.WhenAll(providers.Select(p => ScanProviderAsync(p, ct))).ConfigureAwait(false);

            var bySource = Entries.GroupBy(e => e.Ref.Source).ToDictionary(g => g.Key, g => (IReadOnlyList<LibraryEntry>)g.ToList());
            foreach (var report in reports)
            {
                var previous = bySource.GetValueOrDefault(report.Source) ?? [];
                var combined = Combine(report, previous);
                if (combined is null)
                {
                    LogKeptCache(report.Source, report.Installed.Status, report.Owned.Status);
                    continue;
                }

                await _cache.ReplaceSourceAsync(report.Source, combined, ct).ConfigureAwait(false);
                bySource[report.Source] = combined;
                LogScanned(report.Source, combined.Count, report.Installed.Status, report.Owned.Status);
            }

            Volatile.Write(ref _firstSeen, await _cache.LoadFirstSeenAsync(ct).ConfigureAwait(false));
            Publish(bySource.Values.SelectMany(v => v).ToList());
            if (providers.Count == _providers.Count)
            {
                LastFullScan = reports;
                FullScanCompleted?.Invoke(this, EventArgs.Empty);
            }

            return reports;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>
    /// Installed entries win; owned entries fill in what is not installed. A scan that failed falls back to
    /// the matching part of the previous cache. Returns null when nothing new is known (keep the cache).
    /// </summary>
    public static IReadOnlyList<LibraryEntry>? Combine(ProviderScanReport report, IReadOnlyList<LibraryEntry> previous)
    {
        // "Store not installed" and "no key configured" are real answers (nothing there), unlike Failed.
        var installedUsable = report.Installed.IsUsable || report.Installed.Status == ProviderStatus.NotAvailable;
        var ownedUsable = report.Owned.IsUsable || report.Owned.Status == ProviderStatus.NotConfigured;
        if (!installedUsable && !ownedUsable)
        {
            return null;
        }

        var installed = installedUsable
            ? report.Installed.Entries
            : previous.Where(e => e.IsInstalled).ToList();
        var owned = ownedUsable
            ? report.Owned.Entries
            : previous.Where(e => !e.IsInstalled).ToList();

        var result = new Dictionary<ItemRef, LibraryEntry>();
        foreach (var entry in installed)
        {
            result[entry.Ref] = entry;
        }

        foreach (var entry in owned)
        {
            result.TryAdd(entry.Ref, entry with { State = InstallState.NotInstalled, InstallDirectory = null });
        }

        return result.Values.ToList();
    }

    private async Task<ProviderScanReport> ScanProviderAsync(ILibraryProvider provider, CancellationToken ct)
    {
        var installedTask = RunGuardedAsync(provider, "installed", provider.ScanInstalledAsync, ct);
        var ownedTask = provider.Capabilities.HasFlag(ProviderCapabilities.OwnedLibrary)
            ? RunGuardedAsync(provider, "owned", provider.ScanOwnedAsync, ct)
            : Task.FromResult(ProviderResult.NotConfigured("Owned library not supported."));
        await Task.WhenAll(installedTask, ownedTask).ConfigureAwait(false);
        return new ProviderScanReport(provider.Source, provider.DisplayName, await installedTask.ConfigureAwait(false), await ownedTask.ConfigureAwait(false));
    }

    private async Task<ProviderResult> RunGuardedAsync(
        ILibraryProvider provider,
        string scan,
        Func<CancellationToken, Task<ProviderResult>> run,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_providerTimeout);
        try
        {
            var result = await run(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            foreach (var problem in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning))
            {
                LogProblem(provider.Source, scan, problem.Severity, problem.Message, problem.Detail);
            }

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogTimeout(provider.Source, scan, _providerTimeout);
            return ProviderResult.Failed($"{provider.DisplayName}: the {scan} scan timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Providers must not throw; this is a safety net so one bad provider never breaks the scan.
            LogProviderThrew(ex, provider.Source, scan);
            return ProviderResult.Failed($"{provider.DisplayName}: the {scan} scan failed.", ex);
        }
    }

    private void Publish(IReadOnlyList<LibraryEntry> entries, UserChoices? choices = null)
    {
        choices ??= Volatile.Read(ref _snapshot).Choices;
        var sorted = entries
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Ref.ToString(), StringComparer.Ordinal)
            .ToList();
        // Automatic rescans run often (store folder changes, window focus): when nothing changed, don't make
        // the UI rebuild anything.
        var previous = Volatile.Read(ref _snapshot);
        if (ReferenceEquals(choices, previous.Choices) && previous.Items.Count > 0 && EntryContentComparer.SameList(sorted, previous.Entries))
        {
            return;
        }

        var shown = sorted.Where(e => AppCuration.IsShown(e, choices.Apps)).ToList();
        var items = _merger.Merge(shown, choices.Rules)
            .Select(i => ApplyOverrides(i, choices.Overrides))
            .OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.Key.ToString(), StringComparer.Ordinal)
            .ToList();
        Volatile.Write(ref _snapshot, new Snapshot(sorted, items, choices));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A custom title is saved on every entry of the item it was given to; the primary's wins, then any other's.
    /// An item is hidden when any of its entries is (hiding a merged item hides all of them).
    /// </summary>
    public static LibraryItem ApplyOverrides(LibraryItem item, IReadOnlyDictionary<ItemRef, ItemOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(overrides);
        if (overrides.Count == 0)
        {
            return item;
        }

        var found = item.Entries
            .OrderBy(e => e == item.Primary ? 0 : 1)
            .Select(e => overrides.GetValueOrDefault(e.Ref))
            .Where(o => o is not null)
            .ToList();
        if (found.Count == 0)
        {
            return item;
        }

        return item with
        {
            CustomTitle = found.Select(o => o!.Title).FirstOrDefault(t => t is not null),
            IsHidden = found.Any(o => o!.Hidden),
        };
    }

    private sealed record UserChoices(MergeRules Rules, IReadOnlyDictionary<ItemRef, ItemOverride> Overrides, IReadOnlyDictionary<ItemRef, AppChoice> Apps);

    // Entries, items and the choices they were built with are swapped together so readers never see a mix.
    private sealed record Snapshot(IReadOnlyList<LibraryEntry> Entries, IReadOnlyList<LibraryItem> Items, UserChoices Choices);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scanned {Source}: {Count} entries (installed: {Installed}, owned: {Owned})")]
    private partial void LogScanned(string source, int count, ProviderStatus installed, ProviderStatus owned);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kept cached entries for {Source} (installed: {Installed}, owned: {Owned})")]
    private partial void LogKeptCache(string source, ProviderStatus installed, ProviderStatus owned);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Source} {Scan} scan: [{Severity}] {Message} {Detail}")]
    private partial void LogProblem(string source, string scan, DiagnosticSeverity severity, string message, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Source} {Scan} scan timed out after {Timeout}")]
    private partial void LogTimeout(string source, string scan, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Source} {Scan} scan threw; providers must return diagnostics instead")]
    private partial void LogProviderThrew(Exception exception, string source, string scan);
}
