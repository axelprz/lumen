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

    /// <summary>Steam sign-in (family sharing): the long-lived refresh token. Access tokens are never stored.</summary>
    public const string SteamRefreshToken = "steam.refreshToken";

    /// <summary>Epic sign-in: the refresh token (rotates on every use; the newest one is kept).</summary>
    public const string EpicRefreshToken = "epic.refreshToken";
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

    /// <summary>Signed-in Steam account (family sharing), shown in Settings: account name, SteamID64, since when.</summary>
    public const string SteamAccountName = "steam.account.name";

    public const string SteamAccountId = "steam.account.steamId";

    public const string SteamAccountSince = "steam.account.since";

    /// <summary>Signed-in Epic account, shown in Settings: display name, account id, since when.</summary>
    public const string EpicAccountName = "epic.account.name";

    public const string EpicAccountId = "epic.account.id";

    public const string EpicAccountSince = "epic.account.since";

    /// <summary>
    /// UI language: "system", "en" or "es" (<see cref="Localization.LanguageResolver"/>). Machine-local until the sync
    /// folder arrives (M8), when it moves to <c>config.json</c> → <c>language</c>.
    /// </summary>
    public const string Language = "ui.language";

    /// <summary>"1" once the first-run wizard was finished or skipped.</summary>
    public const string FirstRunCompleted = "firstRun.completed";
}
