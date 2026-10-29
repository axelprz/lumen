// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Providers.Steam;

/// <summary>
/// Family games for the Steam provider through the website sign-in (<see cref="ISteamWebSession"/>):
/// <c>IFamilyGroupsService/GetFamilyGroupForUser</c> then <c>GetSharedLibraryApps</c>, as Playnite does. The token
/// lives only in memory and is reused until shortly before it expires, so the background store page is opened about
/// once a day, not on every scan. Read-only.
/// </summary>
public sealed class SteamFamilyAccess(ISteamWebSession session, SteamAccountApi api, TimeProvider? time = null)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (SteamWebToken Token, DateTimeOffset Expires)? _cached;

    /// <summary>Raised when Steam stops (or starts again) accepting the sign-in, so Settings can say so.</summary>
    public event EventHandler? Changed;

    /// <summary>Signed in once, but Steam no longer accepts the browser's session: sign in again.</summary>
    public bool NeedsReconnect { get; private set; }

    /// <summary>Forgets the in-memory token (after disconnecting or signing in again).</summary>
    public void Reset()
    {
        _cached = null;
        SetNeedsReconnect(false);
    }

    public async Task<SteamFamilyResult> GetSharedGamesAsync(CancellationToken ct)
    {
        if (!await session.IsSignedInAsync(ct).ConfigureAwait(false))
        {
            return new(SteamFamilyStatus.NotConnected, []);
        }

        var token = await TokenAsync(ct).ConfigureAwait(false);
        if (token is null)
        {
            SetNeedsReconnect(true);
            return new(SteamFamilyStatus.NeedsReconnect, [], "store page: not signed in");
        }

        var group = await api.GetFamilyGroupAsync(token.AccessToken, token.SteamId, ct).ConfigureAwait(false);
        if (group.Error != SteamAccountError.None)
        {
            return Failure(group.Error, group.Detail);
        }

        SetNeedsReconnect(false);
        if (group.Value is not { } groupId)
        {
            return new(SteamFamilyStatus.NotInFamily, []);
        }

        var games = await api.GetSharedGamesAsync(token.AccessToken, token.SteamId, groupId, ct).ConfigureAwait(false);
        return games.Error == SteamAccountError.None ? new(SteamFamilyStatus.Ok, games.Value!) : Failure(games.Error, games.Detail);
    }

    private async Task<SteamWebToken?> TokenAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is { } cached && cached.Expires - RefreshMargin > _time.GetUtcNow())
            {
                return cached.Token;
            }

            var token = await session.GetTokenAsync(ct).ConfigureAwait(false);
            _cached = token is null ? null : (token, SteamTokens.Read(token.AccessToken)?.Expires ?? _time.GetUtcNow().AddHours(1));
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private SteamFamilyResult Failure(SteamAccountError error, string? detail)
    {
        if (error == SteamAccountError.Unauthorized)
        {
            _cached = null; // maybe just expired early: the next scan reads a fresh one from the store page
            return new(SteamFamilyStatus.Failed, [], "token rejected; will read a new one");
        }

        return new(SteamFamilyStatus.Failed, [], $"{error} {detail}".Trim());
    }

    private void SetNeedsReconnect(bool value)
    {
        if (NeedsReconnect != value)
        {
            NeedsReconnect = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
