// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Core.Routines;
using Lumen.Core.Tests.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Core.Tests.Routines;

public sealed class RoutineServiceTests
{
    private const string Folder = @"C:\Lumen\portable\routines";

    private readonly MemoryFiles _files = new();
    private readonly MemoryTrust _trust = new();
    private readonly FakeProcessControl _processes = new();
    private readonly FakeLauncher _launcher = new();
    private readonly RoutineService _service;

    public RoutineServiceTests()
    {
        var library = new LibraryService([], new InMemoryCache(), NullLogger<LibraryService>.Instance);
        var runner = new RoutineRunner(_processes, _launcher, new LaunchService(_launcher, NullLogger<LaunchService>.Instance), _files, NullLogger<RoutineRunner>.Instance);
        _service = new RoutineService(Folder, _files, _files, _trust, runner, library, NullLogger<RoutineService>.Instance);
    }

    [Fact]
    public async Task SavedFromTheEditor_IsReady()
    {
        await _service.SaveAsync(Simple("evening"), null, CancellationToken.None);

        _service.Entries.ShouldHaveSingleItem().State.ShouldBe(RoutineTrustState.Trusted);
        _files.Text.ShouldContainKey(Folder + @"\evening.json");
    }

    [Fact]
    public async Task ChangedOutsideLumen_NeedsReview_AndDoesNotRun()
    {
        await _service.SaveAsync(Simple("evening"), null, CancellationToken.None);
        _files.Text[Folder + @"\evening.json"] = RoutineJson.Serialize(Simple("evening") with { Actions = [new UrlAction("https://example.org/other")] });

        var (outcome, run) = await _service.RunAsync("evening", null, CancellationToken.None);

        outcome.ShouldBe(RoutineRunOutcome.NeedsReview);
        run.ShouldBeNull();
        _launcher.Started.ShouldBeEmpty();
        _service.Entries.Single().State.ShouldBe(RoutineTrustState.NeedsReview);
    }

    [Fact]
    public async Task Imported_NeedsReview_ThenRunsOnceApproved()
    {
        _files.Text[@"C:\Downloads\shared.json"] = RoutineJson.Serialize(Simple("shared"));

        var parsed = await _service.ImportAsync(@"C:\Downloads\shared.json", CancellationToken.None);

        parsed.IsValid.ShouldBeTrue();
        var entry = _service.Entries.Single();
        entry.State.ShouldBe(RoutineTrustState.NeedsReview);
        (await _service.RunAsync("shared", null, CancellationToken.None)).Outcome.ShouldBe(RoutineRunOutcome.NeedsReview);

        await _service.ApproveAsync(entry, CancellationToken.None);
        var (outcome, _) = await _service.RunAsync("shared", null, CancellationToken.None);

        outcome.ShouldBe(RoutineRunOutcome.Ran);
        _launcher.Started.Select(s => s.Target).ShouldBe(["https://example.com/"]);
    }

    [Fact]
    public async Task BlockedSteps_CantBeApprovedOrSaved()
    {
        var blocked = Simple("bad") with { Actions = [new LaunchAction(null, @"C:\Windows\System32\cmd.exe")] };
        _files.Text[Folder + @"\bad.json"] = RoutineJson.Serialize(blocked);
        await _service.LoadAsync(CancellationToken.None);

        var entry = _service.Entries.Single();
        entry.State.ShouldBe(RoutineTrustState.Invalid);
        await Should.ThrowAsync<InvalidOperationException>(() => _service.ApproveAsync(entry, CancellationToken.None));

        await _service.SaveAsync(blocked, "bad", CancellationToken.None); // the editor stops this earlier; the service never approves it
        _trust.Approved.ShouldNotContainKey("bad");
    }

    [Fact]
    public async Task FileNameMustMatchTheId()
    {
        _files.Text[Folder + @"\renamed.json"] = RoutineJson.Serialize(Simple("original"));

        await _service.LoadAsync(CancellationToken.None);

        var entry = _service.Entries.Single();
        entry.State.ShouldBe(RoutineTrustState.Invalid);
        entry.Parse.Errors.Single().ShouldContain("original.json");
    }

    [Fact]
    public async Task Rename_MovesTheFileAndItsApproval()
    {
        await _service.SaveAsync(Simple("old-name"), null, CancellationToken.None);

        await _service.SaveAsync(Simple("new-name"), "old-name", CancellationToken.None);

        _files.Text.Keys.ShouldBe([Folder + @"\new-name.json"]);
        _trust.Approved.Keys.ShouldBe(["new-name"]);
    }

    [Fact]
    public async Task Delete_RemovesFileAndApproval()
    {
        await _service.SaveAsync(Simple("evening"), null, CancellationToken.None);

        await _service.DeleteAsync(_service.Entries.Single(), CancellationToken.None);

        _files.Text.ShouldBeEmpty();
        _trust.Approved.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Noche de juegos", "noche-de-juegos")]
    [InlineData("  ¡Café & Música!  ", "cafe-musica")]
    [InlineData("???", "routine")]
    public void NewIds_AreSlugs(string name, string expected) => _service.NewId(name).ShouldBe(expected);

    [Fact]
    public async Task NewIds_AvoidTakenOnes()
    {
        await _service.SaveAsync(Simple("evening"), null, CancellationToken.None);

        _service.NewId("Evening").ShouldBe("evening-2");
    }

    private static Routine Simple(string id) => new(id, new RoutineName("Test"), [new UrlAction("https://example.com/")]);

    private sealed class MemoryTrust : IRoutineTrustStore
    {
        public Dictionary<string, string> Approved { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Approved));

        public Task ApproveAsync(string routineId, string hash, CancellationToken ct)
        {
            Approved[routineId] = hash;
            return Task.CompletedTask;
        }

        public Task ForgetAsync(string routineId, CancellationToken ct)
        {
            Approved.Remove(routineId);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryFiles : IFileSystem, IUserFiles
    {
        public Dictionary<string, string> Text { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => Text.ContainsKey(path);

        public bool DirectoryExists(string path) => true;

        public IReadOnlyList<string> GetFiles(string directory, string pattern) =>
            Text.Keys.Where(k => string.Equals(Path.GetDirectoryName(k), directory, StringComparison.OrdinalIgnoreCase) && k.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();

        public Task<string> ReadAllTextAsync(string path, CancellationToken ct) =>
            Text.TryGetValue(path, out var text) ? Task.FromResult(text) : throw new FileNotFoundException(path);

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => throw new FileNotFoundException(path);

        public IReadOnlyList<string> GetDirectories(string directory) => [];

        public Task WriteTextAsync(string path, string text, CancellationToken ct)
        {
            Text[path] = text;
            return Task.CompletedTask;
        }

        public Task WriteChosenFileAsync(string path, string text, CancellationToken ct) => WriteTextAsync(path, text, ct);

        public void Delete(string path) => Text.Remove(path);
    }
}
