// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.App.Services;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>Main window: sidebar sections and the currently selected one.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly AccentService _accent;
    private bool _returnToHome;

    [ObservableProperty]
    private SectionViewModel? _selectedSection;

    public ShellViewModel(ILocalizer localizer, HomeViewModel home, LibraryViewModel library, SettingsViewModel settings, AccentService accent, FirstRunViewModel firstRun, CollectionsViewModel collections, RoutinesViewModel routines)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(accent);

        _accent = accent;
        FirstRun = firstRun;
        Home = home;
        Library = library;
        Settings = settings;
        Sections =
        [
            new SectionViewModel(ShellSection.Home, "Nav_Home", "", localizer, home),
            new SectionViewModel(ShellSection.Library, "Nav_Library", "", localizer, library),
            new SectionViewModel(ShellSection.Collections, "Nav_Collections", "", localizer, collections),
            new SectionViewModel(ShellSection.Routines, "Nav_Routines", "", localizer, routines),
            new SectionViewModel(ShellSection.Settings, "Nav_Settings", "", localizer, settings),
        ];

        routines.ReviewRequested += (_, _) => SelectedSection = Sections.First(s => s.Section == ShellSection.Routines);
        home.DetailRequested += (_, item) => _ = ShowDetailFromHomeAsync(item);
        library.DetailClosed += (_, _) => OnDetailClosed();
        collections.OpenRequested += (_, id) =>
        {
            SelectedSection = Sections.First(s => s.Section == ShellSection.Library);
            Library.ShowCollection(id);
        };
        SelectedSection = Sections[0];
    }

    public IReadOnlyList<SectionViewModel> Sections { get; }

    public HomeViewModel Home { get; }

    /// <summary>The first-run wizard, shown over everything while open.</summary>
    public FirstRunViewModel FirstRun { get; }

    public LibraryViewModel Library { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Moves to the previous (-1) or next (+1) sidebar section (gamepad LB/RB). No wrap-around.</summary>
    /// <returns>False when there is no such section or the welcome wizard covers the sidebar.</returns>
    public bool SelectAdjacentSection(int delta)
    {
        if (FirstRun.IsOpen || SelectedSection is null)
        {
            return false;
        }

        var index = Sections.ToList().IndexOf(SelectedSection) + delta;
        if (index < 0 || index >= Sections.Count)
        {
            return false;
        }

        SelectedSection = Sections[index];
        return true;
    }

    /// <summary>The accent follows what is on screen: Home's featured game, an open detail, else the default.</summary>
    partial void OnSelectedSectionChanged(SectionViewModel? value)
    {
        if (value?.Section != ShellSection.Library)
        {
            _returnToHome = false;
        }

        Home.SetActive(value?.Section == ShellSection.Home);
        if (value?.Section != ShellSection.Home && !(value?.Section == ShellSection.Library && Library.HasDetail))
        {
            _accent.Reset();
        }
    }

    // Home has no detail view of its own: show it in the library, and come back to Home when it closes.
    private async Task ShowDetailFromHomeAsync(LibraryItem item)
    {
        SelectedSection = Sections.First(s => s.Section == ShellSection.Library);
        _returnToHome = true;
        await Library.ShowDetailAsync(item);
    }

    private void OnDetailClosed()
    {
        if (_returnToHome && SelectedSection?.Section == ShellSection.Library)
        {
            _returnToHome = false;
            SelectedSection = Sections[0];
        }
        else
        {
            _accent.Reset();
        }
    }
}
