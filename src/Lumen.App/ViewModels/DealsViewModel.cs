// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Deals;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>One free game in the list, written in the current language when the list is built.</summary>
public sealed record DealRow(string Title, string StoreText, string StatusText, string? WorthText, string Url);

/// <summary>
/// The Deals page (PLAN-v1 M13). It is off until the user turns it on (the page then shows what it contacts and a
/// button; Settings has the switch too), and nothing is requested before that. The free games come from
/// <see cref="FreeGamesService"/>. The Steamcito card only holds links to open in the user's browser (not Lumen requests).
/// </summary>
public sealed partial class DealsViewModel : ObservableObject
{
    public const string SteamcitoSite = "https://steamcito.com.ar/";

    public const string SteamcitoGuide = "https://steamcito.com.ar/machete-del-ahorro-en-gaming-en-argentina";

    private enum NoteKind
    {
        None,
        Empty,
        Failed,
        Partial,
    }

    private readonly ILocalSettings _settings;
    private readonly FreeGamesService _service;
    private readonly ILauncher _shell;
    private readonly ILocalizer _loc;
    private readonly ILogger<DealsViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private FreeGamesSnapshot? _snapshot;
    private bool _loading;
    private bool _loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    [NotifyPropertyChangedFor(nameof(NoteText))]
    private int _note;

    public DealsViewModel(ILocalSettings settings, FreeGamesService service, ILauncher shell, ILocalizer loc, ILogger<DealsViewModel> logger)
    {
        _settings = settings;
        _service = service;
        _shell = shell;
        _loc = loc;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        loc.CultureChanged += (_, _) => _ui.Post(_ => Rebuild(), null);
    }

    public bool IsDisabled => !IsEnabled;

    public ObservableCollection<DealRow> FreeNow { get; } = [];

    public ObservableCollection<DealRow> Upcoming { get; } = [];

    public bool HasFreeNow => FreeNow.Count > 0;

    public bool HasUpcoming => Upcoming.Count > 0;

    public bool HasNote => (NoteKind)Note != NoteKind.None;

    public string? NoteText => (NoteKind)Note switch
    {
        NoteKind.Empty => _loc["Deals_Empty"],
        NoteKind.Failed => _loc["Deals_Failed"],
        NoteKind.Partial => _loc["Deals_Partial"],
        _ => null,
    };

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            _loading = true;
            IsEnabled = await _settings.GetAsync(LocalSettingKeys.DealsEnabled, ct) == "1";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex); // stays off
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Called when the page is shown: asks the services once (their answer is reused for a while).</summary>
    public async Task EnsureLoadedAsync()
    {
        if (IsEnabled && !_loaded && !IsLoading)
        {
            await LoadAsync(false);
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_loading)
        {
            _ = SaveEnabledAsync(value);
        }

        if (!value)
        {
            _snapshot = null;
            _loaded = false;
            Rebuild();
        }
    }

    [RelayCommand]
    private async Task EnableAsync()
    {
        IsEnabled = true;
        await LoadAsync(false);
    }

    [RelayCommand]
    private void Disable() => IsEnabled = false;

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(true);

    [RelayCommand]
    private Task OpenAsync(DealRow? row) => row is null ? Task.CompletedTask : OpenUrlAsync(row.Url);

    [RelayCommand]
    private Task OpenSteamcitoGuideAsync() => OpenUrlAsync(SteamcitoGuide);

    [RelayCommand]
    private Task OpenSteamcitoSiteAsync() => OpenUrlAsync(SteamcitoSite);

    private async Task LoadAsync(bool refresh)
    {
        if (!IsEnabled || IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            _snapshot = await _service.GetAsync(refresh, CancellationToken.None);
            _loaded = true;
            Rebuild();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
            _snapshot = null;
            Note = (int)NoteKind.Failed;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Opens in the user's browser; only links that pass the check (https, the hosts Lumen links to) are ever opened.
    private async Task OpenUrlAsync(string url)
    {
        if (!DealUrls.IsSafeToOpen(url))
        {
            return;
        }

        try
        {
            await _shell.StartAsync(new LaunchSpec(LaunchKind.Uri, url), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOpenFailed(ex);
        }
    }

    private void Rebuild()
    {
        FreeNow.Clear();
        Upcoming.Clear();
        if (_snapshot is { } snapshot)
        {
            foreach (var game in snapshot.Games)
            {
                (game.IsUpcoming ? Upcoming : FreeNow).Add(ToRow(game));
            }

            Note = (int)(snapshot.AllFailed ? NoteKind.Failed
                : snapshot.Games.Count == 0 ? NoteKind.Empty
                : snapshot.AnyFailed ? NoteKind.Partial
                : NoteKind.None);
        }
        else if ((NoteKind)Note != NoteKind.Failed || !IsEnabled)
        {
            Note = (int)NoteKind.None;
        }

        OnPropertyChanged(nameof(HasFreeNow));
        OnPropertyChanged(nameof(HasUpcoming));
        OnPropertyChanged(nameof(NoteText));
    }

    private DealRow ToRow(FreeGame game)
    {
        var culture = CultureInfo.CurrentCulture;
        var status = game.IsUpcoming
            ? string.Format(culture, _loc["Deals_FreeFrom"], game.StartsAt!.Value.LocalDateTime.ToString("d", culture))
            : game.EndsAt is { } end
                ? string.Format(culture, _loc["Deals_FreeUntil"], end.LocalDateTime.ToString("d", culture))
                : _loc["Deals_NoEndDate"];
        var worth = game.Worth is null ? null : string.Format(culture, _loc["Deals_Worth"], game.Worth);
        return new DealRow(game.Title, game.Store, status, worth, game.Url);
    }

    private async Task SaveEnabledAsync(bool value)
    {
        try
        {
            await _settings.SetAsync(LocalSettingKeys.DealsEnabled, value ? "1" : "0", CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex); // still applies for this session
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the Deals page")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the Deals switch")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open a link from the Deals page")]
    private partial void LogOpenFailed(Exception exception);
}
