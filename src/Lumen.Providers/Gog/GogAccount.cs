// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Settings;

namespace Lumen.Providers.Gog;

/// <summary>Who is signed in to GOG, for Settings. Never contains a token.</summary>
public sealed record GogAccountInfo(string UserName, string UserId, DateTimeOffset ConnectedSince);

public enum GogConnectResult
{
    Connected,

    /// <summary>What was pasted isn't the address GOG ended on, nor a code.</summary>
    NotACode,

    /// <summary>GOG refused the code (wrong, already used, or expired).</summary>
    CodeRejected,
    Failed,
}

public enum GogOwnedStatus
{
    NotConnected,
    Ok,

    /// <summary>The sign-in was rejected or expired: connect again.</summary>
    NeedsReconnect,
    Failed,
}

public sealed record GogOwnedResult(GogOwnedStatus Status, IReadOnlyList<GogOwnedGame> Games, string? Detail = null);

/// <summary>
/// The optional GOG sign-in (PLAN-v1 §4.8): the user signs in on GOG's page and pastes the address it ends on (Lumen
/// never sees the password); only the refresh token is stored (DPAPI secret store), replaced on every refresh; access
/// tokens live in memory. Read-only: the owned games list. GOG can't revoke a sign-in from outside, so Disconnect
/// deletes it here (Settings says so) and it expires by itself.
/// </summary>
public sealed class GogAccount(GogAccountApi api, ISecretStore secrets, ILocalSettings settings, TimeProvider? time = null)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private (string Token, DateTimeOffset Expires)? _access;

    public event EventHandler? Changed;

    public async Task<GogAccountInfo?> GetInfoAsync(CancellationToken ct)
    {
        if (await secrets.GetAsync(SecretNames.GogRefreshToken, ct).ConfigureAwait(false) is null
            || await settings.GetAsync(LocalSettingKeys.GogAccountId, ct).ConfigureAwait(false) is not { } id)
        {
            return null;
        }

        var name = await settings.GetAsync(LocalSettingKeys.GogAccountName, ct).ConfigureAwait(false);
        var since = await settings.GetAsync(LocalSettingKeys.GogAccountSince, ct).ConfigureAwait(false);
        return new GogAccountInfo(
            name ?? id,
            id,
            DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : _time.GetUtcNow());
    }

    public async Task<(GogConnectResult Result, string? Detail)> ConnectAsync(string? pasted, CancellationToken ct)
    {
        if (GogAccountApi.ExtractCode(pasted) is not { } code)
        {
            return (GogConnectResult.NotACode, null);
        }

        var exchange = await api.ExchangeCodeAsync(code, ct).ConfigureAwait(false);
        if (exchange.Value is not { } session)
        {
            return (exchange.Error == GogAccountError.Unauthorized ? GogConnectResult.CodeRejected : GogConnectResult.Failed, exchange.Detail);
        }

        await StoreAsync(session, ct).ConfigureAwait(false);
        var name = (await api.GetUserNameAsync(session.AccessToken, ct).ConfigureAwait(false)).Value ?? session.UserId;
        await settings.SetAsync(LocalSettingKeys.GogAccountId, session.UserId, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.GogAccountName, name, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.GogAccountSince, _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return (GogConnectResult.Connected, null);
    }

    /// <summary>Deletes the sign-in from this PC (GOG has no way to revoke it from outside).</summary>
    public async Task DisconnectAsync(CancellationToken ct)
    {
        _access = null;
        await secrets.RemoveAsync(SecretNames.GogRefreshToken, ct).ConfigureAwait(false);
        foreach (var key in new[] { LocalSettingKeys.GogAccountId, LocalSettingKeys.GogAccountName, LocalSettingKeys.GogAccountSince })
        {
            await settings.RemoveAsync(key, ct).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<GogOwnedResult> GetOwnedAsync(CancellationToken ct)
    {
        if (await secrets.GetAsync(SecretNames.GogRefreshToken, ct).ConfigureAwait(false) is null)
        {
            return new(GogOwnedStatus.NotConnected, []);
        }

        var (access, error, detail) = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (access is null)
        {
            return Failure(error, "renewing access: " + detail);
        }

        var owned = await api.GetOwnedGamesAsync(access, ct).ConfigureAwait(false);
        return owned.Value is { } games ? new(GogOwnedStatus.Ok, games) : Failure(owned.Error, owned.Detail);
    }

    private async Task<(string? Token, GogAccountError Error, string? Detail)> GetAccessTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_access is { } cached && cached.Expires - RefreshMargin > _time.GetUtcNow())
            {
                return (cached.Token, GogAccountError.None, null);
            }

            if (await secrets.GetAsync(SecretNames.GogRefreshToken, ct).ConfigureAwait(false) is not { } refresh)
            {
                return (null, GogAccountError.Unauthorized, null);
            }

            var result = await api.RefreshAsync(refresh, ct).ConfigureAwait(false);
            if (result.Value is not { } session)
            {
                _access = null;
                return (null, result.Error == GogAccountError.None ? GogAccountError.Unauthorized : result.Error, result.Detail);
            }

            await StoreAsync(session, ct).ConfigureAwait(false); // GOG issues a new refresh token each time
            return (session.AccessToken, GogAccountError.None, null);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task StoreAsync(GogSession session, CancellationToken ct)
    {
        await secrets.SetAsync(SecretNames.GogRefreshToken, session.RefreshToken, ct).ConfigureAwait(false);
        _access = (session.AccessToken, session.AccessExpires);
    }

    private GogOwnedResult Failure(GogAccountError error, string? detail)
    {
        if (error == GogAccountError.Unauthorized)
        {
            _access = null;
            return new(GogOwnedStatus.NeedsReconnect, [], detail);
        }

        return new(GogOwnedStatus.Failed, [], $"{error} {detail}".Trim());
    }
}
