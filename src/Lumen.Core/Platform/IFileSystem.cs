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

    /// <summary>Reads a whole (small) file, e.g. an icon. Shares the file with whoever has it open.</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct);

    /// <summary>Subfolders (full paths) of <paramref name="directory"/>, non-recursive; empty when it doesn't exist.</summary>
    IReadOnlyList<string> GetDirectories(string directory);
}

/// <summary>The real disk. Only ever reads.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> GetFiles(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly) : [];

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public IReadOnlyList<string> GetDirectories(string directory) =>
        Directory.Exists(directory) ? Directory.GetDirectories(directory) : [];

    public async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        // FileShare.ReadWrite: stores may be writing their own files while we read them.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }
}
