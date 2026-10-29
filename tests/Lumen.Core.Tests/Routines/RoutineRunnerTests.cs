// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Routines;
using Microsoft.Extensions.Logging.Abstractions;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Routines;

public sealed class RoutineRunnerTests
{
    private const string Chat = @"C:\Tools\Chat\chat.exe";

    private readonly FakeProcessControl _processes = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeFileSystem _files = new();
    private readonly RoutineRunner _runner;

    public RoutineRunnerTests()
    {
        _runner = new RoutineRunner(_processes, _launcher, new LaunchService(_launcher, NullLogger<LaunchService>.Instance), _files, NullLogger<RoutineRunner>.Instance);
        _files.Files.Add(Chat);
        _files.Directories.Add(@"C:\Videos");
    }

    [Fact]
    public async Task RunsEveryStepInOrder()
    {
        var game = Game("steam", "400", "Sample Game");
        var routine = Routine(
            new LaunchAction(game.Ref, null),
            new LaunchAction(null, Chat) { Arguments = "--min" },
            new UrlAction("https://example.com/"),
            new FolderAction(@"C:\Videos"),
            new WaitAction(1));

        var run = await RunAsync(routine, game);

        run.Steps.Select(s => s.Outcome).ShouldAllBe(o => o == StepOutcome.Done);
        _processes.StartedPrograms.ShouldBe([(Chat, (string?)"--min", (string?)@"C:\Tools\Chat")]);
        _launcher.Started.Select(s => s.Target).ShouldBe(["steam://run/400", "https://example.com/", "explorer.exe"]);
        _launcher.Started[2].Arguments.ShouldBe("\"C:\\Videos\"");
        run.Started.Count.ShouldBe(1);
        run.LaunchedFolders.ShouldBe([@"C:\Games\Sample Game"]);
    }

    [Fact]
    public async Task AlreadyRunning_IsSkipped()
    {
        _processes.Add(Chat);

        var run = await RunAsync(Routine(new LaunchAction(null, Chat)));

        run.Steps[0].Outcome.ShouldBe(StepOutcome.Skipped);
        _processes.StartedPrograms.ShouldBeEmpty();
    }

    [Fact]
    public async Task RenamedCopyOfAShell_IsBlockedAtRunTime()
    {
        _processes.OriginalNames[Chat] = "Cmd.Exe";

        var run = await RunAsync(Routine(new LaunchAction(null, Chat)));

        run.Steps[0].Outcome.ShouldBe(StepOutcome.Blocked);
        _processes.StartedPrograms.ShouldBeEmpty();
    }

    [Fact]
    public async Task FailuresContinue_UnlessStopOnError()
    {
        var steps = new RoutineAction[] { new LaunchAction(null, @"C:\Missing\x.exe"), new UrlAction("https://example.com/") };

        var carryOn = await RunAsync(Routine(steps));
        var stop = await RunAsync(Routine(steps) with { StopOnError = true });

        carryOn.Steps.Select(s => s.Outcome).ShouldBe([StepOutcome.Failed, StepOutcome.Done]);
        carryOn.Steps[0].Problem.ShouldBe(StepProblem.NotFound);
        stop.Steps.Select(s => s.Outcome).ShouldBe([StepOutcome.Failed, StepOutcome.NotRun]);
    }

