// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Routines;

namespace Lumen.Core.Tests.Routines;

public sealed class RoutineJsonTests
{
    [Fact]
    public void Hash_IgnoresFormattingAndKeyOrder()
    {
        var original = RoutineParser.Parse(RoutineFixtures.Read("game-night.json"));
        var reformatted = RoutineParser.Parse(RoutineFixtures.Read("same-meaning-reformatted.json"));

        original.Hash.ShouldNotBeNull();
        original.Hash.Length.ShouldBe(64);
        reformatted.Hash.ShouldBe(original.Hash);
    }

    [Fact]
    public void Hash_ChangesWithAnyChangeOfMeaning()
    {
        var json = RoutineFixtures.Read("game-night.json");
        var changed = json.Replace("samplelauncher.exe", "otherlauncher.exe", StringComparison.Ordinal);

        RoutineParser.Parse(changed).Hash.ShouldNotBe(RoutineParser.Parse(json).Hash);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var routine = RoutineParser.Parse(RoutineFixtures.Read("game-night.json")).Routine!;

        var again = RoutineParser.Parse(RoutineJson.Serialize(routine));

        again.Errors.ShouldBeEmpty();
        RoutineJson.Serialize(again.Routine!).ShouldBe(RoutineJson.Serialize(routine));
        again.Routine!.Variables.ShouldBe(routine.Variables);
        again.Routine.Name.For(System.Globalization.CultureInfo.GetCultureInfo("es")).ShouldBe("Noche de juegos");
        // Defaults are left out when writing, so the hash is stable from the first save on.
        RoutineParser.Parse(RoutineJson.Serialize(again.Routine!)).Hash.ShouldBe(again.Hash);
    }
}
