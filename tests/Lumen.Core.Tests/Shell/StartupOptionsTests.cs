// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Shell;
using Shouldly;
using Xunit;

namespace Lumen.Core.Tests.Shell;

public sealed class StartupOptionsTests
{
    [Fact]
    public void NoArguments_OpensTheWindow() => StartupOptions.Parse([]).StartInTray.ShouldBeFalse();

    [Theory]
    [InlineData("--tray")]
    [InlineData("--TRAY")]
    [InlineData(" --tray ")]
    public void TrayArgument_StartsInTheTray(string arg) => StartupOptions.Parse(["--other", arg]).StartInTray.ShouldBeTrue();

    [Fact]
    public void UnknownArguments_AreIgnored() => StartupOptions.Parse(["tray", "-tray", "/tray"]).StartInTray.ShouldBeFalse();

    [Fact]
    public void StartWithWindowsCommand_QuotesThePath() =>
        StartupOptions.StartWithWindowsCommand(@"C:\Program Files\Lumen\Lumen.exe").ShouldBe("\"C:\\Program Files\\Lumen\\Lumen.exe\" --tray");

    [Fact]
    public void StartWithWindowsCommand_RejectsQuotes() =>
        Should.Throw<ArgumentException>(() => StartupOptions.StartWithWindowsCommand("C:\\a\" --evil \"b.exe"));
}
