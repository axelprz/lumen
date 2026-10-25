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
    private IReadOnlyList<LibraryEntry> _entries = [];

    public LibraryService(
        IEnumerable<ILibraryProvider> providers,
        ILibraryCache cache,
        ILogger<LibraryService> logger,
        TimeSpan? providerTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerTimeout = providerTimeout ?? DefaultProviderTimeout;
    }

    /// <summary>Raised (on any thread) after <see cref="Entries"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Current entries, sorted by title. Replaced as a whole, so it is safe to read from any thread.</summary>
    public IReadOnlyList<LibraryEntry> Entries => Volatile.Read(ref _entries);

    public async Task LoadCachedAsync(CancellationToken ct)
    {
        var cached = await _cache.LoadAllAsync(ct).ConfigureAwait(false);
        Publish(cached);
    }

    /// <summary>Scans every provider.</summary>
    public Task<IReadOnlyList<ProviderScanReport>> ScanAsync(CancellationToken ct) => ScanCoreAsync(_providers, ct);

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

            Publish(bySource.Values.SelectMany(v => v).ToList());
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

    private void Publish(IReadOnlyList<LibraryEntry> entries)
    {
        var sorted = entries
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Ref.ToString(), StringComparer.Ordinal)
            .ToList();
        Volatile.Write(ref _entries, sorted);
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
