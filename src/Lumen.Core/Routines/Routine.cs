// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Library;

namespace Lumen.Core.Routines;

/// <summary>
/// A routine (PLAN-v1 §4.5, <c>docs/schemas/routine.schema.json</c>): an ordered list of actions run with one click.
/// v1 actions only open things (games, apps, web pages, folders), wait, or close programs; never scripts or
/// commands, never elevated.
/// </summary>
public sealed record Routine(
    string Id,
    RoutineName Name,
    IReadOnlyList<RoutineAction> Actions)
{
    public const int SchemaVersion = 1;
    public const int MaxActions = 50;

    public string? Description { get; init; }

    public string? Icon { get; init; }

    /// <summary>Shown as a card on Home.</summary>
    public bool Pinned { get; init; }

    /// <summary>Stop at the first failed action instead of carrying on.</summary>
    public bool StopOnError { get; init; }

    /// <summary>Values usable as <c>{name}</c> inside text parameters.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>A valid id: a slug, also the file name (<c>routines/{id}.json</c>).</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: >= 1 and <= 64 } && id[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
        && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}

/// <summary>A routine's name: one text, or one per language ("es", "en-US", …).</summary>
public sealed record RoutineName(string? Text, IReadOnlyDictionary<string, string>? ByCulture = null)
{
    public const int MaxLength = 80;

    /// <summary>The name for a culture: exact match, then its language, then English, then any.</summary>
    public string For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (Text is not null || ByCulture is null || ByCulture.Count == 0)
        {
            return Text ?? string.Empty;
        }

        return ByCulture.GetValueOrDefault(culture.Name)
            ?? ByCulture.GetValueOrDefault(culture.TwoLetterISOLanguageName)
            ?? ByCulture.GetValueOrDefault("en")
            ?? ByCulture.OrderBy(kv => kv.Key, StringComparer.Ordinal).First().Value;
    }

    public override string ToString() => For(CultureInfo.CurrentUICulture);
}

/// <summary>One step of a routine.</summary>
public abstract record RoutineAction
{
    /// <summary>Optional text shown instead of the generated description.</summary>
    public string? Label { get; init; }

    /// <summary>How long the step may take before it counts as failed.</summary>
    public int? TimeoutMs { get; init; }
}

/// <summary>Opens a library item (<see cref="Ref"/>) or a program by path (<see cref="Path"/>), exactly one of them.</summary>
public sealed record LaunchAction(ItemRef? Ref, string? Path) : RoutineAction
{
    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>Do nothing when it is already running (default).</summary>
    public bool SkipIfRunning { get; init; } = true;
}

/// <summary>Opens a web page (http/https only) in the default browser.</summary>
public sealed record UrlAction(string Url) : RoutineAction;

/// <summary>Opens a folder in File Explorer.</summary>
public sealed record FolderAction(string Path) : RoutineAction;

/// <summary>Waits before the next step.</summary>
public sealed record WaitAction(int Milliseconds) : RoutineAction;

/// <summary>
/// Closes library items and/or programs by image name ("discord.exe"): asks them to close, waits
/// <see cref="GraceMs"/>, then ends them only when <see cref="Force"/> is set.
/// </summary>
public sealed record CloseAction(IReadOnlyList<ItemRef> Refs, IReadOnlyList<string> Processes) : RoutineAction
{
    public const int DefaultGraceMs = 5000;

    public int GraceMs { get; init; } = DefaultGraceMs;

    public bool Force { get; init; }
}
