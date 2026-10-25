// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Security.Cryptography;
using System.Text;
using Dapper;
using Lumen.Core.Settings;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SettingsStoreTests : IAsyncLifetime
{
    private const string Secret = "TESTKEY0000000000000000000000000";

    private readonly TempDatabase _db = new();
    private readonly ReversingProtector _protector = new();

    public async Task InitializeAsync() =>
        await new MigrationRunner(_db.Factory, MigrationLoader.LoadEmbedded(), NullLogger<MigrationRunner>.Instance)
            .MigrateAsync(CancellationToken.None);

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Secret_RoundTrips_AndIsNeverStoredInPlaintext()
    {
        var store = new SqliteSecretStore(_db.Factory, _protector);

        await store.SetAsync(SecretNames.SteamWebApiKey, Secret, CancellationToken.None);

        (await store.GetAsync(SecretNames.SteamWebApiKey, CancellationToken.None)).ShouldBe(Secret);
        await using var connection = await _db.Factory.OpenAsync(CancellationToken.None);
        var raw = await connection.QuerySingleAsync<byte[]>("SELECT value FROM secrets;");
        Encoding.UTF8.GetString(raw).ShouldNotContain(Secret);
    }

    [Fact]
    public async Task Secret_Overwrite_And_Remove()
    {
        var store = new SqliteSecretStore(_db.Factory, _protector);

        await store.SetAsync("a", "one", CancellationToken.None);
        await store.SetAsync("a", "two", CancellationToken.None);
        (await store.GetAsync("a", CancellationToken.None)).ShouldBe("two");

        await store.RemoveAsync("a", CancellationToken.None);
        (await store.GetAsync("a", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Secret_ThatCannotBeDecrypted_ReadsAsNotSet()
    {
        await new SqliteSecretStore(_db.Factory, _protector).SetAsync("a", "one", CancellationToken.None);

        var otherUser = new SqliteSecretStore(_db.Factory, new FailingProtector());

        (await otherUser.GetAsync("a", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task LocalSettings_RoundTrip()
    {
        var settings = new SqliteLocalSettings(_db.Factory);

        (await settings.GetAsync(LocalSettingKeys.SteamId64, CancellationToken.None)).ShouldBeNull();
        await settings.SetAsync(LocalSettingKeys.SteamId64, "76561190000000001", CancellationToken.None);
        await settings.SetAsync(LocalSettingKeys.SteamId64, "76561190000000002", CancellationToken.None);
        (await settings.GetAsync(LocalSettingKeys.SteamId64, CancellationToken.None)).ShouldBe("76561190000000002");

        await settings.RemoveAsync(LocalSettingKeys.SteamId64, CancellationToken.None);
        (await settings.GetAsync(LocalSettingKeys.SteamId64, CancellationToken.None)).ShouldBeNull();
    }

    /// <summary>Stand-in for DPAPI in OS-agnostic tests: reverses and XORs bytes so plaintext never appears.</summary>
    private sealed class ReversingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray();

        public byte[] Unprotect(byte[] ciphertext) => ciphertext.Select(b => (byte)(b ^ 0x5A)).Reverse().ToArray();
    }

    private sealed class FailingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new CryptographicException();

        public byte[] Unprotect(byte[] ciphertext) => throw new CryptographicException("Key not valid for use in specified state.");
    }
}
