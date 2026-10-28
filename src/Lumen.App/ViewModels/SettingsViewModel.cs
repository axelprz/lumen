// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>Settings page. Grows section by section (language, stores, artwork, sync...) in later milestones.</summary>
public sealed partial class SettingsViewModel(
    LanguageSettingsViewModel language,
    FirstRunViewModel firstRun,
    SteamSettingsViewModel steam,
    ArtworkSettingsViewModel artwork,
    IMergeRuleStore mergeRules,
    LibraryService library,
    ILocalizer localizer) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryStatus))]
    private string? _libraryStatus;

    public LanguageSettingsViewModel Language { get; } = language;

    public FirstRunViewModel FirstRun { get; } = firstRun;

    public SteamSettingsViewModel Steam { get; } = steam;

    public ArtworkSettingsViewModel Artwork { get; } = artwork;

    public bool HasLibraryStatus => !string.IsNullOrEmpty(LibraryStatus);

    public async Task InitializeAsync(CancellationToken ct)
    {
        await Steam.InitializeAsync(ct);
        await Artwork.InitializeAsync(ct);
    }

    /// <summary>Forgets "separate", "merge" and "always use" decisions so games are grouped automatically again.</summary>
    [RelayCommand]
    private async Task ResetMergesAsync()
    {
        await mergeRules.ResetAsync(CancellationToken.None);
        await library.ReloadUserChoicesAsync(CancellationToken.None);
        LibraryStatus = localizer["Settings_Library_ResetDone"];
    }
}
