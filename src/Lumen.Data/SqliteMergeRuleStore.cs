// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

public sealed class SqliteMergeRuleStore(ISqliteConnectionFactory connectionFactory) : IMergeRuleStore
{
    public async Task<MergeRules> LoadAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<(string Ref, long Separated, long Preferred)>(new CommandDefinition(
            "SELECT ref, separated, preferred FROM merge_rules;", cancellationToken: ct)).ConfigureAwait(false);

        var separated = new HashSet<ItemRef>();
        var preferred = new HashSet<ItemRef>();
        foreach (var row in rows)
        {
            if (!ItemRef.TryParse(row.Ref, out var itemRef))
            {
                continue;
            }

            if (row.Separated == 1)
            {
                separated.Add(itemRef);
            }

            if (row.Preferred == 1)
            {
                preferred.Add(itemRef);
            }
        }

        return new MergeRules(separated, preferred);
    }

    public async Task SeparateAsync(IEnumerable<ItemRef> refs, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var itemRef in refs)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO merge_rules (ref, separated, preferred, updated_at) VALUES (@Ref, 1, 0, @Now)
                ON CONFLICT(ref) DO UPDATE SET separated = 1, preferred = 0, updated_at = excluded.updated_at;
                """,
                new { Ref = itemRef.ToString(), Now = Now() },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task SetPreferredAsync(ItemRef preferred, IEnumerable<ItemRef> others, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var other in others.Where(o => o != preferred))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE merge_rules SET preferred = 0, updated_at = @Now WHERE ref = @Ref;",
                new { Ref = other.ToString(), Now = Now() },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO merge_rules (ref, separated, preferred, updated_at) VALUES (@Ref, 0, 1, @Now)
            ON CONFLICT(ref) DO UPDATE SET preferred = 1, updated_at = excluded.updated_at;
            """,
            new { Ref = preferred.ToString(), Now = Now() },
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM merge_rules;", cancellationToken: ct)).ConfigureAwait(false);
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
