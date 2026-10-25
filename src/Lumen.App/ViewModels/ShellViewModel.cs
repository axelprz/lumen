// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>Main window: sidebar sections and the currently selected one.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private SectionViewModel? _selectedSection;

    public ShellViewModel(ILocalizer localizer, LibraryViewModel library)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(library);

        Library = library;
        Sections =
        [
            new SectionViewModel(ShellSection.Home, "Nav_Home", "", localizer),
            new SectionViewModel(ShellSection.Library, "Nav_Library", "", localizer, library),
            new SectionViewModel(ShellSection.Collections, "Nav_Collections", "", localizer),
            new SectionViewModel(ShellSection.Routines, "Nav_Routines", "", localizer),
            new SectionViewModel(ShellSection.Settings, "Nav_Settings", "", localizer),
        ];

        // Home is a placeholder until M3, so open on the library, which is what M1 delivers.
        SelectedSection = Sections[1];
    }

    public IReadOnlyList<SectionViewModel> Sections { get; }

    public LibraryViewModel Library { get; }
}
