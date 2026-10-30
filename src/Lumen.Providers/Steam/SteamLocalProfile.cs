// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Platform;

namespace Lumen.Providers.Steam;

/// <summary>The Steam account shown in Lumen's sidebar: its public name and Steam's own cached avatar (a local file).</summary>
public sealed record SteamProfile(string SteamId64, string PersonaName, string? AvatarPath);

/// <summary>
/// Reads the Steam profile from Steam's own files, read-only and without the network (security rules 2 and 4):
/// <c>config/loginusers.vdf</c> lists the accounts that signed in on this PC with their public persona name, and
/// <c>config/avatarcache/&lt;SteamID64&gt;.png</c> is the avatar Steam already downloaded.
/// VERIFY: both confirmed on one Windows 11 machine (2026-10-30); that loginusers.vdf had "timestamp" but no "MostRecent".
/// </summary>
public static class SteamLocalProfile
{
    /// <summary>
    /// The profile for <paramref name="preferredSteamId"/> (the account set in Lumen) when it signed in here; otherwise
    /// the account marked "MostRecent", else the one with the newest "timestamp". Null when nothing usable is found.
    /// </summary>
    public static async Task<SteamProfile?> ReadAsync(IFileSystem fileSystem, string steamPath, string? preferredSteamId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var file = SteamProvider.Join(steamPath, @"config\loginusers.vdf");
        if (!fileSystem.FileExists(file))
        {
            return null;
        }

        VdfObject root;
        try
        {
            root = VdfParser.Parse(await fileSystem.ReadAllTextAsync(file, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is VdfParseException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var users = (root.GetObject("users")?.Properties ?? [])
            .Where(p => p.Value is VdfObject && SteamWebApi.IsValidSteamId64(p.Key))
            .Select(p => (Id: p.Key, User: (VdfObject)p.Value))
            .Where(u => !string.IsNullOrWhiteSpace(u.User.GetString("PersonaName")))
            .ToList();
        if (users.Count == 0)
        {
            return null;
        }

        var pick = users.FirstOrDefault(u => u.Id == preferredSteamId);
        if (pick.User is null)
        {
            pick = users.FirstOrDefault(u => u.User.GetString("MostRecent") == "1");
        }

        if (pick.User is null)
        {
            pick = users.OrderByDescending(u => Timestamp(u.User)).First();
        }

        var avatar = SteamProvider.Join(steamPath, $@"config\avatarcache\{pick.Id}.png");
        return new SteamProfile(pick.Id, pick.User.GetString("PersonaName")!.Trim(), fileSystem.FileExists(avatar) ? avatar : null);
    }

    private static long Timestamp(VdfObject user) =>
        long.TryParse(user.GetString("timestamp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
