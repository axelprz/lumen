// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Lumen.Core.Tests.RepositoryChecks;

/// <summary>
/// Guards the rule "every UI string exists in English and Spanish". Reads the resx/XAML files as
/// plain text so it runs on any OS without referencing the WPF project.
/// </summary>
public sealed partial class LocalizationResourceTests
{
    private static readonly string _resourcesDir = Path.Combine(RepoRoot.Find(), "src", "Lumen.App", "Resources");

    [Fact]
    public void EnglishAndSpanish_HaveTheSameKeys()
    {
        var english = LoadStrings("Strings.resx");
        var spanish = LoadStrings("Strings.es.resx");

        english.Keys.Except(spanish.Keys).ShouldBeEmpty("keys missing from Strings.es.resx");
        spanish.Keys.Except(english.Keys).ShouldBeEmpty("keys missing from Strings.resx");
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.es.resx")]
    public void NoValueIsEmpty(string file)
    {
        LoadStrings(file).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ShouldBeEmpty();
    }

    [Fact]
    public void EveryLocKeyUsedInXaml_Exists()
    {
        var keys = LoadStrings("Strings.resx").Keys.ToHashSet(StringComparer.Ordinal);
        var appDir = Path.Combine(RepoRoot.Find(), "src", "Lumen.App");

        var missing = Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => LocUsage().Matches(File.ReadAllText(path))
                .Select(m => (File: Path.GetFileName(path), Key: m.Groups["key"].Value)))
            .Where(usage => !keys.Contains(usage.Key))
            .Select(usage => $"{usage.File}: {usage.Key}")
            .ToList();

        missing.ShouldBeEmpty();
    }

    private static Dictionary<string, string> LoadStrings(string file)
    {
        var doc = XDocument.Load(Path.Combine(_resourcesDir, file));
        return doc.Root!.Elements("data").ToDictionary(
            e => (string)e.Attribute("name")!,
            e => (string?)e.Element("value") ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex(@"\{l:Loc\s+(?:Key=)?(?<key>[A-Za-z0-9_]+)\s*\}")]
    private static partial Regex LocUsage();
}