    [Fact]
    public async Task NotInstalledGame_Fails_WithoutInstalling()
    {
        var game = Game("steam", "400", "Sample Game", installed: false);

        var run = await RunAsync(Routine(new LaunchAction(game.Ref, null)), game);

        run.Steps[0].Problem.ShouldBe(StepProblem.NotInstalled);
        _launcher.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Close_AsksFirst_AndForcesOnlyWhenTold()
    {
        var polite = _processes.Add(@"C:\Apps\launcher.exe");
        var stubborn = _processes.Add(@"C:\Apps\launcher.exe");
        _processes.Stubborn.Add(stubborn.Id);

        var gentle = await RunAsync(Routine(new CloseAction([], ["launcher.exe"]) { GraceMs = 0 }));

        gentle.Steps[0].Problem.ShouldBe(StepProblem.StillRunning);
        _processes.AskedToClose.ShouldBe([polite.Id, stubborn.Id]);
        _processes.Killed.ShouldBeEmpty();

        var forced = await RunAsync(Routine(new CloseAction([], ["launcher.exe"]) { GraceMs = 0, Force = true }));

        forced.Steps[0].Outcome.ShouldBe(StepOutcome.Done);
        _processes.Killed.ShouldBe([stubborn.Id]);
    }

    [Fact]
    public async Task Close_NeverTouchesWindowsProcesses_FoundThroughAFolder()
    {
        var app = Game("manual", "x", "Odd Item") with { InstallDirectory = @"C:\Windows" };
        _processes.Add(@"C:\Windows\explorer.exe");

        var run = await RunAsync(Routine(new CloseAction([app.Ref], [])), app);

        run.Steps[0].Outcome.ShouldBe(StepOutcome.Skipped);
        _processes.AskedToClose.ShouldBeEmpty();
    }

    [Fact]
    public async Task Undo_ClosesOnlyWhatThisRunOpened()
    {
        var game = Game("steam", "400", "Sample Game");
        var before = _processes.Add(@"C:\Games\Sample Game\old.exe", DateTimeOffset.UtcNow.AddHours(-1));

        var run = await RunAsync(Routine(new LaunchAction(game.Ref, null) { SkipIfRunning = false }, new LaunchAction(null, Chat)), game);
        var gameProcess = _processes.Add(@"C:\Games\Sample Game\game.exe", DateTimeOffset.UtcNow.AddSeconds(5));
        var unrelated = _processes.Add(@"C:\Other\other.exe", DateTimeOffset.UtcNow.AddSeconds(5));

        var undo = await _runner.UndoAsync(run, CancellationToken.None);

        undo.Asked.ShouldBe(2); // chat + the game's process; not the older one, not the unrelated one
        undo.StillRunning.ShouldBeEmpty();
        _processes.Running.ShouldBe([before, unrelated]);
        _processes.Running.ShouldNotContain(gameProcess);
    }

    [Fact]
    public async Task Undo_ReportsWhatIgnoredIt_AndForceClosesOnlyThose()
    {
        var game = Game("steam", "400", "Sample Game");
        var run = await RunAsync(Routine(new LaunchAction(game.Ref, null)), game);
        var stubborn = _processes.Add(@"C:\Games\Sample Game\game.exe", DateTimeOffset.UtcNow.AddSeconds(5));
        _processes.Stubborn.Add(stubborn.Id);
        var other = _processes.Add(@"C:\Other\other.exe");

        var undo = await _runner.UndoAsync(run, CancellationToken.None);

        undo.StillRunning.ShouldBe([stubborn]);
        _processes.Killed.ShouldBeEmpty(); // undo itself never forces

        var left = await _runner.ForceCloseAsync(undo.StillRunning, CancellationToken.None);

        left.ShouldBe(0);
        _processes.Killed.ShouldBe([stubborn.Id]);
        _processes.Running.ShouldBe([other]);
    }

    [Fact]
    public async Task ForceClose_NeverEndsWindowsProcesses()
    {
        var explorer = _processes.Add(@"C:\Windows\explorer.exe");

        await _runner.ForceCloseAsync([explorer], CancellationToken.None);

        _processes.Killed.ShouldBeEmpty();
    }

    [Fact]
    public async Task BlockedReview_NeverRuns()
    {
        var review = RoutineReview.Create(Routine(new LaunchAction(null, @"C:\Windows\System32\cmd.exe")), _ => null, _ => true, _ => true);

        await Should.ThrowAsync<InvalidOperationException>(() => _runner.RunAsync(review, _ => null, null, CancellationToken.None));
        _processes.StartedPrograms.ShouldBeEmpty();
    }

    [Fact]
    public async Task SlowStep_TimesOut()
    {
        var run = await RunAsync(Routine(new WaitAction(5000) { TimeoutMs = 100 }));

        run.Steps[0].Problem.ShouldBe(StepProblem.TimedOut);
    }

    private static Routine Routine(params RoutineAction[] actions) => new("test", new RoutineName("Test"), actions);

    private Task<RoutineRun> RunAsync(Routine routine, params LibraryEntry[] library)
    {
        LibraryItem? Find(ItemRef r) => library.FirstOrDefault(e => e.Ref == r) is { } e ? new LibraryItem([e], e) : null;
        var review = RoutineReview.Create(routine, Find, _files.FileExists, _files.DirectoryExists);
        return _runner.RunAsync(review, Find, null, CancellationToken.None);
    }
}
