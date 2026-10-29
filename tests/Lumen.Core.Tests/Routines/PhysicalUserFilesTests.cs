// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Core.Tests.Routines;

public sealed class PhysicalUserFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lumen-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task WritesAtomically_InsideItsFolder()
    {
        var files = new PhysicalUserFiles(_root);
        var path = Path.Combine(_root, "routines", "a.json");

        await files.WriteTextAsync(path, "{}", CancellationToken.None);
        await files.WriteTextAsync(path, "{\"x\":1}", CancellationToken.None);

        (await File.ReadAllTextAsync(path)).ShouldBe("{\"x\":1}");
        File.Exists(path + ".tmp").ShouldBeFalse();
        files.Delete(path);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task RefusesAnythingOutsideItsFolder()
    {
        var files = new PhysicalUserFiles(_root);

        await Should.ThrowAsync<UnauthorizedAccessException>(() => files.WriteTextAsync(Path.Combine(_root + "-other", "x.json"), "{}", CancellationToken.None));
        await Should.ThrowAsync<UnauthorizedAccessException>(() => files.WriteTextAsync(Path.Combine(_root, "..", "escape.json"), "{}", CancellationToken.None));
        Should.Throw<UnauthorizedAccessException>(() => files.Delete(Path.Combine(Path.GetTempPath(), "anything.json")));
    }
}
