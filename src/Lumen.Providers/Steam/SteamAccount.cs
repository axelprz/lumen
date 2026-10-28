// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Settings;

namespace Lumen.Providers.Steam;

/// <summary>Who is signed in, for Settings' transparency section. Never contains a token.</summary>
public sealed record SteamAccountInfo(string AccountName, string SteamId, DateTimeOffset ConnectedSince);

public enum SteamFamilyStatus
{
    /// <summary>No Steam sign-in: family games aren't requested.</summary>
    NotConnected,
    Ok,

    /// <summary>Signed in, but the account isn't in a Steam family.</summary>
    NotInFamily,

    /// <summary>Steam rejected the sign-in (revoked from the phone, expired): connect again.</summary>
    NeedsReconnect,
    Failed,
}

public sealed record SteamFamilyResult(SteamFamilyStatus Status, IReadOnlyList<SteamSharedGame> Games, string? Detail = null);

/// <summary>
/// The optional Steam sign-in for family sharing (PLAN-v1 §4.8): QR sign-in approved on the phone (Lumen never sees a
/// password), only the refresh token is stored — encrypted with DPAPI in the secret store — and access tokens live in
/// memory. Read-only: Lumen only asks for the family's shared games. Disconnect revokes at Steam, then deletes.
/// </summary>
public sealed class SteamAccount(SteamAccountApi api, ISecretStore secrets, ILocalSettings settings, TimeProvider? time = null)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private (string Token, DateTimeOffset Expires)? _access;

    /// <summary>Raised after connecting or disconnecting.</summary>
    public event EventHandler? Changed;

    /// <summary>The signed-in account, or null.</summary>
    public async Task<SteamAccountInfo?> GetInfoAsync(CancellationToken ct)
    {
        if (await secrets.GetAsync(SecretNames.SteamRefreshToken, ct).ConfigureAwait(false) is null)
        {
            return null;
        }

        var name = await settings.GetAsync(LocalSettingKeys.SteamAccountName, ct).ConfigureAwait(false);
        var steamId = await settings.GetAsync(LocalSettingKeys.SteamAccountId, ct).ConfigureAwait(false);
        var since = await settings.GetAsync(LocalSettingKeys.SteamAccountSince, ct).ConfigureAwait(false);
        return steamId is null
            ? null
            : new SteamAccountInfo(
                name ?? steamId,
                steamId,
                DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : _time.GetUtcNow());
    }

    public Task<SteamApiResult<SteamQrSession>> StartQrAsync(CancellationToken ct) => api.BeginQrAsync(ct);

    /// <summary>One poll; when approved, the account is saved (refresh token encrypted) and <see cref="Changed"/> fires.</summary>
    public async Task<SteamQrPoll> PollAsync(SteamQrSession session, CancellationToken ct)
    {
        var poll = await api.PollAsync(session, ct).ConfigureAwait(false);
        if (!poll.IsApproved)
        {
            return poll;
        }

        var steamId = SteamTokens.Read(poll.RefreshToken!)?.SteamId;
        if (steamId is null)
        {
            return poll with { Error = SteamAccountError.Unexpected, RefreshToken = null, AccessToken = null, Detail = "token without SteamID" };
        }

        await secrets.SetAsync(SecretNames.SteamRefreshToken, poll.RefreshToken!, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.SteamAccountId, steamId, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.SteamAccountName, poll.AccountName ?? steamId, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.SteamAccountSince, _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        if (poll.AccessToken is { } access && SteamTokens.Read(access)?.Expires is { } expires)
        {
            _access = (access, expires);
        }

        Changed?.Invoke(this, EventArgs.Empty);

        // The caller only needs to know it worked: tokens don't leave this class.
        return poll with { RefreshToken = "stored", AccessToken = null };
    }

    /// <summary>
    /// Revokes the sign-in at Steam (best effort), then deletes everything Lumen stored about it.
    /// </summary>
    /// <returns>True when Steam confirmed the revocation; false when it only was deleted locally.</returns>
    public async Task<bool> DisconnectAsync(CancellationToken ct)
    {
        var revoked = false;
        var refresh = await secrets.GetAsync(SecretNames.SteamRefreshToken, ct).ConfigureAwait(false);
        if (refresh is not null && (await GetAccessTokenAsync(ct).ConfigureAwait(false)).Token is { } access)
        {
            revoked = await api.RevokeAsync(refresh, access, ct).ConfigureAwait(false) == SteamAccountError.None;
        }

        _access = null;
        await secrets.RemoveAsync(SecretNames.SteamRefreshToken, ct).ConfigureAwait(false);
        foreach (var key in new[] { LocalSettingKeys.SteamAccountId, LocalSettingKeys.SteamAccountName, LocalSettingKeys.SteamAccountSince })
        {
            await settings.RemoveAsync(key, ct).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return revoked;
    }

    /// <summary>Games the family shares with this account (owned by someone else), or why there are none.</summary>
    public async Task<SteamFamilyResult> GetSharedGamesAsync(CancellationToken ct)
    {
        var steamId = await settings.GetAsync(LocalSettingKeys.SteamAccountId, ct).ConfigureAwait(false);
        if (steamId is null || await secrets.GetAsync(SecretNames.SteamRefreshToken, ct).ConfigureAwait(false) is null)
        {
            return new(SteamFamilyStatus.NotConnected, []);
        }

        var (access, tokenError) = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (access is null)
        {
            return Failure(tokenError, "access token");
        }

        var group = await api.GetFamilyGroupAsync(access, steamId, ct).ConfigureAwait(false);
        if (group.Error != SteamAccountError.None)
        {
            return Failure(group.Error, group.Detail);
        }

        if (group.Value is not { } groupId)
        {
            return new(SteamFamilyStatus.NotInFamily, []);
        }

        var games = await api.GetSharedGamesAsync(access, steamId, groupId, ct).ConfigureAwait(false);
        return games.Error == SteamAccountError.None
            ? new(SteamFamilyStatus.Ok, games.Value!)
            : Failure(games.Error, games.Detail);
    }

    /// <summary>A valid access token: the one in memory, or a new one from the refresh token; else why not.</summary>
    private async Task<(string? Token, SteamAccountError Error)> GetAccessTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_access is { } cached && cached.Expires - RefreshMargin > _time.GetUtcNow())
            {
                return (cached.Token, SteamAccountError.None);
            }

            var refresh = await secrets.GetAsync(SecretNames.SteamRefreshToken, ct).ConfigureAwait(false);
            var steamId = await settings.GetAsync(LocalSettingKeys.SteamAccountId, ct).ConfigureAwait(false);
            if (refresh is null || steamId is null)
            {
                return (null, SteamAccountError.Unauthorized);
            }

            var result = await api.RefreshAccessTokenAsync(refresh, steamId, ct).ConfigureAwait(false);
            if (result.Error != SteamAccountError.None || result.Value is null)
            {
                _access = null;
                return (null, result.Error == SteamAccountError.None ? SteamAccountError.Unauthorized : result.Error);
            }

            var expires = SteamTokens.Read(result.Value)?.Expires ?? _time.GetUtcNow().AddHours(1);
            _access = (result.Value, expires);
            return (result.Value, SteamAccountError.None);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private SteamFamilyResult Failure(SteamAccountError error, string? detail)
    {
        if (error == SteamAccountError.Unauthorized)
        {
            _access = null; // maybe just expired early: the next scan tries a fresh one
            return new(SteamFamilyStatus.NeedsReconnect, [], detail);
        }

        return new(SteamFamilyStatus.Failed, [], $"{error} {detail}".Trim());
    }
}
