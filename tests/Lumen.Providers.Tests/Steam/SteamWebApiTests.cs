// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

public sealed class SteamWebApiTests
{
    // Synthetic: valid shape, not a real account.
    private const string SteamId = "76561190000000001";
    private const string FakeKey = "TESTKEY0000000000000000000000000";

    private readonly StubHttpHandler _http = new();

    [Fact]
    public void Parse_ReadsGames()
    {
        var result = SteamWebApi.ParseOwnedGames(Fixtures.Read("steam/owned_games.json"));

        result.Error.ShouldBe(SteamApiError.None);
        result.Games.Count.ShouldBe(5);
        result.Games[0].ShouldBe(new SteamOwnedGame("1000010", "Sample Quest", 754, 1790001000));
        result.Games[2].Name.ShouldBe("Spaced Title");
        result.Games[4].Name.ShouldBe("1000032"); // no name → app id
    }

    [Fact]
    public void Parse_PrivateProfile()
    {
        SteamWebApi.ParseOwnedGames(Fixtures.Read("steam/owned_games_private.json")).Error.ShouldBe(SteamApiError.PrivateProfile);
    }

    [Fact]
    public void Parse_EmptyLibrary_IsNotAnError()
    {
        var result = SteamWebApi.ParseOwnedGames(Fixtures.Read("steam/owned_games_empty.json"));

        result.Error.ShouldBe(SteamApiError.None);
        result.Games.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_Garbage_IsUnexpected()
    {
        SteamWebApi.ParseOwnedGames("<html>maintenance</html>").Error.ShouldBe(SteamApiError.Unexpected);
    }

    [Fact]
    public async Task Request_HasExpectedEndpointAndParameters()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("steam/owned_games_empty.json"));

        await Api().GetOwnedGamesAsync(FakeKey, SteamId, CancellationToken.None);

        var uri = _http.Requests.Single();
        uri.Scheme.ShouldBe("https");
        uri.Host.ShouldBe("api.steampowered.com");
        uri.AbsolutePath.ShouldBe("/IPlayerService/GetOwnedGames/v1/");
        uri.Query.ShouldContain("steamid=" + SteamId);
        uri.Query.ShouldContain("include_appinfo=1");
        uri.Query.ShouldContain("include_played_free_games=1");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SteamApiError.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, SteamApiError.InvalidKey)]
    [InlineData(HttpStatusCode.TooManyRequests, SteamApiError.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, SteamApiError.Unexpected)]
    public async Task HttpErrors_AreMapped(HttpStatusCode status, SteamApiError expected)
    {
        _http.RespondWith(status);

        var result = await Api().GetOwnedGamesAsync(FakeKey, SteamId, CancellationToken.None);

        result.Error.ShouldBe(expected);
        result.Detail.ShouldNotBeNull();
        result.Detail.ShouldNotContain(FakeKey);
    }

    [Fact]
    public async Task NetworkFailure_IsReported_WithoutTheKey()
    {
        _http.Respond = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");

        var result = await Api().GetOwnedGamesAsync(FakeKey, SteamId, CancellationToken.None);

        result.Error.ShouldBe(SteamApiError.Network);
        result.Detail!.ShouldNotContain(FakeKey);
    }

    [Theory]
    [InlineData("76561190000000001", "76561190000000001")]
    [InlineData("  76561190000000001 ", "76561190000000001")]
    [InlineData("https://steamcommunity.com/profiles/76561190000000001/", "76561190000000001")]
    [InlineData("steamcommunity.com/profiles/76561190000000001", "76561190000000001")]
    [InlineData("https://steamcommunity.com/id/somevanity/", null)]
    [InlineData("12345", null)]
    [InlineData("", null)]
    public void ExtractSteamId64(string input, string? expected)
    {
        SteamWebApi.ExtractSteamId64(input).ShouldBe(expected);
    }

    [Fact]
    public async Task InvalidSteamId_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(() => Api().GetOwnedGamesAsync(FakeKey, "123", CancellationToken.None));
    }

    private SteamWebApi Api() => new(new HttpClient(_http));
}
