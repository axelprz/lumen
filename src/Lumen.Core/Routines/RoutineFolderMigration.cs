// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Core.Routines;

/// <summary>What copying routines from one folder to another did.</summary>
/// <param name="Copied">Routines that were not in the destination and were copied as they are.</param>
/// <param name="KeptBoth">Routines whose id was already used there by a different routine; the copy got a new id.</param>
/// <param name="Skipped">Files that couldn't be copied safely (broken files whose name is taken). Left where they are.</param>
public sealed record RoutineCopyResult(int Copied, int KeptBoth, int Skipped);

/// <summary>
/// Moves routines between Lumen's local folder and the sync folder (PLAN-v1 §4.5) when sync is turned on or off. It
/// only adds: nothing in the destination is changed or deleted, and a routine with the same id but different steps is
/// kept next to the other under a new id instead of overwriting it. Approvals are per id and hash, so a routine
/// copied as it is stays approved and a renamed one needs review again.
/// </summary>
public static class RoutineFolderMigration
{
    public static async Task<RoutineCopyResult> CopyMissingAsync(string from, string to, IFileSystem fileSystem, IUserFiles userFiles, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(userFiles);

        var taken = fileSystem.GetFiles(to, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        int copied = 0, keptBoth = 0, skipped = 0;
        foreach (var source in fileSystem.GetFiles(from, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(source);
            var text = await fileSystem.ReadAllTextAsync(source, ct).ConfigureAwait(false);
            var target = Path.Combine(to, name + ".json");

            if (!taken.Contains(name))
            {
                await userFiles.WriteTextAsync(target, text, ct).ConfigureAwait(false);
                taken.Add(name);
                copied++;
            }
            else if (!SameText(text, await fileSystem.ReadAllTextAsync(target, ct).ConfigureAwait(false)))
            {
                var parsed = RoutineParser.Parse(text);
                if (parsed.Routine is not { } routine)
                {
                    skipped++;
                    continue;
                }

                var id = NewId(routine.Id, taken);
                await userFiles.WriteTextAsync(Path.Combine(to, id + ".json"), RoutineJson.Serialize(routine with { Id = id }), ct).ConfigureAwait(false);
                taken.Add(id);
                keptBoth++;
            }
        }

        return new RoutineCopyResult(copied, keptBoth, skipped);
    }

    private static bool SameText(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);

    private static string NewId(string id, HashSet<string> taken)
    {
        var baseId = id.Length > 60 ? id[..60].TrimEnd('-') : id;
        var candidate = baseId + "-2";
        for (var n = 3; taken.Contains(candidate); n++)
        {
            candidate = $"{baseId}-{n}";
        }

        return candidate;
    }
}
