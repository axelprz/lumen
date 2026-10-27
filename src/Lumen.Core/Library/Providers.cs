// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

[Flags]
public enum ProviderCapabilities
{
    None = 0,
    InstalledScan = 1,

    /// <summary>Can list owned-but-not-installed items; usually needs a key or an account.</summary>
    OwnedLibrary = 2,
    Playtime = 4,
}

public enum ProviderStatus
{
    /// <summary>Everything was read.</summary>
    Ok,

    /// <summary>Some items could not be read; the rest are in the result. See diagnostics.</summary>
    Partial,

    /// <summary>The store is not installed on this machine. Not an error.</summary>
    NotAvailable,

    /// <summary>The scan needs a key or login the user has not provided. Not an error.</summary>
    NotConfigured,

    /// <summary>Nothing usable could be read. Previously cached entries should be kept.</summary>
    Failed,
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A non-fatal problem found during a scan. Never contains secrets.</summary>
public sealed record ProviderDiagnostic(DiagnosticSeverity Severity, string Message, string? Detail = null);

public sealed record ProviderResult(
    ProviderStatus Status,
    IReadOnlyList<LibraryEntry> Entries,
    IReadOnlyList<ProviderDiagnostic> Diagnostics)
{
    public static ProviderResult NotAvailable(string reason) =>
        new(ProviderStatus.NotAvailable, [], [new ProviderDiagnostic(DiagnosticSeverity.Info, reason)]);

    public static ProviderResult NotConfigured(string reason) =>
        new(ProviderStatus.NotConfigured, [], [new ProviderDiagnostic(DiagnosticSeverity.Info, reason)]);

    public static ProviderResult Failed(string message, Exception? exception = null) =>
        new(ProviderStatus.Failed, [], [new ProviderDiagnostic(DiagnosticSeverity.Error, message, exception?.Message)]);

    /// <summary>Ok when there are no warnings or errors, Partial otherwise.</summary>
    public static ProviderResult FromScan(IReadOnlyList<LibraryEntry> entries, IReadOnlyList<ProviderDiagnostic> diagnostics) =>
        new(
            diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Warning) ? ProviderStatus.Partial : ProviderStatus.Ok,
            entries,
            diagnostics);

    /// <summary>True when the entries can replace what was cached for this source.</summary>
    public bool IsUsable => Status is ProviderStatus.Ok or ProviderStatus.Partial;
}

/// <summary>
/// A provider that keeps remote results for a while (e.g. Steam's owned list) so frequent automatic rescans
/// stay local. A manual "Rescan" asks it to forget them.
/// </summary>
public interface ICachingProvider
{
    void InvalidateCache();
}

/// <summary>Folders whose changes mean "this store's installed games may have changed" (install finished, uninstall).</summary>
public sealed record WatchTarget(string Directory, string Filter);

/// <summary>A provider that can tell which folders to watch so Lumen notices installs/uninstalls by itself.</summary>
public interface IWatchableProvider
{
    Task<IReadOnlyList<WatchTarget>> GetWatchTargetsAsync(CancellationToken ct);
}

/// <summary>
/// A store or app source. Implementations must never throw out of a scan: catch, record a diagnostic
/// and return whatever could be read.
/// </summary>
public interface ILibraryProvider
{
    /// <summary>Lowercase source id used in <see cref="ItemRef"/>, e.g. "steam".</summary>
    string Source { get; }

    string DisplayName { get; }

    ProviderCapabilities Capabilities { get; }

    Task<ProviderResult> ScanInstalledAsync(CancellationToken ct);

    /// <summary>Returns <see cref="ProviderStatus.NotConfigured"/> when no key/login is set.</summary>
    Task<ProviderResult> ScanOwnedAsync(CancellationToken ct);
}
