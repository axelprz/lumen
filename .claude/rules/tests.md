---
paths:
  - "tests/**"
---

# Test rules

- xUnit + Shouldly. Parsing is fixture-driven (`tests/fixtures/<area>/…`); fixtures are synthetic (SteamIDs like `76561190000000001`, never real libraries, tokens or names).
- Timing tests need generous timeouts. A test for a race should fail without the fix (check it).
- `tests/Lumen.Core.Tests/RepositoryChecks` read source as text (localization, font, type scale, motion, surfaces, menus, licenses): a failing one means a rule was skipped, fix the code, not the test.
- Cloud sandbox baseline: 26 failures in `Lumen.Core.Tests` (`RoutineSafety/Runner/Review/Service` tests with `C:\` paths); they pass on Windows CI. Any other failure is yours.
- Unverified store facts: fixture test + `// VERIFY:` comment + a PR note.
