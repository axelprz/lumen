// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Core.Shell;

namespace Lumen.Core.Sync;

/// <summary>What applying a config to this PC changed.</summary>
/// <param name="Changed">Something was written here.</param>
/// <param name="ManualItemsReceived">Titles of manual items that are new or different on this PC (shown to the user).</param>
public sealed record ConfigApplyResult(bool Changed, IReadOnlyList<string> ManualItemsReceived);

/// <summary>
/// This PC's side of the sync (PLAN-v1 §4.5): reads the portable settings, manual items, renames, ignored apps and
/// collections from the local stores, and applies what arrives from the sync folder to them. Machine-only things
/// (secrets, playtime, caches, routine approvals, paths of folders) are never read here.
/// </summary>
public sealed class LocalPortableState(
    ILocalSettings settings,
    IManualItemStore manualItems,
    IItemOverrideStore overrides,
    IAppChoiceStore appChoices,
    ICollectionStore collections)
{
    private static readonly string[] Languages = ["system", "en", "es"];

    /// <summary>
    /// The portable config as it would be written and read back, so what is compared is exactly what a reader of the
    /// file would see (an entry the rules refuse is left out).
    /// </summary>
    public async Task<PortableConfig> ReadConfigAsync(CancellationToken ct)
    {
        var language = await settings.GetAsync(LocalSettingKeys.Language, ct).ConfigureAwait(false);
        var closeToTray = await settings.GetAsync(LocalSettingKeys.CloseToTray, ct).ConfigureAwait(false);
        var hotkey = await settings.GetAsync(LocalSettingKeys.Hotkey, ct).ConfigureAwait(false);
        var choices = await appChoices.LoadAsync(ct).ConfigureAwait(false);

        var raw = new PortableConfig
        {
            Language = language is not null && Languages.Contains(language) ? language : null,
            MinimizeToTray = closeToTray is null ? null : closeToTray != "0",
            Hotkey = Hotkey.TryParse(hotkey, out _) ? hotkey!.Trim() : null,
            ManualItems = await manualItems.GetAllAsync(ct).ConfigureAwait(false),
            Overrides = (await overrides.LoadAsync(ct).ConfigureAwait(false))
                .Where(kv => kv.Value.Title is not null || kv.Value.Hidden)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            IgnoredApps = choices.Where(kv => kv.Value == AppChoice.Ignored && AppCuration.IsCandidateSource(kv.Key.Source))
                .Select(kv => kv.Key).Order(Comparer<ItemRef>.Create((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()))).ToList(),
        };
        return PortableConfigJson.Parse(PortableConfigJson.Write(raw)).Config ?? raw;
    }

    /// <summary>Every collection; Favorites with nothing in it counts as absent (there is nothing to share).</summary>
    public async Task<IReadOnlyList<Collection>> ReadCollectionsAsync(CancellationToken ct) =>
        (await collections.LoadAsync(ct).ConfigureAwait(false)).Where(c => !c.IsFavorites || c.Items.Count > 0).ToList();

    /// <summary>
    /// Makes this PC match <paramref name="incoming"/>: lists (manual items, renames, ignored apps) are replaced,
    /// values it doesn't set are left alone. A detected app the user added here is never turned into an ignored one.
    /// </summary>
    public async Task<ConfigApplyResult> ApplyConfigAsync(PortableConfig incoming, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var changed = false;

        changed |= await SetIfDifferentAsync(LocalSettingKeys.Language, incoming.Language, ct).ConfigureAwait(false);
        changed |= await SetIfDifferentAsync(LocalSettingKeys.CloseToTray, incoming.MinimizeToTray is { } m ? (m ? "1" : "0") : null, ct).ConfigureAwait(false);
        changed |= await SetIfDifferentAsync(LocalSettingKeys.Hotkey, incoming.Hotkey, ct).ConfigureAwait(false);

        var received = new List<string>();
        var current = (await manualItems.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(i => i.Id);
        foreach (var item in incoming.ManualItems)
        {
            if (!current.TryGetValue(item.Id, out var old) || old != item)
            {
                await manualItems.SaveAsync(item, ct).ConfigureAwait(false);
                received.Add(item.Title);
                changed = true;
            }
        }

        var incomingIds = incoming.ManualItems.Select(i => i.Id).ToHashSet();
        foreach (var gone in current.Keys.Where(id => !incomingIds.Contains(id)))
        {
            await manualItems.RemoveAsync(gone, ct).ConfigureAwait(false);
            changed = true;
        }

        changed |= await ApplyOverridesAsync(incoming.Overrides, ct).ConfigureAwait(false);
        changed |= await ApplyIgnoredAsync(incoming.IgnoredApps, ct).ConfigureAwait(false);
        return new ConfigApplyResult(changed, received);
    }

    /// <summary>Creates or updates a collection so it has exactly these items in this order. Favorites keep their name.</summary>
    public async Task UpsertCollectionAsync(Collection incoming, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var current = (await collections.LoadAsync(ct).ConfigureAwait(false)).FirstOrDefault(c => c.Id == incoming.Id);
        if (current is null)
        {
            await collections.CreateAsync(incoming.Id, incoming.Name, ct).ConfigureAwait(false);
        }
        else if (!incoming.IsFavorites && current.Name != incoming.Name)
        {
            await collections.RenameAsync(incoming.Id, incoming.Name, ct).ConfigureAwait(false);
        }

        var have = current?.Items ?? [];
        if (have.SequenceEqual(incoming.Items))
        {
            return;
        }

        // The store appends; a different order (or a removal) is rebuilt from scratch.
        if (have.Count > 0 && !incoming.Items.Take(have.Count).SequenceEqual(have))
        {
            await collections.RemoveAsync(incoming.Id, have, ct).ConfigureAwait(false);
            have = [];
        }

        await collections.AddAsync(incoming.Id, incoming.Items.Skip(have.Count), ct).ConfigureAwait(false);
    }

    /// <summary>Deletes a collection; Favorites can't be deleted, so they are emptied.</summary>
    public async Task DeleteCollectionAsync(string id, CancellationToken ct)
    {
        if (id != Collection.FavoritesId)
        {
            await collections.DeleteAsync(id, ct).ConfigureAwait(false);
            return;
        }

        var favorites = (await collections.LoadAsync(ct).ConfigureAwait(false)).FirstOrDefault(c => c.IsFavorites);
        if (favorites is { Items.Count: > 0 })
        {
            await collections.RemoveAsync(id, favorites.Items, ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> SetIfDifferentAsync(string key, string? value, CancellationToken ct)
    {
        if (value is null || await settings.GetAsync(key, ct).ConfigureAwait(false) == value)
        {
            return false;
        }

        await settings.SetAsync(key, value, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyOverridesAsync(IReadOnlyDictionary<ItemRef, ItemOverride> incoming, CancellationToken ct)
    {
        var current = await overrides.LoadAsync(ct).ConfigureAwait(false);
        var changed = false;
        foreach (var (itemRef, wanted) in incoming)
        {
            var have = current.GetValueOrDefault(itemRef);
            if (have?.Title != wanted.Title)
            {
                await overrides.SetTitleAsync([itemRef], wanted.Title, ct).ConfigureAwait(false);
                changed = true;
            }

            if ((have?.Hidden ?? false) != wanted.Hidden)
            {
                await overrides.SetHiddenAsync([itemRef], wanted.Hidden, ct).ConfigureAwait(false);
                changed = true;
            }
        }

        foreach (var (itemRef, have) in current.Where(kv => !incoming.ContainsKey(kv.Key)))
        {
            if (have.Title is not null)
            {
                await overrides.SetTitleAsync([itemRef], null, ct).ConfigureAwait(false);
                changed = true;
            }

            if (have.Hidden)
            {
                await overrides.SetHiddenAsync([itemRef], false, ct).ConfigureAwait(false);
                changed = true;
            }
        }

        return changed;
    }

    private async Task<bool> ApplyIgnoredAsync(IReadOnlyList<ItemRef> incoming, CancellationToken ct)
    {
        var choices = await appChoices.LoadAsync(ct).ConfigureAwait(false);
        var wanted = incoming.ToHashSet();
        var toIgnore = wanted.Where(r => !choices.ContainsKey(r)).ToList(); // an app already added or ignored here is left as it is
        var toForget = choices.Where(kv => kv.Value == AppChoice.Ignored && !wanted.Contains(kv.Key)).Select(kv => kv.Key).ToList();

        if (toIgnore.Count > 0)
        {
            await appChoices.SetAsync(toIgnore, AppChoice.Ignored, ct).ConfigureAwait(false);
        }

        if (toForget.Count > 0)
        {
            await appChoices.SetAsync(toForget, null, ct).ConfigureAwait(false);
        }

        return toIgnore.Count > 0 || toForget.Count > 0;
    }
}
