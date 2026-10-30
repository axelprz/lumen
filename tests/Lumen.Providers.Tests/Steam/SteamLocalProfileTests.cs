// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

public sealed class SteamLocalProfileTests
{
    private const string SteamDir = @"C:\Steam";
    private const string LoginUsers = @"C:\Steam\config\loginusers.vdf";

    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task NoLoginUsers_IsNull()
    {
        (await SteamLocalProfile.ReadAsync(_fs, SteamDir, null, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task WithoutPreference_PicksTheNewestTimestamp_AndSkipsUsersWithoutAName()
    {
        _fs.AddFixture(LoginUsers, "steam/profile/loginusers.vdf");

        var profile = await SteamLocalProfile.ReadAsync(_fs, SteamDir, null, CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SteamId64.ShouldBe("76561190000000002");
        profile.PersonaName.ShouldBe("Player Two");
        profile.AvatarPath.ShouldBeNull();
    }

    [Fact]
    public async Task ThePreferredAccount_WinsWhenItSignedInHere()
    {
        _fs.AddFixture(LoginUsers, "steam/profile/loginusers.vdf");

        var profile = await SteamLocalProfile.ReadAsync(_fs, SteamDir, "76561190000000001", CancellationToken.None);

        profile!.PersonaName.ShouldBe("Player One");
    }

    [Fact]
    public async Task MostRecent_BeatsTimestamp()
    {
        _fs.AddFixture(LoginUsers, "steam/profile/loginusers_mostrecent.vdf");

        (await SteamLocalProfile.ReadAsync(_fs, SteamDir, null, CancellationToken.None))!.SteamId64.ShouldBe("76561190000000001");
    }

    [Fact]
    public async Task CachedAvatar_IsUsedWhenPresent()
    {
        const string avatar = @"C:\Steam\config\avatarcache\76561190000000002.png";
        _fs.AddFixture(LoginUsers, "steam/profile/loginusers.vdf");
        _fs.AddFile(avatar, "png");

        (await SteamLocalProfile.ReadAsync(_fs, SteamDir, null, CancellationToken.None))!.AvatarPath.ShouldBe(avatar);
    }

    [Fact]
    public async Task BrokenFile_IsNull()
    {
        _fs.AddFile(LoginUsers, "\"users\" { \"76561190000000001\" {");

        (await SteamLocalProfile.ReadAsync(_fs, SteamDir, null, CancellationToken.None)).ShouldBeNull();
    }
}
