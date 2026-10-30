// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>
/// Writes Lumen's own files (routines now, the sync folder later). <see cref="IFileSystem"/> stays read-only for
/// stores (CLAUDE.md rule 2); this one only writes inside the folders it was given, plus a file the user picked in a
/// save dialog.
/// </summary>
public interface IUserFiles
{
    /// <summary>Writes the whole file atomically (temp file, then rename), inside an allowed folder.</summary>
    Task WriteTextAsync(string path, string text, CancellationToken ct);

    /// <summary>Writes a file the user chose in a save dialog (export).</summary>
    Task WriteChosenFileAsync(string path, string text, CancellationToken ct);

    /// <summary>Deletes a file inside an allowed folder; nothing happens when it's gone already.</summary>
    void Delete(string path);
}

/// <summary>The real disk, limited to <paramref name="allowedRoots"/>.</summary>
public sealed class PhysicalUserFiles(params string[] allowedRoots) : IUserFiles
{
    private readonly object _lock = new();
    private volatile string[] _roots = allowedRoots.Select(Root).ToArray();

    /// <summary>
    /// Also allows writing inside <paramref name="folder"/>: the sync folder the user chose. Never called for anything
    /// the user didn't pick, and the callers only write the files Lumen owns there.
    /// </summary>
    public void AllowFolder(string folder)
    {
        lock (_lock)
        {
            var root = Root(folder);
            if (!_roots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                _roots = [.. _roots, root];
            }
        }
    }

    /// <summary>Stops allowing a folder added with <see cref="AllowFolder"/>.</summary>
    public void ForgetFolder(string folder)
    {
        lock (_lock)
        {
            var root = Root(folder);
            _roots = _roots.Where(r => !string.Equals(r, root, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    private static string Root(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

    public Task WriteTextAsync(string path, string text, CancellationToken ct) => WriteAsync(Allowed(path), text, ct);

    public Task WriteChosenFileAsync(string path, string text, CancellationToken ct) => WriteAsync(Path.GetFullPath(path), text, ct);

    public void Delete(string path)
    {
        var full = Allowed(path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    private static async Task WriteAsync(string full, string text, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp";
        await File.WriteAllTextAsync(temp, text, ct).ConfigureAwait(false);
        File.Move(temp, full, overwrite: true);
    }

    private string Allowed(string path)
    {
        var full = Path.GetFullPath(path);
        if (!_roots.Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException("Lumen only writes inside its own folders.");
        }

        return full;
    }
}
