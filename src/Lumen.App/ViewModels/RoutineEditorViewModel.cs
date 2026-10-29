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

namespace Lumen.App.ViewModels;

/// <summary>What kind of step the editor is adding.</summary>
public enum StepKind
{
    LaunchItem,
    LaunchProgram,
    Url,
    Folder,
    Wait,
    CloseItem,
    CloseProgram,
}

public sealed record StepKindOption(StepKind Kind, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A step in the editor's list: the action plus its sentence (and warnings).</summary>
public sealed record EditorStep(RoutineAction Action, StepLine Line);

/// <summary>
/// The simple routine editor (owner decision 2026-10-28): a name and steps added from a list. Library items come
/// from a search, programs and folders from the system pickers. Every step is checked as it is added, and blocked
/// steps can't be saved. Saving is the approval: the user wrote every step.
/// </summary>
public sealed partial class RoutineEditorViewModel : ObservableObject
{
    private readonly RoutineService _routines;
    private readonly QuickSearch _search;
    private readonly LibraryService _library;
    private readonly IDialogService _dialogs;
    private readonly IFileSystem _fileSystem;
    private readonly ILocalizer _loc;
    private readonly string? _originalId;
    private readonly Routine? _original;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _stopOnError;

    [ObservableProperty]
    private bool _pinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItemKind), nameof(IsProgramKind), nameof(IsUrlKind), nameof(IsFolderKind), nameof(IsWaitKind), nameof(IsCloseProgramKind))]
    private StepKindOption? _selectedKind;

    [ObservableProperty]
    private string _itemSearch = string.Empty;

    [ObservableProperty]
    private string _programPath = string.Empty;

    [ObservableProperty]
    private string _programArguments = string.Empty;

    [ObservableProperty]
    private string _urlInput = "https://";

    [ObservableProperty]
    private string _waitSeconds = "5";

    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private bool _forceClose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    public RoutineEditorViewModel(
        RoutineService routines, QuickSearch search, LibraryService library, IDialogService dialogs, IFileSystem fileSystem, ILocalizer loc, Routine? existing)
    {
        _routines = routines;
        _search = search;
        _library = library;
        _dialogs = dialogs;
        _fileSystem = fileSystem;
        _loc = loc;
        _original = existing;
        _originalId = existing?.Id;
        Kinds = Enum.GetValues<StepKind>().Select(k => new StepKindOption(k, loc["Routine_Kind_" + k])).ToList();
        SelectedKind = Kinds[0];
        if (existing is not null)
        {
            Name = existing.Name.For(CultureInfo.CurrentUICulture);
            StopOnError = existing.StopOnError;
            Pinned = existing.Pinned;
            foreach (var action in existing.Actions)
            {
                Add(action);
            }
        }
    }

    /// <summary>Raised when the editor should close (saved or cancelled).</summary>
    public event EventHandler? Closed;

    public bool IsNew => _originalId is null;

    public IReadOnlyList<StepKindOption> Kinds { get; }

    public ObservableCollection<EditorStep> Steps { get; } = [];

    public ObservableCollection<LibraryItem> ItemResults { get; } = [];

    public bool IsItemKind => SelectedKind?.Kind is StepKind.LaunchItem or StepKind.CloseItem;

    public bool IsProgramKind => SelectedKind?.Kind == StepKind.LaunchProgram;

    public bool IsUrlKind => SelectedKind?.Kind == StepKind.Url;

    public bool IsFolderKind => SelectedKind?.Kind == StepKind.Folder;

    public bool IsWaitKind => SelectedKind?.Kind == StepKind.Wait;

    public bool IsCloseProgramKind => SelectedKind?.Kind == StepKind.CloseProgram;

    public bool HasSteps => Steps.Count > 0;

    public bool HasProblem => !string.IsNullOrEmpty(Problem);

    partial void OnItemSearchChanged(string value)
    {
        ItemResults.Clear();
        if (value.Trim().Length == 0)
        {
            return;
        }

        foreach (var item in _search.Find(_library.Items, value, 6))
        {
            ItemResults.Add(item);
        }
    }

    partial void OnSelectedKindChanged(StepKindOption? value) => Problem = null;

    [RelayCommand]
    private void AddItem(LibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        Add(SelectedKind?.Kind == StepKind.CloseItem ? new CloseAction([item.Primary.Ref], []) : new LaunchAction(item.Primary.Ref, null));
        ItemSearch = string.Empty;
    }

    [RelayCommand]
    private void BrowseProgram()
    {
        if (_dialogs.PickFile(_loc["Routine_ProgramFilter"]) is { } path)
        {
            ProgramPath = path;
        }
    }

    [RelayCommand]
    private void AddProgram()
    {
        var path = ProgramPath.Trim().Trim('"');
        if (path.Length == 0)
        {
            Problem = _loc["Routine_Problem_PickProgram"];
            return;
        }

        var args = ProgramArguments.Trim();
        Add(new LaunchAction(null, path) { Arguments = args.Length == 0 ? null : args });
        ProgramPath = string.Empty;
        ProgramArguments = string.Empty;
    }

    [RelayCommand]
    private void AddUrl()
    {
        var url = UrlInput.Trim();
        if (url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
        {
            Problem = _loc["Routine_Problem_Url"];
            return;
        }

        Add(new UrlAction(url));
        UrlInput = "https://";
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (_dialogs.PickFolder() is { } folder)
        {
            Add(new FolderAction(folder));
        }
    }

    [RelayCommand]
    private void AddWait()
    {
        if (!double.TryParse(WaitSeconds, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) || seconds is < 0 or > 300)
        {
            Problem = _loc["Routine_Problem_Wait"];
            return;
        }

        Add(new WaitAction((int)Math.Round(seconds * 1000)));
    }

    [RelayCommand]
    private void AddCloseProgram()
    {
        var name = ProcessName.Trim();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }

        if (name.Length < 5 || name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            Problem = _loc["Routine_Problem_ProcessName"];
            return;
        }

        Add(new CloseAction([], [name]) { Force = ForceClose });
        ProcessName = string.Empty;
        ForceClose = false;
    }

    [RelayCommand]
    private void MoveUp(EditorStep? step) => Move(step, -1);

    [RelayCommand]
    private void MoveDown(EditorStep? step) => Move(step, 1);

    [RelayCommand]
    private void Remove(EditorStep? step)
    {
        if (step is not null)
        {
            Steps.Remove(step);
            Renumber();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (Steps.Any(s => s.Line.IsBlocked))
        {
            Problem = _loc["Routine_Problem_Blocked"];
            return;
        }

        if (Steps.Count == 0)
        {
            Problem = _loc["Routine_Problem_NoSteps"];
            return;
        }

        var name = Name.Trim();
        var id = _originalId ?? _routines.NewId(name);
        var routine = new Routine(id, new RoutineName(name), Steps.Select(s => s.Action).ToList())
        {
            Description = _original?.Description,
            Icon = _original?.Icon,
            Variables = _original?.Variables ?? new Dictionary<string, string>(),
            StopOnError = StopOnError,
            Pinned = Pinned,
        };
        try
        {
            await _routines.SaveAsync(routine, _originalId, CancellationToken.None);
            Closed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Problem = _loc["Routine_Problem_Save"];
        }
    }

    private bool CanSave() => Name.Trim().Length is >= 1 and <= RoutineName.MaxLength;

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    private void Add(RoutineAction action)
    {
        if (Steps.Count >= Routine.MaxActions)
        {
            Problem = string.Format(CultureInfo.CurrentCulture, _loc["Routine_Problem_TooMany"], Routine.MaxActions);
            return;
        }

        Problem = null;
        Steps.Add(new EditorStep(action, LineFor(action, Steps.Count)));
        OnPropertyChanged(nameof(HasSteps));
        if (Steps[^1].Line.IsBlocked)
        {
            Problem = Steps[^1].Line.Note;
        }
    }

    private void Move(EditorStep? step, int delta)
    {
        if (step is null)
        {
            return;
        }

        var index = Steps.IndexOf(step);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Steps.Count)
        {
            return;
        }

        Steps.Move(index, target);
        Renumber();
    }

    private void Renumber()
    {
        var actions = Steps.Select(s => s.Action).ToList();
        Steps.Clear();
        for (var i = 0; i < actions.Count; i++)
        {
            Steps.Add(new EditorStep(actions[i], LineFor(actions[i], i)));
        }

        OnPropertyChanged(nameof(HasSteps));
    }

    // The same review the preview uses, so the editor warns exactly like the preview would.
    private StepLine LineFor(RoutineAction action, int index)
    {
        var single = new Routine("draft", new RoutineName("draft"), [action]) { Variables = _original?.Variables ?? new Dictionary<string, string>() };
        var review = RoutineReview.Create(single, _routines.FindItem, _fileSystem.FileExists, _fileSystem.DirectoryExists);
        return RoutineText.Line(review.Steps[0] with { Index = index }, _loc);
    }
}
