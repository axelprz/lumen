// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Onboarding;
using Lumen.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

public enum FirstRunStep
{
    Language,
    Stores,
    Keys,
    Done,
}

/// <summary>One store row in the wizard: name, what Lumen found, and whether that is good news.</summary>
public sealed record StoreRow(string Name, string Detail, StoreState State)
{
    public bool IsFound => State == StoreState.Found;

    public override string ToString() => $"{Name}: {Detail}";
}

/// <summary>
/// First-run wizard (PLAN-v1 §4.3): language → detected stores → optional keys → done. Shown over the whole window
/// the first time Lumen opens (and again from Settings). Finishing or skipping remembers it in <c>settings_local</c>.
/// Sign-ins (M5) and app curation (M4) become extra steps when those milestones land.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly ILocalSettings _settings;
    private readonly ILocalizer _localizer;
    private readonly ILogger<FirstRunViewModel> _logger;
    private readonly SynchronizationContext _ui;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepNumber), nameof(IsFirstStep), nameof(IsLastStep), nameof(NextText), nameof(StepText))]
    [NotifyPropertyChangedFor(nameof(IsLanguageStep), nameof(IsStoresStep), nameof(IsKeysStep), nameof(IsDoneStep))]
    private FirstRunStep _step;

    public FirstRunViewModel(
        LanguageSettingsViewModel language,
        SteamSettingsViewModel steam,
        ArtworkSettingsViewModel artwork,
        LibraryService library,
        ILocalSettings settings,
        ILocalizer localizer,
        ILogger<FirstRunViewModel> logger)
    {
        Language = language;
        Steam = steam;
        Artwork = artwork;
        _library = library;
        _settings = settings;
        _localizer = localizer;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _library.FullScanCompleted += (_, _) => _ui.Post(_ => RefreshStores(), null);
        _localizer.CultureChanged += (_, _) =>
        {
            RefreshStores();
            OnPropertyChanged(nameof(NextText));
            OnPropertyChanged(nameof(StepText));
        };
    }

    public LanguageSettingsViewModel Language { get; }

    public SteamSettingsViewModel Steam { get; }

    public ArtworkSettingsViewModel Artwork { get; }

    public IReadOnlyList<StoreRow> Stores { get; private set; } = [];

    /// <summary>True until the first scan of every store finishes.</summary>
    public bool IsSearchingStores => _library.LastFullScan is null;

    /// <summary>"Lumen found 3 stores with 42 installed games."</summary>
    public string StoresSummary { get; private set; } = string.Empty;

    public static int StepCount => Enum.GetValues<FirstRunStep>().Length;

    public int StepNumber => (int)Step + 1;

    /// <summary>"Step 2 of 4".</summary>
    public string StepText => string.Format(CultureInfo.CurrentCulture, _localizer["FirstRun_StepOf"], StepNumber, StepCount);

    public bool IsFirstStep => Step == FirstRunStep.Language;

    public bool IsLastStep => Step == FirstRunStep.Done;

    public bool IsLanguageStep => Step == FirstRunStep.Language;

    public bool IsStoresStep => Step == FirstRunStep.Stores;

    public bool IsKeysStep => Step == FirstRunStep.Keys;

    public bool IsDoneStep => Step == FirstRunStep.Done;

    public string NextText => _localizer[IsLastStep ? "FirstRun_Start" : "FirstRun_Next"];

    /// <summary>Opens the wizard when it was never finished. Call before the main window is shown.</summary>
    public async Task OpenIfNeededAsync(CancellationToken ct)
    {
        string? done = null;
        try
        {
            done = await _settings.GetAsync(LocalSettingKeys.FirstRunCompleted, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStateFailed(ex);
        }

        if (done != "1")
        {
            Open();
        }
    }

    /// <summary>Shows the wizard from the first step (Settings → "Show the welcome again").</summary>
    [RelayCommand]
    public void Open()
    {
        Step = FirstRunStep.Language;
        RefreshStores();
        IsOpen = true;
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsLastStep)
        {
            await FinishAsync();
            return;
        }

        Step++;
    }

    [RelayCommand]
    private void Back()
    {
        if (!IsFirstStep)
        {
            Step--;
        }
    }

    /// <summary>Skipping counts as finished: everything in the wizard is also in Settings.</summary>
    [RelayCommand]
    private Task SkipAsync() => FinishAsync();

    private async Task FinishAsync()
    {
        IsOpen = false;
        try
        {
            await _settings.SetAsync(LocalSettingKeys.FirstRunCompleted, "1", CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStateFailed(ex); // worst case it shows again next time
        }
    }

    private void RefreshStores()
    {
        var findings = _library.LastFullScan is { } scan ? StoreFindings.From(scan) : [];
        Stores = findings.Select(ToRow).ToList();
        var found = findings.Count(f => f.State == StoreState.Found);
        var games = findings.Sum(f => f.Installed);
        StoresSummary = IsSearchingStores
            ? _localizer["FirstRun_Stores_Searching"]
            : string.Format(CultureInfo.CurrentCulture, _localizer["FirstRun_Stores_Summary"], found, games);
        OnPropertyChanged(nameof(Stores));
        OnPropertyChanged(nameof(StoresSummary));
        OnPropertyChanged(nameof(IsSearchingStores));
    }

    private StoreRow ToRow(StoreFinding finding)
    {
        var detail = finding.State switch
        {
            StoreState.Found => string.Format(CultureInfo.CurrentCulture, _localizer[finding.Installed == 1 ? "FirstRun_Store_OneGame" : "FirstRun_Store_Games"], finding.Installed),
            StoreState.Empty => _localizer["FirstRun_Store_Empty"],
            StoreState.Problem => _localizer["FirstRun_Store_Problem"],
            _ => _localizer["FirstRun_Store_NotInstalled"],
        };
        if (finding.OwnedNotInstalled > 0)
        {
            detail += " · " + string.Format(CultureInfo.CurrentCulture, _localizer["FirstRun_Store_Owned"], finding.OwnedNotInstalled);
        }

        return new StoreRow(finding.DisplayName, detail, finding.State);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read or save the first-run state")]
    private partial void LogStateFailed(Exception exception);
}
