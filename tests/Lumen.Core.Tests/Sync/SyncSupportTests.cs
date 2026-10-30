// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;
using Lumen.Core.Routines;
using Lumen.Core.Sync;

namespace Lumen.Core.Tests.Sync;

public sealed class SyncSupportTests
{
    [Theory]
    [InlineData(@"C:\Users\axel\OneDrive\Lumen", SyncFolderProblem.None)]
    [InlineData(@"D:\Sync", SyncFolderProblem.None)]
    [InlineData(@"\\nas\media\lumen", SyncFolderProblem.None)]
    [InlineData(@"C:\", SyncFolderProblem.DriveRoot)]
    [InlineData(@"D:", SyncFolderProblem.NotAbsolute)]
    [InlineData(@"\\nas\media", SyncFolderProblem.DriveRoot)]
    [InlineData(@"lumen\sync", SyncFolderProblem.NotAbsolute)]
    [InlineData("", SyncFolderProblem.NotAbsolute)]
    [InlineData(@"C:\Windows", SyncFolderProblem.SystemFolder)]
    [InlineData(@"c:\windows\system32", SyncFolderProblem.SystemFolder)]
    [InlineData(@"C:\Program Files\Lumen", SyncFolderProblem.SystemFolder)]
    [InlineData(@"C:\Program Files (x86)", SyncFolderProblem.SystemFolder)]
    [InlineData(@"C:\ProgramData\x", SyncFolderProblem.SystemFolder)]
    [InlineData(@"C:\Users\axel\Windows stuff", SyncFolderProblem.None)]
    public void Rules_AllowOrdinaryFoldersOnly(string path, SyncFolderProblem expected) => SyncFolderRules.Check(path).ShouldBe(expected);

    [Fact]
    public async Task UserFiles_WriteOnlyInAllowedFolders_AndFoldersCanBeAddedAndForgotten()
    {
        var home = Path.Combine(Path.GetTempPath(), "lumen-uf-" + Guid.NewGuid().ToString("N"));
        var own = Path.Combine(home, "own");
        var sync = Path.Combine(home, "sync");
        Directory.CreateDirectory(own);
        try
        {
            var files = new PhysicalUserFiles(own);
            await Should.ThrowAsync<UnauthorizedAccessException>(() => files.WriteTextAsync(Path.Combine(sync, "config.json"), "{}", CancellationToken.None));

            files.AllowFolder(sync);
            files.AllowFolder(sync + Path.DirectorySeparatorChar); // same folder twice
            await files.WriteTextAsync(Path.Combine(sync, "collections", "a.json"), "{}", CancellationToken.None);
            File.Exists(Path.Combine(sync, "collections", "a.json")).ShouldBeTrue();
            await Should.ThrowAsync<UnauthorizedAccessException>(() => files.WriteTextAsync(Path.Combine(home, "sync-other", "x.json"), "{}", CancellationToken.None));

            files.ForgetFolder(sync);
            await Should.ThrowAsync<UnauthorizedAccessException>(() => files.WriteTextAsync(Path.Combine(sync, "config.json"), "{}", CancellationToken.None));
            await files.WriteTextAsync(Path.Combine(own, "still.json"), "{}", CancellationToken.None);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private const string Work = """{ "schema": 1, "id": "work", "name": "Work", "actions": [ { "type": "url", "url": "https://example.com/" } ] }""";
    private const string WorkOther = """{ "schema": 1, "id": "work", "name": "Work (other PC)", "actions": [ { "type": "url", "url": "https://example.org/" } ] }""";

    private static string Id(string path) => Path.GetFileNameWithoutExtension(path);

    [Fact]
    public async Task Routines_AreCopiedWhenMissing_AndNothingExistingIsTouched()
    {
        var disk = new MemoryFolder();
        disk.Text["/local/work.json"] = Work;
        disk.Text["/local/play.json"] = Work.Replace("\"work\"", "\"play\"", StringComparison.Ordinal);
        disk.Text["/cloud/other.json"] = Work.Replace("\"work\"", "\"other\"", StringComparison.Ordinal);

        var result = await RoutineFolderMigration.CopyMissingAsync("/local", "/cloud", disk, disk, CancellationToken.None);

        result.ShouldBe(new RoutineCopyResult(2, 0, 0));
        disk.Text.Keys.Where(k => k.StartsWith("/cloud/", StringComparison.Ordinal)).Select(Id).Order().ShouldBe(["other", "play", "work"]);
        disk.Text["/cloud/work.json"].ShouldBe(Work); // copied byte for byte, so it stays approved
    }

    [Fact]
    public async Task SameIdDifferentSteps_KeepsBoth_UnderANewId()
    {
        var disk = new MemoryFolder();
        disk.Text["/local/work.json"] = Work;
        disk.Text["/cloud/work.json"] = WorkOther;

        var result = await RoutineFolderMigration.CopyMissingAsync("/local", "/cloud", disk, disk, CancellationToken.None);

        result.ShouldBe(new RoutineCopyResult(0, 1, 0));
        disk.Text["/cloud/work.json"].ShouldBe(WorkOther);
        var renamed = RoutineParser.Parse(disk.Text["/cloud/work-2.json"]);
        renamed.IsValid.ShouldBeTrue();
        renamed.Routine!.Id.ShouldBe("work-2");
        renamed.Routine.Actions.Single().ShouldBeOfType<UrlAction>().Url.ShouldBe("https://example.com/");
    }

    [Fact]
    public async Task SameRoutineOnBothSides_IsLeftAlone_AndABrokenClashIsSkipped()
    {
        var disk = new MemoryFolder();
        disk.Text["/local/work.json"] = Work + "\n";
        disk.Text["/cloud/work.json"] = Work;
        disk.Text["/local/bad.json"] = "{ not json";
        disk.Text["/cloud/bad.json"] = "{ other broken";

        var result = await RoutineFolderMigration.CopyMissingAsync("/local", "/cloud", disk, disk, CancellationToken.None);

        result.ShouldBe(new RoutineCopyResult(0, 0, 1));
        disk.Text.Keys.Count(k => k.StartsWith("/cloud/", StringComparison.Ordinal)).ShouldBe(2);
        disk.Text["/cloud/bad.json"].ShouldBe("{ other broken");
    }

    [Fact]
    public async Task ManyClashes_GetFreshIds()
    {
        var disk = new MemoryFolder();
        disk.Text["/local/work.json"] = Work;
        disk.Text["/cloud/work.json"] = WorkOther;
        disk.Text["/cloud/work-2.json"] = WorkOther.Replace("\"work\"", "\"work-2\"", StringComparison.Ordinal);

        await RoutineFolderMigration.CopyMissingAsync("/local", "/cloud", disk, disk, CancellationToken.None);

        disk.Text.ContainsKey("/cloud/work-3.json").ShouldBeTrue();
    }
}
