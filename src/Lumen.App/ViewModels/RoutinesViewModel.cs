// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Core.Routines;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>
/// The Routines page (PLAN-v1 §4.5): the routines on this PC with Run, Review, Edit, Undo, Export and Delete, plus
/// New and Import. A routine that is new or changed outside Lumen opens the preview instead of running. The preview
/// and the editor cover the page (a gamepad layer).
/// </summary>
public sealed partial class RoutinesViewModel : ObservableObject
{
    private readonly RoutineService _routines;
    private readonly QuickSearch _search;
    private readonly LibraryService _library;
    private readonly IDialogService _dialogs;
    private readonly IFileSystem _fileSystem;
    private readonly ILocalizer _loc;
    private readonly ILogger<RoutinesViewModel> _logger;
    private readonly SynchronizationContext _ui;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPanel))]
    private object? _panel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public RoutinesViewModel(
        RoutineService routines, QuickSearch search, LibraryService library, IDialogService dialogs, IFileSystem fileSystem,
        ILocalizer loc, ILogger<RoutinesViewModel> logger)
    {
        _routines = routines;
        _search = search;
        _library = library;
        _dialogs = dialogs;
        _fileSystem = fileSystem;
        _loc = loc;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _routines.Changed += (_, _) => _ui.Post(_ => Rebuild(), null);
        _library.Changed += (_, _) => _ui.Post(_ => _ = ReloadAsync(), null);
        _loc.CultureChanged += (_, _) => Rebuild();
    }

    public ObservableCollection<RoutineCardViewModel> Routines { get; } = [];

    public bool HasRoutines => Routines.Count > 0;

    /// <summary>A routine is running right now (sidebar dot).</summary>
    public bool IsBusy => Routines.Any(r => r.IsRunning);

    /// <summary>A routine is new, changed or broken and waits for the user (sidebar dot).</summary>
    public bool NeedsAttention => Routines.Any(r => r.NeedsReview || r.IsInvalid);

    public bool HasPanel => Panel is not null;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            await _routines.LoadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
        }
    }

    private async Task ReloadAsync()
    {
        if (Panel is not null)
        {
            return; // don't change the list under an open preview or editor
        }

        try
        {
            await _routines.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
        }
    }

    /// <summary>Raised when a routine started from the tray, quick search or Home needs its preview, so the window can come forward.</summary>
    public event EventHandler? ReviewRequested;

    /// <summary>Raised when a routine started from the tray, quick search or Home finished: its name and a one-line result.</summary>
    public event EventHandler<RoutineFinishedEventArgs>? FinishedOutsidePage;

    /// <summary>Runs a routine by id (the preview's "Approve and run"). An unapproved one opens its preview instead.</summary>
    public async Task RunByIdAsync(string id)
    {
        if (Routines.FirstOrDefault(r => r.Entry.Id == id) is { } card)
        {
            await RunAsync(card);
        }
    }

    /// <summary>
    /// Runs a routine chosen outside the Routines page (tray, quick search, Home). A new or changed routine never runs from
    /// here: the Routines page opens with its preview. A preview or editor already open is left alone.
    /// </summary>
    public async Task StartFromShortcutAsync(string id)
    {
        if (Routines.FirstOrDefault(r => r.Entry.Id == id) is not { } card || card.IsRunning)
        {
            return;
        }

        if (!card.IsReady)
        {
            if (Panel is null)
            {
                Review(card);
            }

            ReviewRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        await RunAsync(card);
        var shown = Current(card);
        FinishedOutsidePage?.Invoke(this, new RoutineFinishedEventArgs(shown.Name, shown.ResultText ?? string.Empty));
    }

    [RelayCommand]
    private void New() => OpenEditor(null);

    [RelayCommand]
    private void Edit(RoutineCardViewModel? card)
    {
        if (card?.Entry.Routine is { } routine)
        {
            OpenEditor(routine);
        }
    }

    [RelayCommand]
    private void Review(RoutineCardViewModel? card)
    {
        if (card is not null)
        {
            Panel = new RoutineReviewViewModel(card.Entry, _loc);
        }
    }

    [RelayCommand]
    private async Task RunAsync(RoutineCardViewModel? card)
    {
        if (card is null || card.IsRunning)
        {
            return;
        }

        if (!card.IsReady)
        {
            Review(card); // new, changed or broken: the preview explains
            return;
        }

        card.IsRunning = true;
        OnPropertyChanged(nameof(IsBusy));
        card.SetResult(() => _loc["Routine_Running"]);
        try
        {
            var (outcome, run) = await _routines.RunAsync(card.Entry.Id, null, CancellationToken.None);

            // The cards are rebuilt while a routine runs (a library or routine change), so the result goes to the card on screen now.
            var shown = Current(card);
            switch (outcome)
            {
                case RoutineRunOutcome.Ran when run is not null:
                    var ok = run.Steps.Count(s => s.Outcome is StepOutcome.Done or StepOutcome.Skipped);
                    shown.SetResult(() => run.Succeeded
                        ? _loc["Routine_Result_AllOk"]
                        : string.Format(CultureInfo.CurrentCulture, _loc["Routine_Result_Some"], ok, run.Steps.Count, FirstProblem(run)));
                    shown.CanUndo = run.CanUndo;
                    break;
                case RoutineRunOutcome.NeedsReview:
                    shown.SetResult(() => _loc["Routine_Result_Changed"]);
                    break;
                default:
                    shown.SetResult(() => _loc["Routine_Result_Failed"]);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRunFailed(ex, card.Entry.Id);
            Current(card).SetResult(() => _loc["Routine_Result_Failed"]);
        }
        finally
        {
            card.IsRunning = false;
            Current(card).IsRunning = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    // The card now showing this routine (the one passed in may have been replaced by a rebuild).
    private RoutineCardViewModel Current(RoutineCardViewModel card) =>
        Routines.FirstOrDefault(r => r.Entry.Id == card.Entry.Id) ?? card;

    [RelayCommand]
    private async Task UndoAsync(RoutineCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var result = await _routines.UndoAsync(card.Entry.Id, CancellationToken.None);
        card.CanUndo = false;
        card.StillOpen = result.StillRunning;
        card.SetResult(() => result.StillRunning.Count == 0
            ? string.Format(CultureInfo.CurrentCulture, _loc["Routine_Undone"], result.Asked)
            : string.Format(CultureInfo.CurrentCulture, _loc["Routine_UndoStillOpen"], result.StillRunning.Count, string.Join(", ", result.StillRunning.Select(p => p.Name).Distinct())));
    }

    /// <summary>Ends what ignored Undo, after a confirmation that unsaved progress is lost.</summary>
    [RelayCommand]
    private async Task ForceCloseAsync(RoutineCardViewModel? card)
    {
        if (card is not { CanForceClose: true })
        {
            return;
        }

        var names = string.Join(", ", card.StillOpen.Select(p => p.Name).Distinct());
        if (!_dialogs.Confirm(_loc["Routine_ForceTitle"], string.Format(CultureInfo.CurrentCulture, _loc["Routine_ForceConfirm"], names)))
        {
            return;
        }

        var left = await _routines.ForceCloseAsync(card.StillOpen, CancellationToken.None);
        card.StillOpen = [];
        card.SetResult(() => left == 0 ? _loc["Routine_Forced"] : _loc["Routine_ForceFailed"]);
    }

    [RelayCommand]
    private async Task ApproveAsync(RoutineReviewViewModel? review) => await ApproveCoreAsync(review, run: false);

    [RelayCommand]
    private async Task ApproveAndRunAsync(RoutineReviewViewModel? review) => await ApproveCoreAsync(review, run: true);

    [RelayCommand]
    private void ClosePanel() => Panel = null;

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_dialogs.PickFile(_loc["Routine_FileFilter"]) is not { } path)
        {
            return;
        }

        try
        {
            var result = await _routines.ImportAsync(path, CancellationToken.None);
            if (result.Routine is { } routine && Routines.FirstOrDefault(r => r.Entry.Id == routine.Id) is { } card)
            {
                Review(card); // imported routines always start with the preview
            }
            else
            {
                Notice = string.Format(CultureInfo.CurrentCulture, _loc["Routine_ImportFailed"], string.Join(" ", result.Errors.Take(3)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogImportFailed(ex);
            Notice = string.Format(CultureInfo.CurrentCulture, _loc["Routine_ImportFailed"], ex.GetType().Name);
        }
    }

    [RelayCommand]
    private async Task ExportAsync(RoutineCardViewModel? card)
    {
        if (card?.Entry.Routine is null || _dialogs.PickSaveFile(card.Entry.Id + ".json", _loc["Routine_FileFilter"]) is not { } path)
        {
            return;
        }

        try
        {
            await _routines.ExportAsync(card.Entry, path, CancellationToken.None);
            Notice = string.Format(CultureInfo.CurrentCulture, _loc["Routine_Exported"], Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogExportFailed(ex);
            Notice = _loc["Routine_ExportFailed"];
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(RoutineCardViewModel? card)
    {
        if (card is null || !_dialogs.Confirm(_loc["Routine_DeleteTitle"], string.Format(CultureInfo.CurrentCulture, _loc["Routine_DeleteConfirm"], card.Name)))
        {
            return;
        }

        await _routines.DeleteAsync(card.Entry, CancellationToken.None);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(_routines.Folder);
        _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_routines.Folder}\"") { UseShellExecute = true });
    }

    private async Task ApproveCoreAsync(RoutineReviewViewModel? review, bool run)
    {
        if (review is not { CanApprove: true })
        {
            return;
        }

        var id = review.Entry.Id;
        await _routines.ApproveAsync(review.Entry, CancellationToken.None);
        Panel = null;
        if (run)
        {
            await RunByIdAsync(id);
        }
    }

    private void OpenEditor(Routine? routine)
    {
        var editor = new RoutineEditorViewModel(_routines, _search, _library, _dialogs, _fileSystem, _loc, routine);
        editor.Closed += (_, _) => Panel = null;
        Panel = editor;
    }

    private string FirstProblem(RoutineRun run) =>
        run.Steps.FirstOrDefault(s => s.Outcome is StepOutcome.Failed or StepOutcome.Blocked) is { } bad
            ? string.Format(CultureInfo.CurrentCulture, _loc["Routine_Result_StepProblem"], bad.Index + 1, _loc["Routine_Problem_" + (bad.Outcome == StepOutcome.Blocked ? "Blocked" : bad.Problem.ToString())])
            : string.Empty;

    private void Rebuild()
    {
        var undoable = Routines.Where(r => r.CanUndo).Select(r => r.Entry.Id).ToHashSet(StringComparer.Ordinal);
        var results = Routines.Where(r => r.ResultSource is not null).ToDictionary(r => r.Entry.Id, r => r.ResultSource, StringComparer.Ordinal);
        var running = Routines.Where(r => r.IsRunning).Select(r => r.Entry.Id).ToHashSet(StringComparer.Ordinal);

        // Cards are rebuilt whenever the library changes (playtime, installs): keep what the user was looking at.
        var stillOpen = Routines.Where(r => r.CanForceClose).ToDictionary(r => r.Entry.Id, r => r.StillOpen, StringComparer.Ordinal);
        Routines.Clear();
        foreach (var entry in _routines.Entries.OrderBy(e => e.Routine?.Name.For(CultureInfo.CurrentUICulture) ?? e.FileId, StringComparer.CurrentCultureIgnoreCase))
        {
            var card = new RoutineCardViewModel(entry, _loc)
            {
                CanUndo = undoable.Contains(entry.Id) && _routines.LastRun(entry.Id) is not null,
                IsRunning = running.Contains(entry.Id),
                StillOpen = stillOpen.GetValueOrDefault(entry.Id) ?? [],
            };
            card.SetResult(results.GetValueOrDefault(entry.Id)); // written again in the current language
            Routines.Add(card);
        }

        OnPropertyChanged(nameof(HasRoutines));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(NeedsAttention));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load routines")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Routine {Id} failed to run")]
    private partial void LogRunFailed(Exception exception, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not import a routine")]
    private partial void LogImportFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not export a routine")]
    private partial void LogExportFailed(Exception exception);
}

/// <summary>A routine started outside the Routines page finished.</summary>
public sealed class RoutineFinishedEventArgs(string name, string result) : EventArgs
{
    public string Name { get; } = name;

    public string Result { get; } = result;
}
