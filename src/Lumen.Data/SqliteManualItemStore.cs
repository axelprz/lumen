// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

public sealed class SqliteManualItemStore(ISqliteConnectionFactory connectionFactory) : IManualItemStore
{
    public async Task<IReadOnlyList<ManualItem>> GetAllAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            "SELECT id AS Id, kind AS Kind, title AS Title, path AS Path, url AS Url, args AS Args FROM manual_items ORDER BY created_at;",
            cancellationToken: ct)).ConfigureAwait(false);

        return rows
            .Where(r => Guid.TryParse(r.Id, out _) && Enum.TryParse<ItemKind>(r.Kind, out _))
            .Select(r => new ManualItem(Guid.Parse(r.Id), Enum.Parse<ItemKind>(r.Kind), r.Title, r.Path, r.Url, r.Args))
            .ToList();
    }

    public async Task SaveAsync(ManualItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        if ((item.Path is null) == (item.Url is null))
        {
            throw new ArgumentException("Exactly one of Path or Url must be set.", nameof(item));
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO manual_items (id, kind, title, path, url, args, created_at, updated_at)
            VALUES (@Id, @Kind, @Title, @Path, @Url, @Args, @Now, @Now)
            ON CONFLICT(id) DO UPDATE SET kind = excluded.kind, title = excluded.title, path = excluded.path,
                url = excluded.url, args = excluded.args, updated_at = excluded.updated_at;
            """,
            new
            {
                Id = item.Id.ToString("D"),
                Kind = item.Kind.ToString(),
                item.Title,
                item.Path,
                item.Url,
                Args = item.Arguments,
                Now = now,
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM manual_items WHERE id = @id;", new { id = id.ToString("D") }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class Row
    {
        public string Id { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string? Path { get; init; }

        public string? Url { get; init; }

        public string? Args { get; init; }
    }
}
