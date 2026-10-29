// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Settings;

namespace Lumen.Providers.Steam;

/// <summary>
/// Steam family games without any sign-in (owner decision, 2026-10-29, after Steam flagged the account when Lumen
/// signed in as the mobile app): the user picks their family members, and Lumen reads each member's <b>public</b>
/// game list with the user's own Web API key, the same official endpoint as the user's own library. Nothing is sent
/// but the key and the SteamID64s. Steam itself decides, when installing, whether a game can really be shared.
/// The member list is machine-local (<c>settings_local</c> <see cref="LocalSettingKeys.SteamFamilyMembers"/>).
/// </summary>
public sealed class SteamFamilyMembers(SteamWebApi webApi, ISecretStore secrets, ILocalSettings settings)
{
    /// <summary>A Steam family has up to six accounts: you and five more.</summary>
    public const int MaxMembers = 5;

    /// <summary>Raised after the member list changed.</summary>
    public event EventHandler? Changed;

    public async Task<IReadOnlyList<string>> GetIdsAsync(CancellationToken ct) =>
        Parse(await settings.GetAsync(LocalSettingKeys.SteamFamilyMembers, ct).ConfigureAwait(false));

    /// <summary>Saves the members (valid SteamID64s, no duplicates, never the user's own, at most <see cref="MaxMembers"/>).</summary>
    public async Task SetIdsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var own = await settings.GetAsync(LocalSettingKeys.SteamId64, ct).ConfigureAwait(false);
        var clean = ids.Where(SteamWebApi.IsValidSteamId64).Where(id => id != own).Distinct(StringComparer.Ordinal).Take(MaxMembers).ToList();
        if (clean.Count == 0)
        {
            await settings.RemoveAsync(LocalSettingKeys.SteamFamilyMembers, ct).ConfigureAwait(false);
        }
        else
        {
            await settings.SetAsync(LocalSettingKeys.SteamFamilyMembers, string.Join(',', clean), ct).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The members' games, each with how many members own it. Members' playtime is theirs, so it is left out. A
    /// member whose game details are private is counted in <see cref="SteamFamilyResult.PrivateMembers"/>.
    /// </summary>
    public async Task<SteamFamilyResult> GetSharedGamesAsync(CancellationToken ct)
    {
        var ids = await GetIdsAsync(ct).ConfigureAwait(false);
        var key = await secrets.GetAsync(SecretNames.SteamWebApiKey, ct).ConfigureAwait(false);
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(key))
        {
            return new SteamFamilyResult(SteamFamilyStatus.NotConnected, []);
        }

        var owners = new Dictionary<string, (string Name, int Count)>(StringComparer.Ordinal);
        int privateMembers = 0, failed = 0;
        string? lastError = null;
        foreach (var id in ids)
        {
            var result = await webApi.GetOwnedGamesAsync(key, id, ct, includeFreeGames: false).ConfigureAwait(false);
            switch (result.Error)
            {
                case SteamApiError.None:
                    foreach (var game in result.Games.DistinctBy(g => g.AppId))
                    {
                        owners[game.AppId] = owners.TryGetValue(game.AppId, out var seen) ? (seen.Name, seen.Count + 1) : (game.Name, 1);
                    }

                    break;
                case SteamApiError.PrivateProfile:
                    privateMembers++;
                    break;
                default:
                    failed++;
                    lastError = $"{result.Error} {result.Detail}".Trim();
                    break;
            }
        }

        if (failed == ids.Count)
        {
            return new SteamFamilyResult(SteamFamilyStatus.Failed, [], lastError);
        }

        var games = owners.Select(kv => new SteamSharedGame(kv.Key, kv.Value.Name, kv.Value.Count, 0, 0)).ToList();
        return new SteamFamilyResult(failed > 0 ? SteamFamilyStatus.Failed : SteamFamilyStatus.Ok, games, lastError) { PrivateMembers = privateMembers };
    }

    internal static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(SteamWebApi.IsValidSteamId64).Distinct(StringComparer.Ordinal).Take(MaxMembers).ToList();
}
