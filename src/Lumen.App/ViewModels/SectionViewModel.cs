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

/// <summary>A small status dot next to a sidebar entry.</summary>
public enum SectionDot
{
    None,

    /// <summary>Something is running (green).</summary>
    Active,

    /// <summary>Something waits for the user (amber).</summary>
    Attention,
}

/// <summary>One sidebar entry and the page it shows.</summary>
public sealed class SectionViewModel : ObservableObject
{
    private int? _count;
    private SectionDot _dot;

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

    /// <summary>A number shown as a pill (the library's size); null shows nothing.</summary>
    public int? Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(HasCount));
            }
        }
    }

    public bool HasCount => Count is > 0;

    public SectionDot Dot
    {
        get => _dot;
        set => SetProperty(ref _dot, value);
    }

    // What screen readers announce for the sidebar entries (instead of the type name).
    public override string ToString() => Title;

    /// <summary>The view model shown in the content area; views are picked by DataTemplate type.</summary>
    public object Page { get; }
}
