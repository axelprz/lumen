// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.Concurrent;
using Lumen.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Providers.Steam;

public enum AchievementsStatus
{
    /// <summary>Counts are available.</summary>
    Available,

    /// <summary>The game has no achievements.</summary>
    NotAvailable,

    /// <summary>No Web API key and no website sign-in: Settings → Steam sets one up.</summary>
    NeedsSetup,

    /// <summary>The profile's game details are private, so Steam hides the achievements.</summary>
    Private,

    /// <summary>Steam could not be reached or answered something unexpected; try again later.</summary>
    Failed,
}

/// <param name="SteamId64">Whose achievements these are (for the link to Steam's page); set when <see cref="AchievementsStatus.Available"/>.</param>
public sealed record AchievementsResult(AchievementsStatus Status, int Unlocked = 0, int Total = 0, string? SteamId64 = null)
{
    /// <summary>Unlocked as a share of the total, 0 to 100.</summary>
    public double Percent => Total <= 0 ? 0 : 100.0 * Unlocked / Total;
}

/// <summary>
/// Achievements of one Steam game for the detail view (PLAN-v1 M12): the account's own web session first, else the Web
/// API key the user entered. Read-only, never throws; answers are kept in memory for a while so opening a detail again
/// does not ask Steam again. Needs the user's game details to be public (or the sign-in), as Steam decides.
/// </summary>
public sealed partial class SteamAchievementsService(
    SteamWebApi api,
    ISecretStore secrets,
    ILocalSettings settings,
    SteamFamilyAccess? family = null,
    TimeProvider? time = null,
    ILogger<SteamAchievementsService>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<SteamAchievementsService>.Instance;
    /// <summary>How long an answer is reused (unlocking something while playing shows after this, or on restart).</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (AchievementsResult Result, DateTimeOffset At)> _cache = new(StringComparer.Ordinal);

    public async Task<AchievementsResult> GetAsync(string appId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(appId) || !appId.All(char.IsAsciiDigit))
        {
            return new AchievementsResult(AchievementsStatus.NotAvailable);
        }

        if (_cache.TryGetValue(appId, out var hit) && _time.GetUtcNow() - hit.At < CacheLifetime)
        {
            return hit.Result;
        }

        var result = await AskAsync(appId, ct).ConfigureAwait(false);
        if (result.Status is AchievementsStatus.Available or AchievementsStatus.NotAvailable)
        {
            _cache[appId] = (result, _time.GetUtcNow());
        }

        return result;
    }

    private async Task<AchievementsResult> AskAsync(string appId, CancellationToken ct)
    {
        try
        {
            var tokenSaidPrivate = false;
            if (family is not null && await family.TokenIfSignedInAsync(ct).ConfigureAwait(false) is { } token)
            {
                var viaToken = await api.GetAchievementsWithTokenAsync(token.AccessToken, token.SteamId, appId, ct).ConfigureAwait(false);
                LogAnswer(appId, "sign-in", viaToken.Error, viaToken.Detail);
                // A "private" answer to the token is not final: the key may be treated differently, so try it too.
                tokenSaidPrivate = viaToken.Error == SteamApiError.PrivateProfile;
                if (!tokenSaidPrivate && Map(viaToken, token.SteamId) is { } mine)
                {
                    return mine;
                }
            }

            var apiKey = await secrets.GetAsync(SecretNames.SteamWebApiKey, ct).ConfigureAwait(false);
            var steamId = await settings.GetAsync(LocalSettingKeys.SteamId64, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(apiKey) || steamId is null || !SteamWebApi.IsValidSteamId64(steamId))
            {
                return new AchievementsResult(tokenSaidPrivate ? AchievementsStatus.Private : AchievementsStatus.NeedsSetup);
            }

            var viaKey = await api.GetAchievementsAsync(apiKey, steamId, appId, ct).ConfigureAwait(false);
            LogAnswer(appId, "key", viaKey.Error, viaKey.Detail);
            return Map(viaKey, steamId) ?? new AchievementsResult(AchievementsStatus.Failed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AchievementsResult(AchievementsStatus.Failed); // never throw out of a detail view
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam achievements for app {AppId} via {Credential}: {Error} {Detail}")]
    private partial void LogAnswer(string appId, string credential, SteamApiError error, string? detail);

    // null = this credential did not work, try the next one.
    private static AchievementsResult? Map(SteamAchievementsResult result, string steamId) => result.Error switch
    {
        SteamApiError.None => new AchievementsResult(AchievementsStatus.Available, result.Unlocked, result.Total, steamId),
        SteamApiError.NoAchievements => new AchievementsResult(AchievementsStatus.NotAvailable),
        SteamApiError.PrivateProfile => new AchievementsResult(AchievementsStatus.Private),
        _ => null,
    };
}
