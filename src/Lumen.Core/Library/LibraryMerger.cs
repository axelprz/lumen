// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>
/// Groups entries into <see cref="LibraryItem"/>s. Games with the same normalized title from different
/// stores become one item; apps are never merged automatically; entries the user separated stay alone; entries
/// the user merged by hand join the item of the entry they point to. Pure: same input, same output.
/// </summary>
public sealed class LibraryMerger(TitleNormalizer normalizer)
{
    /// <summary>Which store Play uses when several have the game installed and the user has no preference.</summary>
    public static IReadOnlyList<string> SourcePriority { get; } = ["steam", "epic", "gog", "xbox", "ubisoft", "ea", "battlenet", "manual"];

    public IReadOnlyList<LibraryItem> Merge(IReadOnlyList<LibraryEntry> entries, MergeRules rules)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rules);

        var groups = new List<List<LibraryEntry>>();
        var mergeable = new Dictionary<string, List<List<LibraryEntry>>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Kind != ItemKind.Game || rules.Separated.Contains(entry.Ref))
            {
                groups.Add([entry]);
                continue;
            }

            var key = normalizer.Normalize(entry.Title);
            if (key.Length == 0)
            {
                groups.Add([entry]);
                continue;
            }

            if (!mergeable.TryGetValue(key, out var candidates))
            {
                candidates = [];
                mergeable[key] = candidates;
            }

            // One entry per store in a group: the same title twice in one store means two different products.
            var target = candidates.FirstOrDefault(g => g.All(e => e.Ref.Source != entry.Ref.Source));
            if (target is null)
            {
                target = [];
                candidates.Add(target);
                groups.Add(target);
            }

            target.Add(entry);
        }

        return ApplyLinks(groups, rules.Links).Select(g => ToItem(g, rules)).ToList();
    }

    /// <summary>Joins groups linked by manual merges (transitively). Links to entries that are gone are ignored.</summary>
    private static List<List<LibraryEntry>> ApplyLinks(List<List<LibraryEntry>> groups, IReadOnlyDictionary<ItemRef, ItemRef> links)
    {
        if (links.Count == 0)
        {
            return groups;
        }

        var groupOf = new Dictionary<ItemRef, int>();
        for (var i = 0; i < groups.Count; i++)
        {
            foreach (var entry in groups[i])
            {
                groupOf.TryAdd(entry.Ref, i);
            }
        }

        var parent = Enumerable.Range(0, groups.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        foreach (var (from, to) in links)
        {
            if (groupOf.TryGetValue(from, out var a) && groupOf.TryGetValue(to, out var b))
            {
                var (rootA, rootB) = (Find(a), Find(b));
                if (rootA != rootB)
                {
                    // The earlier group absorbs the later one, so the result doesn't depend on link order.
                    parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
                }
            }
        }

        var joined = new Dictionary<int, List<LibraryEntry>>();
        var result = new List<List<LibraryEntry>>();
        for (var i = 0; i < groups.Count; i++)
        {
            var root = Find(i);
            if (!joined.TryGetValue(root, out var target))
            {
                target = [];
                joined[root] = target;
                result.Add(target);
            }

            target.AddRange(groups[i]);
        }

        return result;
    }

    /// <summary>
    /// The entry Play/Install uses: the user's preferred one if it can run, else the first installed one by
    /// <see cref="SourcePriority"/>, else the first that can at least be installed.
    /// </summary>
    public static LibraryEntry ChoosePrimary(IReadOnlyList<LibraryEntry> entries, MergeRules rules)
    {
        var ordered = entries.OrderBy(e => Priority(e.Ref.Source)).ThenBy(e => e.Ref.ToString(), StringComparer.Ordinal).ToList();
        return ordered.FirstOrDefault(e => rules.Preferred.Contains(e.Ref) && LaunchService.PrimaryAction(e) is not null)
            ?? ordered.FirstOrDefault(e => e.IsInstalled && e.Launch is not null)
            ?? ordered.FirstOrDefault(e => LaunchService.PrimaryAction(e) is not null)
            ?? ordered[0];
    }

    private static LibraryItem ToItem(List<LibraryEntry> group, MergeRules rules)
    {
        var ordered = group.OrderBy(e => Priority(e.Ref.Source)).ToList();
        return new LibraryItem(ordered, ChoosePrimary(ordered, rules));
    }

    private static readonly Dictionary<string, int> _priority =
        SourcePriority.Select((source, index) => (source, index)).ToDictionary(x => x.source, x => x.index, StringComparer.Ordinal);

    private static int Priority(string source) => _priority.GetValueOrDefault(source, int.MaxValue);
}
