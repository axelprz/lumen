// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>Settings page. Grows section by section (language, stores, artwork, sync...) in later milestones.</summary>
public sealed partial class SettingsViewModel(
    SteamSettingsViewModel steam,
    IMergeRuleStore mergeRules,
    LibraryService library,
    ILocalizer localizer) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryStatus))]
    private string? _libraryStatus;

    public SteamSettingsViewModel Steam { get; } = steam;

    public bool HasLibraryStatus => !string.IsNullOrEmpty(LibraryStatus);

    public Task InitializeAsync(CancellationToken ct) => Steam.InitializeAsync(ct);

    /// <summary>Forgets "separate"/"always use" decisions so games are grouped automatically again.</summary>
    [RelayCommand]
    private async Task ResetMergesAsync()
    {
        await mergeRules.ResetAsync(CancellationToken.None);
        await library.ReloadMergeRulesAsync(CancellationToken.None);
        LibraryStatus = localizer["Settings_Library_ResetDone"];
    }
}
