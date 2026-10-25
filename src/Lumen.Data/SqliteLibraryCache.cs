// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

/// <summary>The <c>entries</c> table: last known provider output per source.</summary>
public sealed class SqliteLibraryCache(ISqliteConnectionFactory connectionFactory) : ILibraryCache
{
    private static readonly JsonSerializerOptions _json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<LibraryEntry>> LoadAllAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT source AS Source, external_id AS ExternalId, kind AS Kind, title AS Title, state AS State,
                   install_dir AS InstallDir, launch_json AS LaunchJson, install_json AS InstallJson, extra_json AS ExtraJson
            FROM entries;
            """,
            cancellationToken: ct)).ConfigureAwait(false);

        var entries = new List<LibraryEntry>();
        foreach (var row in rows)
        {
            // Rows written by a newer Lumen (unknown enum values) are skipped rather than crashing.
            if (ToEntry(row) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public async Task ReplaceSourceAsync(string source, IReadOnlyList<LibraryEntry> entries, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Any(e => e.Ref.Source != source))
        {
            throw new ArgumentException($"All entries must belong to source '{source}'.", nameof(entries));
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM entries WHERE source = @source;", new { source }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO entries (source, external_id, kind, title, state, install_dir, launch_json, install_json, extra_json, updated_at)
            VALUES (@Source, @ExternalId, @Kind, @Title, @State, @InstallDir, @LaunchJson, @InstallJson, @ExtraJson, @UpdatedAt);
            """,
            entries.Select(e => new
            {
                e.Ref.Source,
                e.Ref.ExternalId,
                Kind = e.Kind.ToString(),
                e.Title,
                State = e.State.ToString(),
                InstallDir = e.InstallDirectory,
                LaunchJson = e.Launch is null ? null : JsonSerializer.Serialize(e.Launch, _json),
                InstallJson = e.Install is null ? null : JsonSerializer.Serialize(e.Install, _json),
                ExtraJson = JsonSerializer.Serialize(e.Extra, _json),
                UpdatedAt = now,
            }),
            transaction,
            cancellationToken: ct)).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static LibraryEntry? ToEntry(Row row)
    {
        if (!Enum.TryParse<ItemKind>(row.Kind, out var kind) || !Enum.TryParse<InstallState>(row.State, out var state))
        {
            return null;
        }

        try
        {
            return new LibraryEntry(
                new ItemRef(row.Source, row.ExternalId),
                kind,
                row.Title,
                state,
                row.InstallDir,
                row.LaunchJson is null ? null : JsonSerializer.Deserialize<LaunchSpec>(row.LaunchJson, _json),
                row.InstallJson is null ? null : JsonSerializer.Deserialize<LaunchSpec>(row.InstallJson, _json),
                JsonSerializer.Deserialize<Dictionary<string, string>>(row.ExtraJson, _json) ?? new Dictionary<string, string>());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class Row
    {
        public string Source { get; init; } = string.Empty;

        public string ExternalId { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string State { get; init; } = string.Empty;

        public string? InstallDir { get; init; }

        public string? LaunchJson { get; init; }

        public string? InstallJson { get; init; }

        public string ExtraJson { get; init; } = "{}";
    }
}
