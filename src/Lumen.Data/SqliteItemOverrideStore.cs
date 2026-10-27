// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

public sealed class SqliteItemOverrideStore(ISqliteConnectionFactory connectionFactory) : IItemOverrideStore
{
    public async Task<IReadOnlyDictionary<ItemRef, ItemOverride>> LoadAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<(string Ref, string? Title, long Hidden)>(new CommandDefinition(
            "SELECT ref, title, hidden FROM item_overrides;", cancellationToken: ct)).ConfigureAwait(false);

        var result = new Dictionary<ItemRef, ItemOverride>();
        foreach (var row in rows)
        {
            if (ItemRef.TryParse(row.Ref, out var itemRef))
            {
                result[itemRef] = new ItemOverride(row.Title, row.Hidden == 1);
            }
        }

        return result;
    }

    public Task SetTitleAsync(IEnumerable<ItemRef> refs, string? title, CancellationToken ct) =>
        UpsertAsync(
            refs,
            "INSERT INTO item_overrides (ref, title, hidden, updated_at) VALUES (@Ref, @Value, 0, @Now) " +
            "ON CONFLICT(ref) DO UPDATE SET title = excluded.title, updated_at = excluded.updated_at;",
            ItemOverride.CleanTitle(title),
            ct);

    public Task SetHiddenAsync(IEnumerable<ItemRef> refs, bool hidden, CancellationToken ct) =>
        UpsertAsync(
            refs,
            "INSERT INTO item_overrides (ref, title, hidden, updated_at) VALUES (@Ref, NULL, @Value, @Now) " +
            "ON CONFLICT(ref) DO UPDATE SET hidden = excluded.hidden, updated_at = excluded.updated_at;",
            hidden ? 1 : 0,
            ct);

    private async Task UpsertAsync(IEnumerable<ItemRef> refs, string sql, object? value, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var itemRef in refs.Distinct())
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql, new { Ref = itemRef.ToString(), Value = value, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        }

        // A row with no custom title that isn't hidden says nothing: drop it.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM item_overrides WHERE title IS NULL AND hidden = 0;", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
