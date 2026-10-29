# CLAUDE.md — working on Lumen

Lumen is a game-first launcher for Windows (C# + WPF, .NET 10) that unifies every game store and the user's curated apps, with optional JSON routines.

**Read first:** [docs/PLAN-v1.md](docs/PLAN-v1.md) (scope, decisions, milestones) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (layers, contracts, store starting points). JSON contracts: [docs/schemas/](docs/schemas/). Anything not in PLAN-v1 is out of scope unless the owner asks.

## How to work

- Work milestone by milestone (M0 → M10), in order. One milestone = one or more focused PRs; each PR leaves `main` building and green.
- Before starting a milestone, write a short checklist in the PR description from the plan's "Done when" column.
- If the implementation contradicts ARCHITECTURE.md or a schema, update the doc in the same PR and say why.
- Store-specific paths/URIs in ARCHITECTURE.md §4 are **starting points to verify**, not facts. When you can't verify on a real machine, write fixture-based tests from documented formats and leave a `// VERIFY:` comment plus a note in the PR.
- Ask the owner before: adding a dependency not listed in ARCHITECTURE.md §2, changing a schema in a breaking way, changing scope, or anything touching security rules below.

## Commands

```bash
dotnet restore
dotnet build            # whole solution; EnableWindowsTargeting lets it build on Linux too
dotnet test             # Core/Providers/Data tests run on any OS
dotnet run --project src/Lumen.App   # Windows only
```

The WPF app only runs on Windows. When working from a non-Windows environment, keep logic in the OS-agnostic projects (`Lumen.Core`, `Lumen.Data`, `Lumen.Providers`) and cover it with tests; keep `Lumen.Platform.Windows` and `Lumen.App` thin.

## Conventions

- Code, identifiers, comments, commit messages, PR text, technical docs: **English**. User-facing text: resx (`Strings.resx` = English, `Strings.es.resx` = Spanish) — never hard-code UI strings; always add both languages.
- Every source file starts with:
  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
  ```
- Nullable enabled, warnings as errors, file-scoped namespaces, `async` all the way with `CancellationToken`, no `.Result`/`.Wait()`.
- MVVM with CommunityToolkit.Mvvm source generators; no logic in code-behind beyond view concerns.
- Dependency direction: `App → Platform.Windows → Providers → Data → Core`. `Lumen.Core` has no Windows/WPF references.
- Commits: Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`), scope optional (`feat(steam): ...`).
- Branches: `feat/<milestone>-<short-name>`, e.g. `feat/m1-steam-provider`.
- Tests: xUnit; parsing code always fixture-driven (`tests/fixtures/<store>/...`). Fixtures must be synthetic or anonymized — never commit someone's real library, SteamID, tokens or usernames.

## Security rules (non-negotiable)

1. **No secrets in the repo.** API keys (Steam, SteamGridDB) and account tokens (Epic, GOG, Steam sign-in) are entered by the user and stored with DPAPI (`DataProtectionScope.CurrentUser`). Never log them, never sync them, never put them in URLs that get logged. Sign-in flows follow PLAN-v1 §4.8 (no passwords when avoidable, refresh token only, revoke on disconnect, transparent in Settings).
2. **Read-only toward stores.** Never write to a store's files, folders or registry keys.
3. **No elevation.** Nothing requires or requests admin.
4. **No telemetry, no analytics, no remote config.** Network calls only to: Steam Web API, Steam CDN, SteamGridDB, Epic/GOG auth and library endpoints, GitHub Releases.
5. **Routines never run scripts or shell commands in v1.** Untrusted (new/changed) routines require explicit user approval via the preview before running.
6. **No loading third-party code at runtime** (no plugins, no themes with code) in v1.
7. Providers never throw out of a scan; they return partial results + diagnostics.
8. **Never pose as a device or app Lumen isn't.** No sign-in that claims to be a store's mobile app, or that copies another device's identity. Store sign-ins only through the store's own web sign-in page, approved by the user (Epic, GOG). **Steam has no sign-in at all**: only the user's Web API key and public profile data. After the 2026-10-28 incident, Steam flagged an account when Lumen signed in as its mobile app.

## Definition of done (every PR)

- Builds with zero warnings; tests pass; new logic has tests.
- New UI strings exist in English and Spanish.
- Docs updated if behavior or contracts changed.
- No secrets, personal data or real user files in the diff.
