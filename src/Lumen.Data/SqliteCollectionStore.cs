// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

public sealed class SqliteCollectionStore(ISqliteConnectionFactory connectionFactory) : ICollectionStore
{
    public async Task<IReadOnlyList<Collection>> LoadAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var collections = (await connection.QueryAsync<(string Id, string Name)>(new CommandDefinition(
            "SELECT id, name FROM collections;", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        var items = (await connection.QueryAsync<(string CollectionId, string Ref)>(new CommandDefinition(
            "SELECT collection_id, ref FROM collection_items ORDER BY position, added_at;", cancellationToken: ct)).ConfigureAwait(false))
            .GroupBy(i => i.CollectionId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ItemRef>)g.Select(i => ItemRef.TryParse(i.Ref, out var r) ? r : (ItemRef?)null).OfType<ItemRef>().ToList(),
                StringComparer.Ordinal);

        if (!collections.Any(c => c.Id == Collection.FavoritesId))
        {
            collections.Add((Collection.FavoritesId, "Favorites"));
        }

        return collections
            .OrderBy(c => c.Id == Collection.FavoritesId ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new Collection(c.Id, c.Name, items.GetValueOrDefault(c.Id) ?? []))
            .ToList();
    }

    public async Task CreateAsync(string id, string name, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO collections (id, name, created_at, updated_at) VALUES (@Id, @Name, @Now, @Now);",
            new { Id = id, Name = name, Now = Now() },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RenameAsync(string id, string name, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE collections SET name = @Name, updated_at = @Now WHERE id = @Id;",
            new { Id = id, Name = name, Now = Now() },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM collections WHERE id = @Id AND id <> 'favorites';", new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task AddAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = Now();
        foreach (var itemRef in refs.Distinct())
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT OR IGNORE INTO collection_items (collection_id, ref, position, added_at)
                VALUES (@Id, @Ref, (SELECT COALESCE(MAX(position), 0) + 1 FROM collection_items WHERE collection_id = @Id), @Now);
                """,
                new { Id = id, Ref = itemRef.ToString(), Now = now },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await TouchAsync(connection, transaction, id, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string id, IEnumerable<ItemRef> refs, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var itemRef in refs.Distinct())
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM collection_items WHERE collection_id = @Id AND ref = @Ref;",
                new { Id = id, Ref = itemRef.ToString() },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await TouchAsync(connection, transaction, id, Now(), ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static Task TouchAsync(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, string id, string now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "UPDATE collections SET updated_at = @Now WHERE id = @Id;", new { Id = id, Now = now }, transaction, cancellationToken: ct));

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
