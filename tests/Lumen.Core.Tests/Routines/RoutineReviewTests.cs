// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Routines;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Routines;

public sealed class RoutineReviewTests
{
    [Fact]
    public void FlagsProgramsOutsideTheLibrary_AndCloseActions()
    {
        var routine = RoutineParser.Parse(RoutineFixtures.Read("game-night.json")).Routine!;
        var game = Game("steam", "400", "Sample Game");

        var review = RoutineReview.Create(routine, r => r == game.Ref ? new LibraryItem([game], game) : null, _ => false, _ => true);

        review.CanRun.ShouldBeTrue();
        review.Steps[0].Target.ShouldBe("Sample Game");
        review.Steps[0].Notes.ShouldBe(StepNotes.None);
        review.Steps[1].Notes.ShouldBe(StepNotes.OutsideLibrary | StepNotes.NotFound);
        review.Steps[1].Target.ShouldBe(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\SampleTools\Chat\chat.exe"));
        review.Steps[5].Notes.ShouldBe(StepNotes.Closes | StepNotes.ForcesClose);
        review.Steps[5].Target.ShouldBe("samplelauncher.exe");
    }

    [Fact]
    public void ScriptsShortNamesAndWindowsProcesses_BlockTheWholeRoutine()
    {
        var routine = RoutineParser.Parse(RoutineFixtures.Read("runs-a-script.json")).Routine!;

        var review = RoutineReview.Create(routine, _ => null, _ => true, _ => true);

        review.CanRun.ShouldBeFalse();
        review.Steps.Select(s => s.Blocked).ShouldBe(
            [BlockedReason.ScriptHost, BlockedReason.NotAProgram, BlockedReason.ShortName, BlockedReason.ProtectedProcess]);
    }

    [Fact]
    public void ItemsMissingOnThisPc_AreNoted()
    {
        var routine = new Routine("a", new RoutineName("A"), [new LaunchAction(new ItemRef("gog", "1"), null), new CloseAction([new ItemRef("gog", "1")], [])]);

        var review = RoutineReview.Create(routine, _ => null, _ => true, _ => true);

        review.CanRun.ShouldBeTrue();
        review.Steps[0].Notes.ShouldBe(StepNotes.NotInLibrary);
        review.Steps[1].Notes.HasFlag(StepNotes.NotInLibrary).ShouldBeTrue();
    }
}
