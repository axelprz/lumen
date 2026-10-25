// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Reflection;
using System.Text.RegularExpressions;

namespace Lumen.Data.Migrations;

public static partial class MigrationLoader
{
    private const string ResourcePrefix = "Lumen.Data.Migrations.";

    /// <summary>Loads the migrations embedded in Lumen.Data, ordered by version.</summary>
    public static IReadOnlyList<Migration> LoadEmbedded()
    {
        var assembly = typeof(MigrationLoader).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => (Name: name[ResourcePrefix.Length..], Sql: ReadResource(assembly, name)));
        return Parse(resources);
    }

    /// <summary>
    /// Validates and orders migrations given as (file name, SQL) pairs. File names must look like
    /// <c>0001_initial.sql</c> and versions must be contiguous starting at 1.
    /// </summary>
    public static IReadOnlyList<Migration> Parse(IEnumerable<(string FileName, string Sql)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var migrations = new List<Migration>();
        foreach (var (fileName, sql) in files)
        {
            var match = FileNamePattern().Match(fileName);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Migration file '{fileName}' does not match 'NNNN_lower_snake_name.sql'.");
            }

            migrations.Add(new Migration(
                int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["name"].Value,
                sql));
        }

        migrations.Sort((a, b) => a.Version.CompareTo(b.Version));
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
            {
                throw new InvalidOperationException(
                    $"Migration versions must be contiguous from 1; expected {i + 1}, found {migrations[i].Version}.");
            }
        }

        return migrations;
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded migration '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^(?<version>\d{4})_(?<name>[a-z0-9_]+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}
