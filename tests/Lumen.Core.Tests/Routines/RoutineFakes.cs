// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Core.Tests.Routines;

internal static class RoutineFixtures
{
    /// <summary>Reads tests/fixtures/routines/{name} (copied next to the test assembly).</summary>
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "routines", name));
}

internal sealed class FakeLauncher : ILauncher
{
    public List<LaunchSpec> Started { get; } = [];

    public Task<int?> StartAsync(LaunchSpec spec, CancellationToken ct)
    {
        Started.Add(spec);
        return Task.FromResult<int?>(null);
    }
}

internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.Contains(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public IReadOnlyList<string> GetFiles(string directory, string pattern) => [];

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct) => throw new FileNotFoundException(path);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => throw new FileNotFoundException(path);

    public IReadOnlyList<string> GetDirectories(string directory) => [];
}

/// <summary>Processes in memory. Polite close works unless the process is marked stubborn.</summary>
internal sealed class FakeProcessControl : IProcessControl
{
    private int _nextId = 1000;

    public List<RunningProcess> Running { get; } = [];

    public HashSet<int> Stubborn { get; } = [];

    public List<(string Path, string? Args, string? WorkingDirectory)> StartedPrograms { get; } = [];

    public List<int> Killed { get; } = [];

    public List<int> AskedToClose { get; } = [];

    public Dictionary<string, string> OriginalNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public RunningProcess Add(string path, DateTimeOffset? start = null)
    {
        var process = new RunningProcess(_nextId++, start ?? DateTimeOffset.UtcNow, Path.GetFileName(path), path);
        Running.Add(process);
        return process;
    }

    public RunningProcess Start(string path, string? arguments, string? workingDirectory)
    {
        StartedPrograms.Add((path, arguments, workingDirectory));
        return Add(path);
    }

    public IReadOnlyList<RunningProcess> FindByName(string imageName) =>
        Running.Where(p => string.Equals(p.Name, imageName, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<RunningProcess> FindInFolder(string folder) =>
        Running.Where(p => p.ImagePath?.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) == true).ToList();

    public bool IsRunning(RunningProcess process) => Running.Contains(process);

    public bool RequestClose(RunningProcess process)
    {
        AskedToClose.Add(process.Id);
        if (!Stubborn.Contains(process.Id))
        {
            Running.Remove(process);
        }

        return true;
    }

    public bool Kill(RunningProcess process)
    {
        Killed.Add(process.Id);
        return Running.Remove(process);
    }

    public string? OriginalFileName(string path) => OriginalNames.GetValueOrDefault(path);
}
