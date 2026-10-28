// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

public sealed class SqliteAppChoiceStore(ISqliteConnectionFactory connectionFactory) : IAppChoiceStore
{
    public async Task<IReadOnlyDictionary<ItemRef, AppChoice>> LoadAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<(string Ref, string Choice)>(new CommandDefinition(
            "SELECT ref, choice FROM app_choices;", cancellationToken: ct)).ConfigureAwait(false);

        var result = new Dictionary<ItemRef, AppChoice>();
        foreach (var row in rows)
        {
            if (ItemRef.TryParse(row.Ref, out var itemRef) && Parse(row.Choice) is { } choice)
            {
                result[itemRef] = choice;
            }
        }

        return result;
    }

    public async Task SetAsync(IEnumerable<ItemRef> refs, AppChoice? choice, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var itemRef in refs.Distinct())
        {
            var sql = choice is null
                ? "DELETE FROM app_choices WHERE ref = @Ref;"
                : """
                  INSERT INTO app_choices (ref, choice, updated_at) VALUES (@Ref, @Choice, @Now)
                  ON CONFLICT(ref) DO UPDATE SET choice = excluded.choice, updated_at = excluded.updated_at;
                  """;
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { Ref = itemRef.ToString(), Choice = choice is null ? null : Format(choice.Value), Now = now },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static string Format(AppChoice choice) => choice == AppChoice.Added ? "added" : "ignored";

    private static AppChoice? Parse(string value) => value switch
    {
        "added" => AppChoice.Added,
        "ignored" => AppChoice.Ignored,
        _ => null,
    };
}
