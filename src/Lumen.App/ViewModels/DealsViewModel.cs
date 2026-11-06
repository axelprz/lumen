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
public sealed record DealRow(string Title, string StoreText, string StatusText, string? WorthText, string Url, string? ImageUrl = null);

/// <summary>One offer in "Best deals".</summary>
public sealed record PriceRow(
    string Title,
    string Store,
    string PriceText,
    string DetailText,
    string Url,
    string? ImageUrl = null,
    string? OldPriceText = null,
    string? DiscountText = null,
    decimal Price = 0);

/// <summary>One store filter of the Deals grid. <see cref="Key"/> is "all", "under5" or a store name; the label is written on read.</summary>
public sealed partial class DealFilter(string key, Func<string> label) : ObservableObject
{
    public string Key { get; } = key;

    public string Label => label();

    [ObservableProperty]
    private bool _isSelected;

    public void Refresh() => OnPropertyChanged(nameof(Label));
}

/// <summary>One game of a price search: the best price and every store's price.</summary>
public sealed record PriceGameRow(string Title, string BestText, string StoresText, string Url);

/// <summary>
/// The Deals page (PLAN-v1 M13). It is off until the user turns it on (the page then shows what it contacts and a
/// button; Settings has the switch too), and nothing is requested before that. The free games come from
/// <see cref="FreeGamesService"/>. The Steamcito card only holds links to open in the user's browser (not Lumen requests).
/// </summary>
public sealed partial class DealsViewModel : ObservableObject
{
    public const string SteamcitoSite = "https://steamcito.com.ar/";

    public const string SteamcitoGuide = "https://steamcito.com.ar/machete-del-ahorro-en-gaming-en-argentina";

    public const string CheapSharkSite = "https://www.cheapshark.com/";

    private enum SearchOutcome
    {
        None,
        NoResults,
        Failed,
        TooShort,
    }

    private enum NoteKind
    {
        None,
        Empty,
        Failed,
        Partial,
    }

