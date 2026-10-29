// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Routines;

namespace Lumen.Core.Tests.Routines;

public sealed class RoutineSafetyTests
{
    [Theory]
    [InlineData(@"C:\Games\Sample\game.exe", BlockedReason.None)]
    [InlineData(@"C:\Windows\System32\cmd.exe", BlockedReason.ScriptHost)]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\POWERSHELL.EXE", BlockedReason.ScriptHost)]
    [InlineData(@"C:\Python\pythonw.exe", BlockedReason.ScriptHost)]
    [InlineData(@"C:\Games\..\Windows\System32\mshta.exe", BlockedReason.ScriptHost)]
    [InlineData(@"C:\Tools\setup.bat", BlockedReason.NotAProgram)]
    [InlineData(@"C:\Tools\link.lnk", BlockedReason.NotAProgram)]
    [InlineData(@"C:\Tools\run.ps1", BlockedReason.NotAProgram)]
    [InlineData(@"C:\PROGRA~1\Tools\POWERS~1.EXE", BlockedReason.ShortName)]
    [InlineData(@"game.exe", BlockedReason.NotAbsolute)]
    [InlineData(@"\\?\C:\Windows\System32\cmd.exe", BlockedReason.NotAbsolute)]
    public void CheckProgram(string path, BlockedReason expected) => RoutineSafety.CheckProgram(path).ShouldBe(expected);

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("EXPLORER.EXE")]
    [InlineData("lumen.exe")]
    [InlineData("svchost.exe")]
    public void ProtectedProcesses(string name) => RoutineSafety.IsProtectedProcess(name).ShouldBeTrue();

    [Fact]
    public void OrdinaryPrograms_AreNotProtected() => RoutineSafety.IsProtectedProcess("discord.exe").ShouldBeFalse();

    [Theory]
    [InlineData("Cmd.Exe")]
    [InlineData("PowerShell.EXE.MUI")]
    [InlineData(" wscript.exe ")]
    public void VersionInfoNames_AreRecognized(string original) => RoutineSafety.IsBlockedProgramName(original).ShouldBeTrue();

    [Fact]
    public void Expand_FillsVariablesThenEnvironment()
    {
        var variables = new Dictionary<string, string> { ["dir"] = @"%SystemRoot%\Temp" };

        var expanded = RoutineSafety.Expand(@"{dir}\{unknown}\x.exe", variables);

        expanded.ShouldBe(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp") + @"\{unknown}\x.exe");
    }

    [Fact]
    public void VariableHidingAShell_IsStillBlocked()
    {
        var routine = new Routine("a", new RoutineName("A"), [new LaunchAction(null, @"{shell}")])
        {
            Variables = new Dictionary<string, string> { ["shell"] = @"C:\Windows\System32\cmd.exe" },
        };

        var review = RoutineReview.Create(routine, _ => null, _ => true, _ => true);

        review.CanRun.ShouldBeFalse();
        review.Steps[0].Blocked.ShouldBe(BlockedReason.ScriptHost);
    }

    [Fact]
    public void LibraryAppThatIsAShell_IsBlocked()
    {
        var entry = new LibraryEntry(
            new ItemRef("startmenu", "Windows PowerShell"), ItemKind.App, "Windows PowerShell", InstallState.Installed, null,
            new LaunchSpec(LaunchKind.Executable, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"), null, LibraryEntry.NoExtra);
        var routine = new Routine("a", new RoutineName("A"), [new LaunchAction(entry.Ref, null)]);

        var review = RoutineReview.Create(routine, _ => new LibraryItem([entry], entry), _ => true, _ => true);

        review.Steps[0].Blocked.ShouldBe(BlockedReason.ScriptHost);
    }
}
