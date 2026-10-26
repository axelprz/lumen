// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Settings;

/// <summary>Encrypts secrets for the current Windows user (DPAPI). Abstracted so storage can be tested anywhere.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The data cannot be decrypted (other user/machine, corrupted).</exception>
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>
/// User-supplied API keys and tokens. Values are encrypted at rest, never logged and never synced
/// (security rule 1). Names are fixed constants such as <see cref="SecretNames.SteamWebApiKey"/>.
/// </summary>
public interface ISecretStore
{
    /// <returns>The value, or null when not set or no longer decryptable.</returns>
    Task<string?> GetAsync(string name, CancellationToken ct);

    Task SetAsync(string name, string value, CancellationToken ct);

    Task RemoveAsync(string name, CancellationToken ct);
}

public static class SecretNames
{
    public const string SteamWebApiKey = "steam.webApiKey";

    public const string SteamGridDbApiKey = "steamgriddb.apiKey";
}

/// <summary>Machine-local preferences that never sync (SQLite <c>settings_local</c>).</summary>
public interface ILocalSettings
{
    Task<string?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string value, CancellationToken ct);

    Task RemoveAsync(string key, CancellationToken ct);
}

public static class LocalSettingKeys
{
    public const string SteamId64 = "steam.steamId64";
}
