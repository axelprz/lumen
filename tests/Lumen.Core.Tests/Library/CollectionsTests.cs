// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Microsoft.Extensions.Logging.Abstractions;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class CollectionsTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    [Theory]
    [InlineData("Para jugar con amigos", "para-jugar-con-amigos")]
    [InlineData("  Acción & Aventura!! ", "accion-aventura")]
    [InlineData("🎮🎮", "collection")]
    [InlineData("Favorites", "favorites-collection")]
    public void NewId_FollowsTheSchemaPattern(string name, string expected)
    {
        var id = Collection.NewId(name, None);

        id.ShouldBe(expected);
        id.ShouldMatch("^[a-z0-9][a-z0-9-]{0,63}$");
    }

    [Fact]
    public void NewId_IsUnique_AndNeverTooLong()
    {
        Collection.NewId("Work", new HashSet<string> { "work", "work-2" }).ShouldBe("work-3");
        Collection.NewId(new string('a', 200), None).Length.ShouldBeLessThanOrEqualTo(64);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Work ", "Work")]
    public void CleanName_TrimsAndRejectsEmpty(string? input, string? expected) => Collection.CleanName(input).ShouldBe(expected);

    [Fact]
    public void Contains_MatchesAnyStoreOfAMergedGame()
    {
        var steam = Game("steam", "1", "Sample");
        var epic = Game("epic", "x", "Sample");
        var collection = new Collection("c", "C", [epic.Ref]);

        collection.Contains(new LibraryItem([steam, epic], steam)).ShouldBeTrue();
        collection.Contains(new LibraryItem([steam], steam)).ShouldBeFalse();
    }

    [Fact]
    public async Task Service_AddsEveryStore_CreatesUniqueIds_AndProtectsFavorites()
    {
        var store = new InMemoryCollections();
        var service = new CollectionService(store, NullLogger<CollectionService>.Instance);
        var changed = 0;
        service.Changed += (_, _) => changed++;
        await service.LoadAsync(CancellationToken.None);
        var steam = Game("steam", "1", "Sample");
        var epic = Game("epic", "x", "Sample");
        var merged = new LibraryItem([steam, epic], steam);

        await service.SetFavoriteAsync(merged, true, CancellationToken.None);
        var id = await service.CreateAsync("Work", CancellationToken.None);
        var again = await service.CreateAsync("work", CancellationToken.None);
        await service.DeleteAsync(Collection.FavoritesId, CancellationToken.None);
        await service.RenameAsync(Collection.FavoritesId, "Mine", CancellationToken.None);

        service.Favorites.Items.ShouldBe([steam.Ref, epic.Ref]);
        service.IsFavorite(new LibraryItem([epic], epic)).ShouldBeTrue();
        (id, again).ShouldBe(("work", "work-2"));
        (await service.CreateAsync("   ", CancellationToken.None)).ShouldBeNull();
        service.All.Select(c => c.Id).ShouldBe(["favorites", "work", "work-2"]);
        service.Favorites.Name.ShouldBe("Favorites");
        changed.ShouldBeGreaterThan(0);

        await service.SetFavoriteAsync(merged, false, CancellationToken.None);
        service.Favorites.Items.ShouldBeEmpty();
    }

    private sealed class InMemoryCollections : ICollectionStore
    {
        private readonly List<(string Id, string Name, List<ItemRef> Items)> _all = [(Collection.FavoritesId, "Favorites", [])];

        public Task<IReadOnlyList<Collection>> LoadAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Collection>>(_all
                .OrderBy(c => c.Id == Collection.FavoritesId ? 0 : 1).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new Collection(c.Id, c.Name, c.Items.ToList())).ToList());

        public Task CreateAsync(string id, string name, CancellationToken ct)
        {
            _all.Add((id, name, []));
            return Task.CompletedTask;
        }

        public Task RenameAsync(string id, string name, CancellationToken ct)
        {
            var i = _all.FindIndex(c => c.Id == id);
            _all[i] = _all[i] with { Name = name };
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken ct)
        {
            _all.RemoveAll(c => c.Id == id);
            return Task.CompletedTask;
        }

        public Task AddAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct)
        {
            var items = _all.Single(c => c.Id == id).Items;
            items.AddRange(refs.Where(r => !items.Contains(r)));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct)
        {
            _all.Single(c => c.Id == id).Items.RemoveAll(refs.Contains);
            return Task.CompletedTask;
        }
    }
}
