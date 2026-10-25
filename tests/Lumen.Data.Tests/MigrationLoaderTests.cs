// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Data.Migrations;

namespace Lumen.Data.Tests;

public sealed class MigrationLoaderTests
{
    [Fact]
    public void EmbeddedMigrations_AreContiguousFromOne()
    {
        var migrations = MigrationLoader.LoadEmbedded();

        migrations.ShouldNotBeEmpty();
        migrations.Select(m => m.Version).ShouldBe(Enumerable.Range(1, migrations.Count));
        migrations[0].Name.ShouldBe("initial");
        migrations.ShouldAllBe(m => !string.IsNullOrWhiteSpace(m.Sql));
    }

    [Fact]
    public void Parse_SortsByVersion()
    {
        var parsed = MigrationLoader.Parse([("0002_b.sql", "B"), ("0001_a.sql", "A")]);

        parsed.Select(m => (m.Version, m.Name, m.Sql)).ShouldBe([(1, "a", "A"), (2, "b", "B")]);
    }

    [Theory]
    [InlineData("1_short.sql")]
    [InlineData("0001-dash.sql")]
    [InlineData("0001_Upper.sql")]
    [InlineData("0001_initial.txt")]
    public void Parse_RejectsBadFileNames(string fileName)
    {
        Should.Throw<InvalidOperationException>(() => MigrationLoader.Parse([(fileName, "SELECT 1;")]));
    }

    [Fact]
    public void Parse_RejectsGaps()
    {
        Should.Throw<InvalidOperationException>(
            () => MigrationLoader.Parse([("0001_a.sql", "A"), ("0003_c.sql", "C")]));
    }

    [Fact]
    public void Parse_RejectsDuplicates()
    {
        Should.Throw<InvalidOperationException>(
            () => MigrationLoader.Parse([("0001_a.sql", "A"), ("0001_b.sql", "B")]));
    }
}
