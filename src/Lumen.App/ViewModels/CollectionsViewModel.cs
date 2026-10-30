// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>One collection on the Collections page.</summary>
public sealed partial class CollectionCard : ObservableObject
{
    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    public CollectionCard(Collection collection, string name, string countText)
    {
        Collection = collection;
        Name = name;
        CountText = countText;
    }

    public Collection Collection { get; }

    public string Id => Collection.Id;

    public string Name { get; }

    public string CountText { get; }

    public bool IsFavorites => Collection.IsFavorites;

    /// <summary>Which soft tint the card uses (0-2, cycling down the page; Favorites is always amber).</summary>
    public int Tint { get; init; }

    /// <summary>Favorites can't be renamed or deleted.</summary>
    public bool CanEdit => !Collection.IsFavorites;

    public override string ToString() => Name;
}

/// <summary>
/// Collections page (PLAN-v1 §4.1): Favorites and the user's own collections of games and apps. Opening one shows it
/// in the library (the library's collection filter); items are added from a game's detail view or its tile menu.
/// </summary>
public sealed partial class CollectionsViewModel : ObservableObject
{
    private readonly CollectionService _collections;
    private readonly LibraryService _library;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly ILogger<CollectionsViewModel> _logger;
    private readonly SynchronizationContext _ui;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public CollectionsViewModel(CollectionService collections, LibraryService library, IDialogService dialogs, ILocalizer localizer, ILogger<CollectionsViewModel> logger)
    {
        _collections = collections;
        _library = library;
        _dialogs = dialogs;
        _localizer = localizer;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _collections.Changed += (_, _) => Refresh();
        _library.Changed += (_, _) => _ui.Post(_ => Refresh(), null);
        _localizer.CultureChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Raised when the user opens a collection (the shell shows it in the library).</summary>
    public event EventHandler<string>? OpenRequested;

    public IReadOnlyList<CollectionCard> Cards { get; private set; } = [];

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>"Favorites" is stored in English; the page shows it translated.</summary>
    public static string DisplayName(Collection collection, ILocalizer localizer) =>
        collection.IsFavorites ? localizer["Collections_Favorites"] : collection.Name;

    [RelayCommand]
    private void Open(CollectionCard? card)
    {
        if (card is not null)
        {
            OpenRequested?.Invoke(this, card.Id);
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        await RunAsync(async () =>
        {
            if (await _collections.CreateAsync(NewName, CancellationToken.None) is not null)
            {
                NewName = string.Empty;
            }
        });
    }

    [RelayCommand]
    private void StartRename(CollectionCard? card)
    {
        if (card is { CanEdit: true })
        {
            card.RenameText = card.Name;
            card.IsRenaming = true;
        }
    }

    [RelayCommand]
    private void CancelRename(CollectionCard? card)
    {
        if (card is not null)
        {
            card.IsRenaming = false;
        }
    }

    [RelayCommand]
    private Task SaveRenameAsync(CollectionCard? card) =>
        card is null ? Task.CompletedTask : RunAsync(() => _collections.RenameAsync(card.Id, card.RenameText, CancellationToken.None));

    [RelayCommand]
    private Task DeleteAsync(CollectionCard? card)
    {
        if (card is not { CanEdit: true })
        {
            return Task.CompletedTask;
        }

        // Deleting a collection never touches the games or apps in it.
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["Collections_DeleteConfirm"], card.Name);
        return _dialogs.Confirm(_localizer["Collections_DeleteTitle"], message)
            ? RunAsync(() => _collections.DeleteAsync(card.Id, CancellationToken.None))
            : Task.CompletedTask;
    }

    private async Task RunAsync(Func<Task> action)
    {
        Notice = null;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            Notice = _localizer["Collections_SaveFailed"];
        }
    }

    private void Refresh()
    {
        var items = _library.Items;
        Cards = _collections.All
            .Select((c, index) =>
            {
                var count = items.Count(c.Contains);
                var countText = count == 1 ? _localizer["Library_CountOne"] : string.Format(CultureInfo.CurrentCulture, _localizer["Library_Count"], count);
                return new CollectionCard(c, DisplayName(c, _localizer), countText) { Tint = index % 3 };
            })
            .ToList();
        OnPropertyChanged(nameof(Cards));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save a collection change")]
    private partial void LogFailed(Exception exception);
}
