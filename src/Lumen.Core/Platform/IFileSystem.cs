// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>Read-only file access for providers, so tests can feed fixture files from memory.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>File names (full paths) in <paramref name="directory"/> matching <paramref name="pattern"/>, non-recursive.</summary>
    IReadOnlyList<string> GetFiles(string directory, string pattern);

    Task<string> ReadAllTextAsync(string path, CancellationToken ct);
}

/// <summary>The real disk. Only ever reads.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> GetFiles(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly) : [];

    public async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        // FileShare.ReadWrite: stores may be writing their own files while we read them.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }
}
