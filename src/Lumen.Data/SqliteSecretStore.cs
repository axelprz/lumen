// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Lumen.Core.Settings;

namespace Lumen.Data;

/// <summary>Secrets table: only ciphertext is stored. Plaintext never leaves this class except to its caller.</summary>
public sealed class SqliteSecretStore(ISqliteConnectionFactory connectionFactory, ISecretProtector protector) : ISecretStore
{
    public async Task<string?> GetAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var cipher = await connection.QuerySingleOrDefaultAsync<byte[]?>(new CommandDefinition(
            "SELECT value FROM secrets WHERE name = @name;", new { name }, cancellationToken: ct)).ConfigureAwait(false);
        if (cipher is null)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(protector.Unprotect(cipher));
        }
        catch (CryptographicException)
        {
            // Encrypted by another Windows user or machine (e.g. a copied database): treat as not set.
            return null;
        }
    }

    public async Task SetAsync(string name, string value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrEmpty(value);
        var cipher = protector.Protect(Encoding.UTF8.GetBytes(value));
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO secrets (name, value, updated_at) VALUES (@name, @cipher, @now)
            ON CONFLICT(name) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
            """,
            new { name, cipher, now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string name, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM secrets WHERE name = @name;", new { name }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
