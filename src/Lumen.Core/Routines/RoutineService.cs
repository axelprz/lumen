// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Routines;

/// <summary>One routine file and what Lumen thinks of it.</summary>
/// <param name="Review">The preview of the steps (null when the file has errors).</param>
public sealed record RoutineEntry(string Path, string FileId, RoutineParseResult Parse, RoutineTrustState State, RoutineReview? Review)
{
    public Routine? Routine => Parse.Routine;

    public string Id => Routine?.Id ?? FileId;
}

public enum RoutineRunOutcome
{
    Ran,

    /// <summary>Not approved (new, or the file changed since): review it first.</summary>
    NeedsReview,

    /// <summary>The file has errors or a blocked step.</summary>
    Invalid,

    /// <summary>The file is gone.</summary>
    Missing,
}

/// <summary>
/// The routines on this PC (PLAN-v1 §4.5): files in <c>routines/</c> inside Lumen's portable folder (the sync folder
/// later, M8). It reads and reviews them, and runs only approved ones. Right before running, the file is read again
/// and must still have the approved hash, so what runs is exactly what was approved. A routine saved from Lumen's
/// editor is approved by that save, because the user wrote every step; files imported or changed outside Lumen need
/// the preview.
/// </summary>
public sealed partial class RoutineService(
    string folder,
    IFileSystem fileSystem,
    IUserFiles userFiles,
    IRoutineTrustStore trust,
    RoutineRunner runner,
    LibraryService library,
    ILogger<RoutineService> logger)
{
    private readonly Dictionary<string, RoutineRun> _lastRuns = new(StringComparer.Ordinal);

    /// <summary>Raised (on the caller's thread) after routines were loaded, saved, approved or deleted.</summary>
    public event EventHandler? Changed;

    public string Folder => folder;

    public IReadOnlyList<RoutineEntry> Entries { get; private set; } = [];

    public async Task LoadAsync(CancellationToken ct)
    {
        var approved = await trust.LoadAsync(ct).ConfigureAwait(false);
        var entries = new List<RoutineEntry>();
        foreach (var path in fileSystem.GetFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(await ReadAsync(path, approved, ct).ConfigureAwait(false));
        }

        Entries = entries;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves a routine from the editor (approved by that save). A different id renames the file.</summary>
    public async Task SaveAsync(Routine routine, string? previousId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(routine);
        var json = RoutineJson.Serialize(routine);
        var parsed = RoutineParser.Parse(json);
        if (!parsed.IsValid)
        {
            throw new ArgumentException("The routine isn't valid: " + string.Join("; ", parsed.Errors), nameof(routine));
        }

        await userFiles.WriteTextAsync(PathFor(routine.Id), json, ct).ConfigureAwait(false);
        if (previousId is not null && previousId != routine.Id)
        {
            userFiles.Delete(PathFor(previousId));
            await trust.ForgetAsync(previousId, ct).ConfigureAwait(false);
        }

        var review = RoutineReview.Create(parsed.Routine!, FindItem, fileSystem.FileExists, fileSystem.DirectoryExists);
        if (review.CanRun)
        {
            await trust.ApproveAsync(routine.Id, parsed.Hash!, ct).ConfigureAwait(false);
        }

        LogSaved(routine.Id);
        await LoadAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Approves the entry as reviewed in the preview (never one with a blocked step).</summary>
    public async Task ApproveAsync(RoutineEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Review is not { CanRun: true } || entry.Parse.Hash is null)
        {
            throw new InvalidOperationException("Only a valid routine without blocked steps can be approved.");
        }

        await trust.ApproveAsync(entry.Id, entry.Parse.Hash, ct).ConfigureAwait(false);
        LogApproved(entry.Id);
        await LoadAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(RoutineEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        userFiles.Delete(entry.Path);
        await trust.ForgetAsync(entry.Id, ct).ConfigureAwait(false);
        _lastRuns.Remove(entry.Id);
        await LoadAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Copies a routine file into the folder. It isn't approved: it shows as "needs review".</summary>
    /// <returns>The parse result (with errors when the file can't be used).</returns>
    public async Task<RoutineParseResult> ImportAsync(string path, CancellationToken ct)
    {
        var json = await fileSystem.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var parsed = RoutineParser.Parse(json);
        if (parsed.Routine is { } routine)
        {
            await userFiles.WriteTextAsync(PathFor(routine.Id), json, ct).ConfigureAwait(false);
            await trust.ForgetAsync(routine.Id, ct).ConfigureAwait(false); // replacing one needs a new review
            LogImported(routine.Id);
            await LoadAsync(ct).ConfigureAwait(false);
        }

        return parsed;
    }

    public Task ExportAsync(RoutineEntry entry, string destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Routine is { } routine
            ? userFiles.WriteChosenFileAsync(destination, RoutineJson.Serialize(routine), ct)
            : throw new InvalidOperationException("A routine with errors can't be exported.");
    }

    /// <summary>Runs an approved routine, checking first that the file still is exactly what was approved.</summary>
    public async Task<(RoutineRunOutcome Outcome, RoutineRun? Run)> RunAsync(string id, IProgress<StepResult>? progress, CancellationToken ct)
    {
        var path = PathFor(id);
        if (!fileSystem.FileExists(path))
        {
            return (RoutineRunOutcome.Missing, null);
        }

        var entry = await ReadAsync(path, await trust.LoadAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        switch (entry.State)
        {
            case RoutineTrustState.Invalid:
                return (RoutineRunOutcome.Invalid, null);
            case RoutineTrustState.NeedsReview:
                await LoadAsync(ct).ConfigureAwait(false); // the list shows it changed
                return (RoutineRunOutcome.NeedsReview, null);
        }

        var run = await runner.RunAsync(entry.Review!, FindItem, progress, ct).ConfigureAwait(false);
        _lastRuns[id] = run;
        return (RoutineRunOutcome.Ran, run);
    }

    /// <summary>The last run of this routine in this session, if it opened something that can be closed.</summary>
    public RoutineRun? LastRun(string id) => _lastRuns.TryGetValue(id, out var run) && run.CanUndo ? run : null;

    public async Task<int> UndoAsync(string id, CancellationToken ct)
    {
        if (!_lastRuns.Remove(id, out var run))
        {
            return 0;
        }

        return await runner.UndoAsync(run, ct).ConfigureAwait(false);
    }

    /// <summary>A free id for a new routine named <paramref name="name"/> ("game-night", "game-night-2", …).</summary>
    public string NewId(string name)
    {
        var slug = new string(name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        slug = slug.Length == 0 ? "routine" : slug[..Math.Min(slug.Length, 56)].Trim('-');
        var taken = Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var id = slug;
        for (var n = 2; taken.Contains(id); n++)
        {
            id = $"{slug}-{n}";
        }

        return id;
    }

    public LibraryItem? FindItem(ItemRef itemRef) =>
        library.Items.FirstOrDefault(i => i.Entries.Any(e => e.Ref == itemRef));

    private string PathFor(string id) =>
        Routine.IsValidId(id) ? Path.Combine(folder, id + ".json") : throw new ArgumentException("Not a routine id.", nameof(id));

    private async Task<RoutineEntry> ReadAsync(string path, IReadOnlyDictionary<string, string> approved, CancellationToken ct)
    {
        var fileId = Path.GetFileNameWithoutExtension(path);
        RoutineParseResult parsed;
        try
        {
            parsed = RoutineParser.Parse(await fileSystem.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            parsed = new RoutineParseResult(null, [$"Could not read the file: {ex.GetType().Name}"], null);
        }

        if (parsed.Routine is { } routine && routine.Id != fileId)
        {
            parsed = new RoutineParseResult(null, [$"The file name must be \"{routine.Id}.json\" (its id)."], null);
        }

        if (parsed.Routine is null)
        {
            return new RoutineEntry(path, fileId, parsed, RoutineTrustState.Invalid, null);
        }

        var review = RoutineReview.Create(parsed.Routine, FindItem, fileSystem.FileExists, fileSystem.DirectoryExists);
        var state = !review.CanRun ? RoutineTrustState.Invalid
            : approved.TryGetValue(parsed.Routine.Id, out var hash) && hash == parsed.Hash ? RoutineTrustState.Trusted
            : RoutineTrustState.NeedsReview;
        return new RoutineEntry(path, fileId, parsed, state, review);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Routine {Id} saved from the editor")]
    private partial void LogSaved(string id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Routine {Id} approved after review")]
    private partial void LogApproved(string id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Routine {Id} imported (needs review)")]
    private partial void LogImported(string id);
}
