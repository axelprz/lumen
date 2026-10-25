// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>Commands a tile can run; created by <see cref="LibraryViewModel"/>.</summary>
public sealed record TileCommands(ICommand Primary, ICommand? Edit, ICommand? Remove);

/// <summary>One cover in the library grid. Artwork arrives in M3; until then the tile shows the title's initial.</summary>
public sealed class LibraryTileViewModel
{
    public LibraryTileViewModel(LibraryEntry entry, string storeName, ILocalizer localizer, TileCommands commands)
    {
        Entry = entry;
        StoreName = storeName;
        PrimaryCommand = commands.Primary;
        EditCommand = commands.Edit;
        RemoveCommand = commands.Remove;
        CanRunPrimary = LaunchService.PrimaryAction(entry) is not null;
        ActionText = localizer[!CanRunPrimary ? "Action_Unavailable" : entry.IsInstalled ? "Action_Play" : "Action_Install"];
        StateText = entry.IsInstalled ? string.Empty : localizer[CanRunPrimary ? "Library_NotInstalled" : "Library_NotAvailable"];
        Initial = entry.Title.Length == 0
            ? "?"
            : StringInfo.GetNextTextElement(entry.Title).ToUpper(CultureInfo.CurrentCulture);
    }

    public LibraryEntry Entry { get; }

    public string Title => Entry.Title;

    public bool IsInstalled => Entry.IsInstalled;

    /// <summary>False when there is nothing to run (e.g. a manual item whose file is not on this PC).</summary>
    public bool CanRunPrimary { get; }

    public bool IsManual => Entry.Ref.Source == ManualItem.Source;

    public string StoreName { get; }

    public string Initial { get; }

    /// <summary>"Play", "Install" or "Unavailable".</summary>
    public string ActionText { get; }

    public string StateText { get; }

    public ICommand PrimaryCommand { get; }

    /// <summary>Manual items only.</summary>
    public ICommand? EditCommand { get; }

    /// <summary>Manual items only.</summary>
    public ICommand? RemoveCommand { get; }
}
