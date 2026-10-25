// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

public enum ShellSection
{
    Home,
    Library,
    Collections,
    Routines,
    Settings,
}

/// <summary>One sidebar entry. Each section gets its own page view model in later milestones.</summary>
public sealed class SectionViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly string _titleKey;

    public SectionViewModel(ShellSection section, string titleKey, string iconGlyph, ILocalizer localizer)
    {
        Section = section;
        _titleKey = titleKey;
        IconGlyph = iconGlyph;
        _localizer = localizer;
        _localizer.CultureChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public ShellSection Section { get; }

    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets code point.</summary>
    public string IconGlyph { get; }

    public string Title => _localizer[_titleKey];
}
