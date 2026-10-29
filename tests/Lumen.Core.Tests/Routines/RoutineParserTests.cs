// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Library;
using Lumen.Core.Routines;

namespace Lumen.Core.Tests.Routines;

public sealed class RoutineParserTests
{
    [Fact]
    public void ReadsEveryActionType()
    {
        var result = RoutineParser.Parse(RoutineFixtures.Read("game-night.json"));

        result.Errors.ShouldBeEmpty();
        var routine = result.Routine!;
        routine.Id.ShouldBe("game-night");
        routine.Name.For(CultureInfo.GetCultureInfo("es-AR")).ShouldBe("Noche de juegos");
        routine.Name.For(CultureInfo.GetCultureInfo("fr-FR")).ShouldBe("Game night");
        routine.Pinned.ShouldBeTrue();
        routine.Variables["tools"].ShouldBe(@"%LOCALAPPDATA%\SampleTools");

        routine.Actions[0].ShouldBe(new LaunchAction(new ItemRef("steam", "400"), null));
        routine.Actions[1].ShouldBeOfType<LaunchAction>().Arguments.ShouldBe("--start-minimized");
        routine.Actions[2].ShouldBe(new WaitAction(2000));
        routine.Actions[3].ShouldBe(new UrlAction("https://example.com/stream"));
        routine.Actions[4].ShouldBe(new FolderAction(@"%USERPROFILE%\Videos"));
        var close = routine.Actions[5].ShouldBeOfType<CloseAction>();
        close.Processes.ShouldBe(["samplelauncher.exe"]);
        close.GraceMs.ShouldBe(3000);
        close.Force.ShouldBeTrue();
        close.Label.ShouldBe("Close the launcher");
    }

    [Fact]
    public void BrokenFile_ListsEveryProblem_AndIsNeverRunnable()
    {
        var result = RoutineParser.Parse(RoutineFixtures.Read("broken.json"));

        result.IsValid.ShouldBeFalse();
        result.Hash.ShouldBeNull();
        var errors = string.Join("\n", result.Errors);
        errors.ShouldContain("unknown property 'extra'");
        errors.ShouldContain("$.id");
        errors.ShouldContain("$.name");
        errors.ShouldContain("exactly one of \"ref\"");
        errors.ShouldContain("http:// or https://");
        errors.ShouldContain("$.actions[2].ms");
        errors.ShouldContain("needs \"refs\" and/or \"processes\"");
        errors.ShouldContain("unknown action 'teleport'");
    }

    [Theory]
    [InlineData("{ \"schema\": 1, \"schema\": 1, \"id\": \"a\", \"name\": \"A\", \"actions\": [{ \"type\": \"wait\", \"ms\": 1 }] }", "appears twice")]
    [InlineData("{ \"schema\": 2, \"id\": \"a\", \"name\": \"A\", \"actions\": [{ \"type\": \"wait\", \"ms\": 1 }] }", "$.schema")]
    [InlineData("{ \"schema\": 1, \"id\": \"a\", \"name\": \"A\", \"actions\": [] }", "1–50 steps")]
    [InlineData("{ \"schema\": 1, \"id\": \"a\", \"name\": \"A\", \"actions\": [{ \"type\": \"launch\", \"ref\": \"steam:1\", \"args\": \"-x\" }] }", "only go with \"path\"")]
    [InlineData("{ \"schema\": 1, \"id\": \"a\", \"name\": \"A\", \"actions\": [{ \"type\": \"close\", \"processes\": [\"C:\\\\x\\\\a.exe\"] }] }", "program name")]
    [InlineData("{ \"schema\": 1, \"id\": \"a\", \"name\": \"A\", // comment\n \"actions\": [] }", "Not valid JSON")]
    [InlineData("[1, 2]", "must be an object")]
    public void Rejects(string json, string expected) =>
        string.Join("\n", RoutineParser.Parse(json).Errors).ShouldContain(expected);

    [Fact]
    public void HugeFile_IsRefusedBeforeParsing() =>
        RoutineParser.Parse(new string(' ', RoutineParser.MaxBytes + 1)).Errors.ShouldBe(["The file is larger than 256 KB."]);
}
