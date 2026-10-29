// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Localization;
using Lumen.Core.Routines;

namespace Lumen.App.ViewModels;

/// <summary>A routine on the Routines page.</summary>
public sealed partial class RoutineCardViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Programs that ignored Undo's polite request; "Force close" ends exactly these.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanForceClose))]
    private IReadOnlyList<Lumen.Core.Platform.RunningProcess> _stillOpen = [];

    public bool CanForceClose => StillOpen.Count > 0;

    public RoutineCardViewModel(RoutineEntry entry, ILocalizer loc)
    {
        Entry = entry;
        Name = entry.Routine?.Name.For(CultureInfo.CurrentUICulture) ?? entry.FileId;
        Description = entry.Routine?.Description;
        Summary = entry.Routine is { } routine
            ? string.Format(CultureInfo.CurrentCulture, loc[routine.Actions.Count == 1 ? "Routine_OneStep" : "Routine_Steps"], routine.Actions.Count)
            : loc["Routine_HasErrors"];
        StateText = loc[entry.State switch
        {
            RoutineTrustState.Trusted => "Routine_State_Ready",
            RoutineTrustState.NeedsReview => "Routine_State_NeedsReview",
            _ => entry.Review is { CanRun: false } ? "Routine_State_Blocked" : "Routine_State_Invalid",
        }];
    }

    public RoutineEntry Entry { get; }

    public string Name { get; }

    public string? Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string Summary { get; }

    public string StateText { get; }

    public bool IsReady => Entry.State == RoutineTrustState.Trusted;

    public bool NeedsReview => Entry.State == RoutineTrustState.NeedsReview;

    public bool IsInvalid => Entry.State == RoutineTrustState.Invalid;

    /// <summary>Can be opened in the editor (a valid file; blocked steps can be removed there).</summary>
    public bool CanEdit => Entry.Routine is not null;

    public bool HasResult => !string.IsNullOrEmpty(ResultText);
}

/// <summary>The trust preview (PLAN-v1 §4.5): every step listed before a new or changed routine may run.</summary>
public sealed class RoutineReviewViewModel(RoutineEntry entry, ILocalizer loc)
{
    public RoutineEntry Entry { get; } = entry;

    public string Name { get; } = entry.Routine?.Name.For(CultureInfo.CurrentUICulture) ?? entry.FileId;

    public IReadOnlyList<StepLine> Steps { get; } = entry.Review is { } review ? RoutineText.Lines(review, loc) : [];

    /// <summary>Why the file can't be used (technical, from the validator).</summary>
    public IReadOnlyList<string> Errors { get; } = entry.Parse.Errors;

    public bool HasErrors => Errors.Count > 0;

    public bool CanApprove => Entry.Review is { CanRun: true } && Entry.State != RoutineTrustState.Trusted;

    public bool IsBlocked => Entry.Review is { CanRun: false };

    /// <summary>"New" or "changed outside Lumen" wording isn't knowable for sure; the hint just says it needs a look.</summary>
    public string Hint { get; } = loc[entry.Review is { CanRun: false } ? "Routine_Review_BlockedHint" : entry.State == RoutineTrustState.Trusted ? "Routine_Review_TrustedHint" : "Routine_Review_Hint"];
}
