// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Settings;
using Lumen.Core.Tests.Sync;
using Lumen.Core.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Core.Tests.Updates;

public sealed class UpdateServiceTests
{
    private readonly FakeUpdateClient _client = new();
    private readonly MapSettings _settings = new();

    private UpdateService Create() => new(_client, _settings, NullLogger<UpdateService>.Instance) { StartupDelay = TimeSpan.Zero };

    private static readonly UpdateOffer V2 = new("0.2.0-beta.1", 40_000_000, "v2");

    [Theory]
    [InlineData("0.0.0-dev", UpdateChannel.Beta)]
    [InlineData("0.3.0-beta.2", UpdateChannel.Beta)]
    [InlineData("0.9.9", UpdateChannel.Beta)]
    [InlineData("1.0.0", UpdateChannel.Stable)]
    [InlineData("v1.2.0", UpdateChannel.Stable)]
    [InlineData("12.0.0-rc.1", UpdateChannel.Stable)]
    [InlineData("", UpdateChannel.Beta)]
    [InlineData(null, UpdateChannel.Beta)]
    [InlineData("banana", UpdateChannel.Beta)]
    public void DefaultChannel_IsBetaUntilOnePointZero(string? version, UpdateChannel expected) =>
        UpdateChannels.DefaultFor(version).ShouldBe(expected);

    [Theory]
    [InlineData("stable", true, UpdateChannel.Stable)]
    [InlineData(" BETA ", true, UpdateChannel.Beta)]
    [InlineData("nightly", false, UpdateChannel.Stable)]
    [InlineData(null, false, UpdateChannel.Stable)]
    public void ChannelText_ParsesLeniently(string? text, bool ok, UpdateChannel expected)
    {
        UpdateChannels.TryParse(text, out var channel).ShouldBe(ok);
        channel.ShouldBe(expected);
    }

    [Fact]
    public async Task Initialize_UsesTheSavedChoices_OrTheDefaults()
    {
        _client.Version = "1.4.0";
        var fresh = Create();
        await fresh.InitializeAsync(CancellationToken.None);
        fresh.Channel.ShouldBe(UpdateChannel.Stable);
        fresh.CheckAutomatically.ShouldBeTrue();

        _settings.Values[LocalSettingKeys.UpdateChannel] = "beta";
        _settings.Values[LocalSettingKeys.UpdateCheckAutomatically] = "0";
        var saved = Create();
        await saved.InitializeAsync(CancellationToken.None);
        saved.Channel.ShouldBe(UpdateChannel.Beta);
        saved.CheckAutomatically.ShouldBeFalse();
    }

    [Fact]
    public async Task ABuildRunFromSource_NeverChecks()
    {
        _client.Installed = false;
        _client.Next = V2;
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);

        await service.CheckAsync(manual: true, CancellationToken.None);
        service.StartAutomaticChecks();
        await Task.Delay(100);

