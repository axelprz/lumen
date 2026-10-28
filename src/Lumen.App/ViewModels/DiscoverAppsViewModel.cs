// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>One detected app in "Discover apps".</summary>
public sealed record AppSuggestion(LibraryEntry Entry, string Title, string Source, string? Location)
{
    public bool HasLocation => !string.IsNullOrEmpty(Location);

    public override string ToString() => Title;
}

/// <summary>
/// "Discover apps" (PLAN-v1 §4.2): apps detected on this PC that aren't in the library yet. One click adds or ignores;
/// ignored apps stay ignored across rescans and can be brought back. Opened from the library, and shown as a step of
/// the first-run wizard.
/// </summary>
public sealed partial class DiscoverAppsViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly IAppChoiceStore _choices;
    private readonly TitleNormalizer _normalizer;
    private readonly ILocalizer _localizer;
    private readonly ILogger<DiscoverAppsViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private IReadOnlyList<LibraryEntry> _suggested = [];
    private IReadOnlyList<LibraryEntry> _ignored = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleIgnoredText))]
    private bool _showIgnored;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public DiscoverAppsViewModel(LibraryService library, IAppChoiceStore choices, TitleNormalizer normalizer, ILocalizer localizer, ILogger<DiscoverAppsViewModel> logger)
    {
        _library = library;
        _choices = choices;
        _normalizer = normalizer;
        _localizer = localizer;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _library.Changed += (_, _) => _ui.Post(_ => Refresh(), null);
        _localizer.CultureChanged += (_, _) => Refresh();
    }

    /// <summary>What the list shows: suggestions or ignored apps, narrowed by the search.</summary>
    public IReadOnlyList<AppSuggestion> Visible { get; private set; } = [];

    /// <summary>Suggestions waiting for a decision (the library button's count).</summary>
    public int SuggestionCount => _suggested.Count;

    public int IgnoredCount => _ignored.Count;

    public bool HasIgnored => _ignored.Count > 0 || ShowIgnored;

    public bool IsEmpty => Visible.Count == 0;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>"Discover apps · 12" for the library header.</summary>
    public string ButtonText => SuggestionCount == 0
        ? _localizer["Discover_Title"]
        : string.Format(CultureInfo.CurrentCulture, _localizer["Discover_ButtonCount"], SuggestionCount);

    public string ToggleIgnoredText => ShowIgnored
        ? _localizer["Discover_BackToSuggestions"]
        : string.Format(CultureInfo.CurrentCulture, _localizer["Discover_ShowIgnored"], IgnoredCount);

    public string EmptyText => _localizer[
        !string.IsNullOrWhiteSpace(SearchText) ? "Discover_NoMatches"
        : ShowIgnored ? "Discover_NoIgnored"
        : "Discover_AllReviewed"];

    [RelayCommand]
    private void Open()
    {
        SearchText = string.Empty;
        ShowIgnored = false;
        Notice = null;
        Refresh();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ToggleIgnored() => ShowIgnored = !ShowIgnored;

    [RelayCommand]
    private Task AddAsync(AppSuggestion? app) => SetAsync(app, AppChoice.Added, "Discover_Added");

    [RelayCommand]
    private Task IgnoreAsync(AppSuggestion? app) => SetAsync(app, AppChoice.Ignored, "Discover_IgnoredNotice");

    /// <summary>An ignored app becomes a suggestion again.</summary>
    [RelayCommand]
    private Task RestoreAsync(AppSuggestion? app) => SetAsync(app, null, null);

    /// <summary>Takes an added app out of the library; it goes back to the suggestions (tile menu).</summary>
    public Task RemoveFromLibraryAsync(LibraryEntry entry) =>
        SetAsync(new AppSuggestion(entry, entry.Title, string.Empty, null), null, null);

    partial void OnShowIgnoredChanged(bool value) => Refresh();

    partial void OnSearchTextChanged(string value) => Refresh();

    private async Task SetAsync(AppSuggestion? app, AppChoice? choice, string? noticeKey)
    {
        if (app is null)
        {
            return;
        }

        try
        {
            await _choices.SetAsync([app.Entry.Ref], choice, CancellationToken.None);
            await _library.ReloadUserChoicesAsync(CancellationToken.None);
            Notice = noticeKey is null ? null : string.Format(CultureInfo.CurrentCulture, _localizer[noticeKey], app.Title);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex);
            Notice = _localizer["Discover_SaveFailed"];
        }

        Refresh();
    }

    private void Refresh()
    {
        var entries = _library.Entries;
        var choices = _library.AppChoices;
        _suggested = AppCuration.Detected(entries, choices, _normalizer);
        _ignored = AppCuration.Detected(entries, choices, _normalizer, AppChoice.Ignored);

        var source = ShowIgnored ? _ignored : _suggested;
        var search = SearchText.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        Visible = source
            .Where(e => search.Length == 0 || compare.IndexOf(e.Title, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .Select(ToSuggestion)
            .ToList();

        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(SuggestionCount));
        OnPropertyChanged(nameof(IgnoredCount));
        OnPropertyChanged(nameof(HasIgnored));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(ToggleIgnoredText));
        OnPropertyChanged(nameof(EmptyText));
    }

    private AppSuggestion ToSuggestion(LibraryEntry entry) => new(
        entry,
        entry.Title,
        _localizer[entry.Ref.Source == "appsfolder" ? "Store_MicrosoftStore" : "Store_StartMenu"],
        entry.Extra.GetValueOrDefault("target") ?? entry.Extra.GetValueOrDefault("url"));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save an app choice")]
    private partial void LogSaveFailed(Exception exception);
}
