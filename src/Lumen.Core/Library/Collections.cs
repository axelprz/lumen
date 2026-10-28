// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Library;

/// <summary>
/// A user collection of games and/or apps (<c>docs/schemas/collection.schema.json</c>). Items are entry refs: a merged
/// game is stored with every store's ref, so it stays in the collection whichever store Play uses.
/// </summary>
public sealed record Collection(string Id, string Name, IReadOnlyList<ItemRef> Items)
{
    /// <summary>The reserved collection that holds favorites. Always exists; can't be renamed or deleted.</summary>
    public const string FavoritesId = "favorites";

    /// <summary>Longest name accepted (matches the schema).</summary>
    public const int MaxNameLength = 80;

    public bool IsFavorites => Id == FavoritesId;

    /// <summary>True when any of the item's entries is in this collection.</summary>
    public bool Contains(LibraryItem item) => item.Entries.Any(e => Items.Contains(e.Ref));

    /// <summary>Trims the user's input; null when empty. Over-long names are cut.</summary>
    public static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxNameLength ? trimmed : trimmed[..MaxNameLength].TrimEnd();
    }

    /// <summary>
    /// A stable id from the name, as the schema requires (<c>^[a-z0-9][a-z0-9-]{0,63}$</c>): "Para jugar con amigos" →
    /// "para-jugar-con-amigos"; "-2", "-3"… when taken; "collection" when the name has no usable letters.
    /// </summary>
    public static string NewId(string name, IReadOnlySet<string> taken)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        slug = slug.Length == 0 ? "collection" : slug[..Math.Min(slug.Length, 56)].TrimEnd('-');
        if (slug == FavoritesId)
        {
            slug = "favorites-collection";
        }

        var id = slug;
        for (var n = 2; taken.Contains(id); n++)
        {
            id = $"{slug}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        return id;
    }
}

/// <summary>Stores collections (SQLite <c>collections</c> / <c>collection_items</c>; synced as JSON files in M8).</summary>
public interface ICollectionStore
{
    /// <summary>Every collection, Favorites first, then by name. Items in the order they were added.</summary>
    Task<IReadOnlyList<Collection>> LoadAsync(CancellationToken ct);

    Task CreateAsync(string id, string name, CancellationToken ct);

    Task RenameAsync(string id, string name, CancellationToken ct);

    Task DeleteAsync(string id, CancellationToken ct);

    /// <summary>Adds refs at the end; refs already there stay where they are.</summary>
    Task AddAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct);

    Task RemoveAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct);
}

/// <summary>
/// The user's collections and favorites, loaded once and kept current; every change goes through here so all pages
/// (library, detail, Home, Collections) see it at once via <see cref="Changed"/>.
/// </summary>
public sealed partial class CollectionService(ICollectionStore store, ILogger<CollectionService> logger)
{
    private IReadOnlyList<Collection> _all = [new Collection(Collection.FavoritesId, "Favorites", [])];

    /// <summary>Raised (on the caller's thread) after any change.</summary>
    public event EventHandler? Changed;

    /// <summary>Favorites first, then the rest by name.</summary>
    public IReadOnlyList<Collection> All => Volatile.Read(ref _all);

    public Collection Favorites => All.First(c => c.IsFavorites);

    public Collection? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    public bool IsFavorite(LibraryItem item) => Favorites.Contains(item);

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            Volatile.Write(ref _all, await store.LoadAsync(ct).ConfigureAwait(false));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex); // collections are user data: keep showing what we had
        }
    }

    /// <returns>The new collection's id, or null when the name is empty.</returns>
    public async Task<string?> CreateAsync(string name, CancellationToken ct)
    {
        if (Collection.CleanName(name) is not { } clean)
        {
            return null;
        }

        var id = Collection.NewId(clean, All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal));
        await store.CreateAsync(id, clean, ct).ConfigureAwait(false);
        await LoadAsync(ct).ConfigureAwait(false);
        return id;
    }

    public async Task RenameAsync(string id, string name, CancellationToken ct)
    {
        if (id == Collection.FavoritesId || Collection.CleanName(name) is not { } clean)
        {
            return;
        }

        await store.RenameAsync(id, clean, ct).ConfigureAwait(false);
        await LoadAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (id == Collection.FavoritesId)
        {
            return;
        }

        await store.DeleteAsync(id, ct).ConfigureAwait(false);
        await LoadAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Adds the item (every store's entry) to the collection, or takes it out.</summary>
    public async Task SetAsync(string id, LibraryItem item, bool inCollection, CancellationToken ct)
    {
        var refs = item.Entries.Select(e => e.Ref).ToList();
        if (inCollection)
        {
            await store.AddAsync(id, refs, ct).ConfigureAwait(false);
        }
        else
        {
            await store.RemoveAsync(id, refs, ct).ConfigureAwait(false);
        }

        await LoadAsync(ct).ConfigureAwait(false);
    }

    public Task SetFavoriteAsync(LibraryItem item, bool favorite, CancellationToken ct) =>
        SetAsync(Collection.FavoritesId, item, favorite, ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load collections")]
    private partial void LogFailed(Exception exception);
}