    private readonly ILocalSettings _settings;
    private readonly FreeGamesService _service;
    private readonly PriceDealsService _prices;
    private readonly TrendingService _trending;
    private readonly ILauncher _shell;
    private readonly ILocalizer _loc;
    private readonly ILogger<DealsViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private FreeGamesSnapshot? _snapshot;
    private BestDealsSnapshot? _best;
    private TrendingSnapshot? _top;
    private PriceSearchSnapshot? _search;
    private bool _loading;
    private bool _loaded;
    private string _filter = "all";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchNote))]
    [NotifyPropertyChangedFor(nameof(SearchNoteText))]
    private int _searchState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    [NotifyPropertyChangedFor(nameof(NoteText))]
    private int _note;

    public DealsViewModel(ILocalSettings settings, FreeGamesService service, PriceDealsService prices, TrendingService trending, ILauncher shell, ILocalizer loc, ILogger<DealsViewModel> logger)
    {
        _settings = settings;
        _service = service;
        _prices = prices;
        _trending = trending;
        _shell = shell;
        _loc = loc;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        loc.CultureChanged += (_, _) => _ui.Post(_ => Rebuild(), null);
    }

    public bool IsDisabled => !IsEnabled;

    /// <summary>The first free game right now, shown large at the top.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHero))]
    private DealRow? _hero;

    public bool HasHero => Hero is not null;

    /// <summary>Store filters of the grids below (all, each store with offers, under 5 dollars).</summary>
    public ObservableCollection<DealFilter> Filters { get; } = [];

    /// <summary>The other free games right now.</summary>
    public ObservableCollection<DealRow> FreeNow { get; } = [];

    public ObservableCollection<DealRow> Upcoming { get; } = [];

    public bool HasFreeNow => FreeNow.Count > 0;

    public bool HasUpcoming => Upcoming.Count > 0;

    public ObservableCollection<PriceRow> BestDeals { get; } = [];

    public ObservableCollection<PriceGameRow> SearchResults { get; } = [];

    public bool HasBestDeals => BestDeals.Count > 0;

    /// <summary>Steam's top sellers, listed after the best deals.</summary>
    public ObservableCollection<PriceRow> TopSellers { get; } = [];

    public bool HasTopSellers => TopSellers.Count > 0;

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool HasSearchNote => (SearchOutcome)SearchState != SearchOutcome.None;

    public string? SearchNoteText => (SearchOutcome)SearchState switch
    {
        SearchOutcome.NoResults => _loc["Deals_Search_NoResults"],
        SearchOutcome.Failed => _loc["Deals_Search_Failed"],
        SearchOutcome.TooShort => string.Format(CultureInfo.CurrentCulture, _loc["Deals_Search_TooShort"], PriceDealsService.MinQueryLength, PriceDealsService.MaxQueryLength),
        _ => null,
    };

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
            _best = null;
            _top = null;
            _search = null;
            SearchText = string.Empty;
            SearchState = (int)SearchOutcome.None;
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
    private void SelectFilter(DealFilter? filter)
    {
        if (filter is null || filter.Key == _filter)
        {
            return;
        }

        _filter = filter.Key;
        RebuildPrices();
    }

    [RelayCommand]
    private Task OpenAsync(DealRow? row) => row is null ? Task.CompletedTask : OpenLinkAsync(row.Url);

    [RelayCommand]
    private Task OpenUrlAsync(string? url) => url is null ? Task.CompletedTask : OpenLinkAsync(url);

    [RelayCommand]
    private Task OpenCheapSharkAsync() => OpenLinkAsync(CheapSharkSite);

    // Asks for the price of the typed game; the text is cleaned first and nothing but it is sent.
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!IsEnabled || IsSearching)
        {
            return;
        }

        if (PriceDealsService.CleanQuery(SearchText) is not { } query)
        {
            _search = null;
            SearchState = (int)SearchOutcome.TooShort;
            RebuildPrices();
            return;
        }

        IsSearching = true;
        try
        {
            _search = await _prices.SearchAsync(query, CancellationToken.None);
            SearchState = (int)(_search.Failed ? SearchOutcome.Failed : _search.Games.Count == 0 ? SearchOutcome.NoResults : SearchOutcome.None);
            RebuildPrices();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
            SearchState = (int)SearchOutcome.Failed;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private Task OpenSteamcitoGuideAsync() => OpenLinkAsync(SteamcitoGuide);

    [RelayCommand]
    private Task OpenSteamcitoSiteAsync() => OpenLinkAsync(SteamcitoSite);

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
            _best = await _prices.BestAsync(refresh, CancellationToken.None);
            _top = await _trending.GetAsync(refresh, CancellationToken.None);
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
    private async Task OpenLinkAsync(string url)
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
        Hero = null;
        if (_snapshot is { } snapshot)
        {
            foreach (var game in snapshot.Games)
            {
                var row = ToRow(game);
                if (game.IsUpcoming)
                {
                    Upcoming.Add(row);
                }
                else if (Hero is null)
                {
                    Hero = row;
                }
                else
                {
                    FreeNow.Add(row);
                }
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
        RebuildPrices();
    }

    private void RebuildPrices()
    {
        var best = (_best?.Deals ?? []).Select(deal => new PriceRow(
            deal.Title, deal.Store, Money(deal.Sale), Detail(deal), deal.Url, DealUrls.SteamPosterUrl(deal.SteamAppId),
            deal.IsOnSale ? Money(deal.Normal) : null, deal.IsOnSale ? $"-{deal.SavingsPercent.ToString("0", CultureInfo.CurrentCulture)}%" : null, deal.Sale)).ToList();
        var top = (_top?.Games ?? []).Select(game => new PriceRow(
            game.Title, "Steam", Money(game.Sale),
            game.DiscountPercent > 0 ? string.Format(CultureInfo.CurrentCulture, _loc["Deals_PriceDetail"], game.DiscountPercent, Money(game.Normal)) : _loc["Deals_PriceRegular"],
            game.Url, DealUrls.SteamPosterUrl(game.AppId),
            game.DiscountPercent > 0 ? Money(game.Normal) : null, game.DiscountPercent > 0 ? $"-{game.DiscountPercent}%" : null, game.Sale)).ToList();

        RebuildFilters(best.Concat(top).Select(r => r.Store).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

        BestDeals.Clear();
        foreach (var row in best.Where(Matches))
        {
            BestDeals.Add(row);
        }

        TopSellers.Clear();
        foreach (var row in top.Where(Matches))
        {
            TopSellers.Add(row);
        }

        SearchResults.Clear();
        foreach (var game in _search?.Games ?? [])
        {
            var cheapest = game.Best;
            var bestText = string.Format(CultureInfo.CurrentCulture, _loc["Deals_Search_Best"], cheapest.Store, Money(cheapest.Sale)) + (cheapest.IsOnSale ? $" (-{cheapest.SavingsPercent.ToString("0", CultureInfo.CurrentCulture)}%)" : string.Empty);
            var stores = string.Join(" · ", game.Stores.Select(d => $"{d.Store} {Money(d.Sale)}"));
            SearchResults.Add(new PriceGameRow(game.Title, bestText, stores, cheapest.Url));
        }

        OnPropertyChanged(nameof(HasBestDeals));
        OnPropertyChanged(nameof(HasTopSellers));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(SearchNoteText));
    }

    private bool Matches(PriceRow row) => _filter switch
    {
        "all" => true,
        "under5" => row.Price < 5m,
        var store => row.Store.Equals(store, StringComparison.OrdinalIgnoreCase),
    };

    // The chips: All, each store that has offers, Under US$5. A chosen store that no longer has offers goes back to All.
    private void RebuildFilters(IEnumerable<string> stores)
    {
        var keys = new List<(string Key, Func<string> Label)> { ("all", () => _loc["Deals_Filter_All"]) };
        keys.AddRange(stores.Select(s => (s, (Func<string>)(() => s))));
        keys.Add(("under5", () => _loc["Deals_Filter_Under5"]));
        if (!keys.Any(k => k.Key == _filter))
        {
            _filter = "all";
        }

        Filters.Clear();
        foreach (var (key, label) in keys)
        {
            Filters.Add(new DealFilter(key, label) { IsSelected = key == _filter });
        }

        OnPropertyChanged(nameof(HasFilters));
    }

    public bool HasFilters => Filters.Count > 2;

    // Prices are US dollars (CheapShark): the number follows Lumen's language.
    private string Money(decimal value) =>
        value == 0 ? _loc["Deals_PriceFree"] : string.Format(CultureInfo.CurrentCulture, _loc["Deals_Price"], value);

    private string Detail(PriceDeal deal) => deal.IsOnSale
        ? string.Format(CultureInfo.CurrentCulture, _loc["Deals_PriceDetail"], deal.SavingsPercent, Money(deal.Normal))
        : _loc["Deals_PriceRegular"];

    private DealRow ToRow(FreeGame game)
    {
        var culture = CultureInfo.CurrentCulture;
        var status = game.IsUpcoming
            ? string.Format(culture, _loc["Deals_FreeFrom"], game.StartsAt!.Value.LocalDateTime.ToString("d", culture))
            : game.EndsAt is { } end
                ? string.Format(culture, _loc["Deals_FreeUntil"], end.LocalDateTime.ToString("d", culture))
                : _loc["Deals_NoEndDate"];
        var worth = game.Worth is null ? null : string.Format(culture, _loc["Deals_Worth"], game.Worth);
        return new DealRow(game.Title, game.Store, status, worth, game.Url, DealUrls.SteamPosterUrl(DealUrls.SteamAppIdOf(game.Url)));
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
