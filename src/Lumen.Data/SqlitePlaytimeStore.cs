// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

public sealed class SqlitePlaytimeStore(ISqliteConnectionFactory connectionFactory) : IPlaytimeStore
{
    public async Task<long> StartAsync(ItemRef itemRef, DateTimeOffset start, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO play_sessions (ref, started_at, ended_at, open) VALUES (@Ref, @At, @At, 1) RETURNING id;",
            new { Ref = itemRef.ToString(), At = Format(start) },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task ExtendAsync(long sessionId, DateTimeOffset lastSeen, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE play_sessions SET ended_at = @At WHERE id = @Id AND open = 1;",
            new { Id = sessionId, At = Format(lastSeen) },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task FinishAsync(long sessionId, DateTimeOffset end, TimeSpan minimum, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE play_sessions SET ended_at = @At, open = 0 WHERE id = @Id;",
            new { Id = sessionId, At = Format(end) },
            cancellationToken: ct)).ConfigureAwait(false);
        await DropShortAsync(connection, minimum, ct).ConfigureAwait(false);
    }

    public async Task CloseLeftoversAsync(TimeSpan minimum, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE play_sessions SET open = 0 WHERE open = 1;", cancellationToken: ct)).ConfigureAwait(false);
        await DropShortAsync(connection, minimum, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<ItemRef, PlaytimeTotal>> LoadTotalsAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<(string Ref, string Started, string Ended)>(new CommandDefinition(
            "SELECT ref, started_at, ended_at FROM play_sessions;", cancellationToken: ct)).ConfigureAwait(false);

        var totals = new Dictionary<ItemRef, (double Minutes, DateTimeOffset Last)>();
        foreach (var row in rows)
        {
            if (!ItemRef.TryParse(row.Ref, out var itemRef) || Parse(row.Started) is not { } start || Parse(row.Ended) is not { } end || end < start)
            {
                continue;
            }

            var minutes = (end - start).TotalMinutes;
            totals[itemRef] = totals.TryGetValue(itemRef, out var sum)
                ? (sum.Minutes + minutes, end > sum.Last ? end : sum.Last)
                : (minutes, end);
        }

        return totals.ToDictionary(kv => kv.Key, kv => new PlaytimeTotal((long)Math.Round(kv.Value.Minutes), kv.Value.Last));
    }

    // Too short to be play (a crash at start, a launcher that closed the game): dropped once closed.
    private static async Task DropShortAsync(System.Data.Common.DbConnection connection, TimeSpan minimum, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<(long Id, string Started, string Ended)>(new CommandDefinition(
            "SELECT id, started_at, ended_at FROM play_sessions WHERE open = 0;", cancellationToken: ct)).ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (Parse(row.Started) is { } start && Parse(row.Ended) is { } end && end - start < minimum)
            {
                await connection.ExecuteAsync(new CommandDefinition("DELETE FROM play_sessions WHERE id = @Id;", new { row.Id }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Parse(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
}
