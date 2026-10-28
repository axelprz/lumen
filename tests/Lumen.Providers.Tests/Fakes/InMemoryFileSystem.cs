// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.RegularExpressions;
using Lumen.Core.Platform;

namespace Lumen.Providers.Tests.Fakes;

/// <summary>
/// Windows-style in-memory file system (backslashes, case-insensitive) so provider tests behave the same on
/// any OS. Files are added from fixtures under tests/fixtures.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryFileSystem AddFile(string path, string content)
    {
        path = Normalize(path);
        _files[path] = content;
        for (var dir = Parent(path); dir is not null; dir = Parent(dir))
        {
            _directories.Add(dir);
        }

        return this;
    }

    public InMemoryFileSystem AddFixture(string path, string fixture) => AddFile(path, Fixtures.Read(fixture));

    public InMemoryFileSystem AddDirectory(string path)
    {
        for (string? dir = Normalize(path); dir is not null; dir = Parent(dir))
        {
            _directories.Add(dir);
        }

        return this;
    }

    /// <summary>The file exists but reading it throws (locked, permissions).</summary>
    public InMemoryFileSystem MakeUnreadable(string path)
    {
        _unreadable.Add(Normalize(path));
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public IReadOnlyList<string> GetFiles(string directory, string pattern)
    {
        var dir = Normalize(directory);
        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase);
        return _files.Keys
            .Where(f => string.Equals(Parent(f), dir, StringComparison.OrdinalIgnoreCase) && regex.IsMatch(f[(dir.Length + 1)..]))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetDirectories(string directory)
    {
        var dir = Normalize(directory);
        return _directories
            .Where(d => string.Equals(Parent(d), dir, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        path = Normalize(path);
        if (_unreadable.Contains(path))
        {
            throw new IOException("The file is locked.");
        }

        return _files.TryGetValue(path, out var content)
            ? Task.FromResult(content)
            : throw new FileNotFoundException("Not found", path);
    }

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');

    private static string? Parent(string path)
    {
        var i = path.LastIndexOf('\\');
        return i <= 0 ? null : path[..i];
    }
}
