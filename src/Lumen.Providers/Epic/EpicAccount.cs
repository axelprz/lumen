// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Settings;

namespace Lumen.Providers.Epic;

/// <summary>Who is signed in to Epic, for Settings. Never contains a token.</summary>
public sealed record EpicAccountInfo(string DisplayName, string AccountId, DateTimeOffset ConnectedSince);

public enum EpicConnectResult
{
    Connected,

    /// <summary>What was pasted isn't an authorization code.</summary>
    NotACode,

    /// <summary>Epic refused the code (wrong, already used, or older than a few minutes).</summary>
    CodeRejected,

    /// <summary>Epic wants something done on its website first (terms, verification).</summary>
    ActionRequired,
    Failed,
}

public enum EpicOwnedStatus
{
    NotConnected,
    Ok,

    /// <summary>The sign-in was revoked or expired: connect again.</summary>
    NeedsReconnect,
    Failed,
}

public sealed record EpicOwnedResult(EpicOwnedStatus Status, IReadOnlyList<EpicLibraryRecord> Records, string? Detail = null);

/// <summary>
/// The optional Epic sign-in (PLAN-v1 §4.8): the user signs in on Epic's page and pastes the code (Lumen never sees
/// the password); only the refresh token is stored (DPAPI secret store), replaced by the new one Epic issues on each
/// refresh; access tokens live in memory. Read-only: the owned library and catalog titles. Disconnect ends the session
/// at Epic, then deletes it here.
/// </summary>
public sealed class EpicAccount(EpicAccountApi api, ISecretStore secrets, ILocalSettings settings, TimeProvider? time = null)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private (string Token, DateTimeOffset Expires)? _access;

    public event EventHandler? Changed;

    public async Task<EpicAccountInfo?> GetInfoAsync(CancellationToken ct)
    {
        if (await secrets.GetAsync(SecretNames.EpicRefreshToken, ct).ConfigureAwait(false) is null
            || await settings.GetAsync(LocalSettingKeys.EpicAccountId, ct).ConfigureAwait(false) is not { } id)
        {
            return null;
        }

        var name = await settings.GetAsync(LocalSettingKeys.EpicAccountName, ct).ConfigureAwait(false);
        var since = await settings.GetAsync(LocalSettingKeys.EpicAccountSince, ct).ConfigureAwait(false);
        return new EpicAccountInfo(
            name ?? id,
            id,
            DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : _time.GetUtcNow());
    }

    /// <summary>Exchanges the pasted code; on success stores the account and raises <see cref="Changed"/>.</summary>
    public async Task<(EpicConnectResult Result, string? Detail)> ConnectAsync(string? pasted, CancellationToken ct)
    {
        if (EpicAccountApi.ExtractCode(pasted) is not { } code)
        {
            return (EpicConnectResult.NotACode, null);
        }

        var exchange = await api.ExchangeCodeAsync(code, ct).ConfigureAwait(false);
        if (exchange.Value is not { } session)
        {
            return (exchange.Error switch
            {
                EpicAccountError.Unauthorized => EpicConnectResult.CodeRejected,
                EpicAccountError.ActionRequired => EpicConnectResult.ActionRequired,
                _ => EpicConnectResult.Failed,
            }, exchange.Detail);
        }

        await StoreAsync(session, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.EpicAccountId, session.AccountId, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.EpicAccountName, session.DisplayName, ct).ConfigureAwait(false);
        await settings.SetAsync(LocalSettingKeys.EpicAccountSince, _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return (EpicConnectResult.Connected, null);
    }

    /// <returns>True when Epic confirmed ending the session; false when it was only deleted locally.</returns>
    public async Task<bool> DisconnectAsync(CancellationToken ct)
    {
        var ended = false;
        if ((await GetAccessTokenAsync(ct).ConfigureAwait(false)).Token is { } access)
        {
            ended = await api.KillSessionAsync(access, ct).ConfigureAwait(false) == EpicAccountError.None;
        }

        _access = null;
        await secrets.RemoveAsync(SecretNames.EpicRefreshToken, ct).ConfigureAwait(false);
        foreach (var key in new[] { LocalSettingKeys.EpicAccountId, LocalSettingKeys.EpicAccountName, LocalSettingKeys.EpicAccountSince })
        {
            await settings.RemoveAsync(key, ct).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return ended;
    }

    public async Task<EpicOwnedResult> GetOwnedAsync(CancellationToken ct)
    {
        if (await secrets.GetAsync(SecretNames.EpicRefreshToken, ct).ConfigureAwait(false) is null)
        {
            return new(EpicOwnedStatus.NotConnected, []);
        }

        var (access, error) = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (access is null)
        {
            return Failure(error, "access token");
        }

        var library = await api.GetLibraryAsync(access, ct).ConfigureAwait(false);
        return library.Value is { } records ? new(EpicOwnedStatus.Ok, records) : Failure(library.Error, library.Detail);
    }

    /// <summary>Catalog title and kind for one owned item (callers cache the answer).</summary>
    public async Task<EpicApiResult<EpicCatalogInfo>> LookupCatalogAsync(string ns, string catalogItemId, CancellationToken ct)
    {
        var (access, error) = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        return access is null ? new(error, null) : await api.GetCatalogInfoAsync(access, ns, catalogItemId, ct).ConfigureAwait(false);
    }

    private async Task<(string? Token, EpicAccountError Error)> GetAccessTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_access is { } cached && cached.Expires - RefreshMargin > _time.GetUtcNow())
            {
                return (cached.Token, EpicAccountError.None);
            }

            if (await secrets.GetAsync(SecretNames.EpicRefreshToken, ct).ConfigureAwait(false) is not { } refresh)
            {
                return (null, EpicAccountError.Unauthorized);
            }

            var result = await api.RefreshAsync(refresh, ct).ConfigureAwait(false);
            if (result.Value is not { } session)
            {
                _access = null;
                return (null, result.Error == EpicAccountError.None ? EpicAccountError.Unauthorized : result.Error);
            }

            await StoreAsync(session, ct).ConfigureAwait(false); // Epic issues a new refresh token each time
            return (session.AccessToken, EpicAccountError.None);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task StoreAsync(EpicSession session, CancellationToken ct)
    {
        await secrets.SetAsync(SecretNames.EpicRefreshToken, session.RefreshToken, ct).ConfigureAwait(false);
        _access = (session.AccessToken, session.AccessExpires);
    }

    private EpicOwnedResult Failure(EpicAccountError error, string? detail)
    {
        if (error is EpicAccountError.Unauthorized or EpicAccountError.ActionRequired)
        {
            _access = null;
            return new(EpicOwnedStatus.NeedsReconnect, [], detail);
        }

        return new(EpicOwnedStatus.Failed, [], $"{error} {detail}".Trim());
    }
}
