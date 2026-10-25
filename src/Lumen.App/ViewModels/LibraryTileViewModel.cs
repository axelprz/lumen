// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>One cover in the library grid. Artwork arrives in M3; until then the tile shows the title's initial.</summary>
public sealed class LibraryTileViewModel
{
    public LibraryTileViewModel(LibraryEntry entry, string storeName, ILocalizer localizer, ICommand primaryCommand)
    {
        Entry = entry;
        StoreName = storeName;
        PrimaryCommand = primaryCommand;
        ActionText = localizer[entry.IsInstalled ? "Action_Play" : "Action_Install"];
        StateText = entry.IsInstalled ? string.Empty : localizer["Library_NotInstalled"];
        Initial = entry.Title.Length == 0
            ? "?"
            : StringInfo.GetNextTextElement(entry.Title).ToUpper(CultureInfo.CurrentCulture);
    }

    public LibraryEntry Entry { get; }

    public string Title => Entry.Title;

    public bool IsInstalled => Entry.IsInstalled;

    public string StoreName { get; }

    public string Initial { get; }

    /// <summary>"Play" when installed, "Install" otherwise.</summary>
    public string ActionText { get; }

    public string StateText { get; }

    /// <summary>Runs the primary action (play or install) for this tile.</summary>
    public ICommand PrimaryCommand { get; }
}