        service.State.ShouldBe(UpdateState.NotInstalled);
        _client.Checks.ShouldBe(0);
    }

    [Fact]
    public async Task ANewVersion_IsOffered_NotDownloaded()
    {
        _client.Next = V2;
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);

        await service.CheckAsync(manual: true, CancellationToken.None);

        service.State.ShouldBe(UpdateState.Available);
        service.Offer.ShouldBe(V2);
        service.LastChecked.ShouldNotBeNull();
        _client.Downloads.ShouldBe(0);
        _client.CheckedChannels.ShouldBe([UpdateChannel.Beta]);
    }

    [Fact]
    public async Task NothingNew_IsUpToDate()
    {
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);

        await service.CheckAsync(manual: true, CancellationToken.None);

        service.State.ShouldBe(UpdateState.UpToDate);
        service.Offer.ShouldBeNull();
    }

    [Fact]
    public async Task TheTrayNotice_IsOncePerVersion_AndNeverForAManualCheck()
    {
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);
        var announced = new List<string>();
        service.Announce += (_, offer) => announced.Add(offer.Version);

        _client.Next = V2;
        await service.CheckAsync(manual: true, CancellationToken.None);
        announced.ShouldBeEmpty();

        await service.CheckAsync(manual: false, CancellationToken.None);
        await service.CheckAsync(manual: false, CancellationToken.None);
        announced.ShouldBe(["0.2.0-beta.1"]);
        _settings.Values[LocalSettingKeys.UpdateNotifiedVersion].ShouldBe("0.2.0-beta.1");

        _client.Next = new UpdateOffer("0.3.0-beta.1", 1, "v3");
        await service.CheckAsync(manual: false, CancellationToken.None);
        announced.ShouldBe(["0.2.0-beta.1", "0.3.0-beta.1"]);
    }

    [Fact]
    public async Task AFailedCheck_IsReported_AndTheNextOneStartsClean()
    {
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);
        _client.Fail = true;

        await service.CheckAsync(manual: true, CancellationToken.None);
        service.State.ShouldBe(UpdateState.Failed);

        _client.Fail = false;
        _client.Next = V2;
        await service.CheckAsync(manual: true, CancellationToken.None);
        service.State.ShouldBe(UpdateState.Available);
    }

    [Fact]
    public async Task Download_OnlyWhenOffered_ReportsProgress_ThenWaitsForTheUser()
    {
        _client.Next = V2;
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);
        await service.DownloadAsync(CancellationToken.None); // nothing offered yet
        _client.Downloads.ShouldBe(0);

        await service.CheckAsync(manual: true, CancellationToken.None);
        var percents = new List<int>();
        service.Changed += (_, _) => percents.Add(service.Percent);
        await service.DownloadAsync(CancellationToken.None);

        service.State.ShouldBe(UpdateState.ReadyToRestart);
        service.Percent.ShouldBe(100);
        percents.ShouldContain(50);
        _client.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFailedDownload_GoesBackToFailed()
    {
        _client.Next = V2;
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);
        await service.CheckAsync(manual: true, CancellationToken.None);
        _client.Fail = true;

        await service.DownloadAsync(CancellationToken.None);

        service.State.ShouldBe(UpdateState.Failed);
        service.Offer.ShouldBeNull();
    }

    [Fact]
    public async Task Restart_OnlyAfterTheDownload_AndReleasesTheAppFirst()
    {
        _client.Next = V2;
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);
        var order = new List<string>();
        service.BeforeRestart = () => order.Add("before");
        _client.OnApply = () => order.Add("apply");

        service.RestartToUpdate();
        order.ShouldBeEmpty();

        await service.CheckAsync(manual: true, CancellationToken.None);
        service.RestartToUpdate();
        order.ShouldBeEmpty();

        await service.DownloadAsync(CancellationToken.None);
        service.RestartToUpdate();
        order.ShouldBe(["before", "apply"]);
        _client.Applied.ShouldBe([V2]);
    }

    [Fact]
    public async Task ADownloadFromAnEarlierRun_IsReadyToRestart()
    {
        _client.Pending = V2;
        var service = Create();

        await service.InitializeAsync(CancellationToken.None);

        service.State.ShouldBe(UpdateState.ReadyToRestart);
        service.Offer.ShouldBe(V2);
        await service.CheckAsync(manual: true, CancellationToken.None); // doesn't look again while one waits
        _client.Checks.ShouldBe(0);
    }

    [Fact]
    public async Task ChangingTheChannel_IsSaved_AndForgetsWhatWasFound()
    {
        _client.Next = V2;
        var service = Create();
        await service.InitializeAsync(CancellationToken.None);
        await service.CheckAsync(manual: true, CancellationToken.None);

        await service.SetChannelAsync(UpdateChannel.Stable, CancellationToken.None);

        service.Channel.ShouldBe(UpdateChannel.Stable);
        service.State.ShouldBe(UpdateState.Idle);
        service.Offer.ShouldBeNull();
        _settings.Values[LocalSettingKeys.UpdateChannel].ShouldBe("stable");

        _client.Next = null;
        await service.CheckAsync(manual: true, CancellationToken.None);
        _client.CheckedChannels.ShouldBe([UpdateChannel.Beta, UpdateChannel.Stable]);
    }

    [Fact]
    public async Task AutomaticChecks_RunAtStart_OnlyWhenOn()
    {
        _client.Next = V2;
        var on = Create();
        await on.InitializeAsync(CancellationToken.None);
        on.StartAutomaticChecks();
        await WaitForAsync(() => on.State == UpdateState.Available);
        on.Dispose();

        var checks = _client.Checks;
        _settings.Values[LocalSettingKeys.UpdateCheckAutomatically] = "0";
        var off = Create();
        await off.InitializeAsync(CancellationToken.None);
        off.StartAutomaticChecks();
        await Task.Delay(200);
        off.Dispose();

        _client.Checks.ShouldBe(checks);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public bool Installed { get; set; } = true;

        public string? Version { get; set; } = "0.1.0";

        public UpdateOffer? Pending { get; set; }

        public UpdateOffer? Next { get; set; }

        public bool Fail { get; set; }

        public int Checks { get; private set; }

        public int Downloads { get; private set; }

        public List<UpdateChannel> CheckedChannels { get; } = [];

        public List<UpdateOffer> Applied { get; } = [];

        public Action? OnApply { get; set; }

        public bool IsInstalled => Installed;

        public string? CurrentVersion => Version;

        public UpdateOffer? PendingRestart => Pending;

        public Task<UpdateOffer?> CheckAsync(UpdateChannel channel, CancellationToken ct)
        {
            Checks++;
            CheckedChannels.Add(channel);
            return Fail ? throw new HttpRequestException("offline") : Task.FromResult(Next);
        }

        public Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken ct)
        {
            Downloads++;
            if (Fail)
            {
                throw new HttpRequestException("offline");
            }

            progress(50);
            return Task.CompletedTask;
        }

        public void ApplyAndRestart(UpdateOffer offer)
        {
            Applied.Add(offer);
            OnApply?.Invoke();
        }
    }
}
