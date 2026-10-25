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

/// <summary>One sidebar entry and the page it shows.</summary>
public sealed class SectionViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly string _titleKey;

    public SectionViewModel(ShellSection section, string titleKey, string iconGlyph, ILocalizer localizer, object? page = null)
    {
        Section = section;
        _titleKey = titleKey;
        IconGlyph = iconGlyph;
        _localizer = localizer;
        Page = page ?? new PlaceholderPageViewModel(this);
        _localizer.CultureChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public ShellSection Section { get; }

    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets code point.</summary>
    public string IconGlyph { get; }

    public string Title => _localizer[_titleKey];

    /// <summary>The view model shown in the content area; views are picked by DataTemplate type.</summary>
    public object Page { get; }
}
