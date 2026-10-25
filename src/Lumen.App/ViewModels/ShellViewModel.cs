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

    public ShellViewModel(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        Sections =
        [
            new SectionViewModel(ShellSection.Home, "Nav_Home", "", localizer),
            new SectionViewModel(ShellSection.Library, "Nav_Library", "", localizer),
            new SectionViewModel(ShellSection.Collections, "Nav_Collections", "", localizer),
            new SectionViewModel(ShellSection.Routines, "Nav_Routines", "", localizer),
            new SectionViewModel(ShellSection.Settings, "Nav_Settings", "", localizer),
        ];
        SelectedSection = Sections[0];
    }

    public IReadOnlyList<SectionViewModel> Sections { get; }
}
