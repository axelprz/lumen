# CLAUDE.md — working on Lumen

Lumen is a game-first launcher for Windows (C# + WPF, .NET 10) that unifies every game store and the user's curated apps, with optional JSON routines.

**Read first:** [docs/PLAN-v1.md](docs/PLAN-v1.md) (scope, decisions, milestones) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (layers, contracts, what is implemented and verified). JSON contracts: [docs/schemas/](docs/schemas/). Anything not in PLAN-v1 is out of scope unless the owner asks.

## Status (update when a milestone or big PR lands)

- **Done:** M0–M8. M5 Steam family uses the website sign-in (2026-10-29). M8: routine engine (#36), Routines page (#39), routines from the tray, quick search and Home (#41), sync folder engine (#42), and sync folder in Settings with routines moving into it.
- **Next:**
  1. **M9:** Velopack updates + release workflow.
  2. **M10:** UI rework (PLAN §4.9).
  3. 1.0 hardening.
  4. **v1.1:** Deals (PLAN; needs owner decisions first).
- **Small leftovers:**
  - "theme accents on/off" setting (§4.7)
  - launch-arguments override
  - GOG Galaxy install URI is still `// VERIFY`
  - Xbox GDK games unverified
  - Hollow Knight shows no Steam data (owner report, never investigated)
- **Owner test notes:** a Steam game ignored Undo and Force close (the tracked process ended, a window stayed); the owner accepted this, and apps close fine.

## How to work

- **Order:** milestone by milestone, in order. One milestone = one or more focused PRs; each PR leaves `main` building and green.
- **PR description:** starts with a checklist from the plan's "Done when" column.
- **Docs follow the code:** if the implementation contradicts ARCHITECTURE.md or a schema, update the doc in the same PR and say why. Record owner decisions (with dates) in the docs.
- **Store facts:** store paths/URIs in ARCHITECTURE.md §4 are **starting points to verify**. When you can't verify on a real machine, write fixture-based tests from documented formats and leave a `// VERIFY:` comment plus a note in the PR.
- **Ask the owner before:**
  - adding a dependency not listed in ARCHITECTURE.md §2
  - a breaking schema change
  - changing scope
  - anything touching the security rules below

  Use the question tool with a recommended option.

### Working with the owner

- **Language:** the owner writes in Spanish (rioplatense, voseo). Reply in Spanish, in plain words. Code, commits, PR text and docs stay in English.
- **After every PR:** give a prioritized manual test guide in Spanish (what to click, what should happen).
- **Merging needs the owner's explicit OK** ("mergealo", "Funciona, mergealo"), never on your own. Then check CI is green and run `gh pr merge <n> --rebase --delete-branch`. `main` is protected (PR + "Build & test (Windows)" check).
- **Owner bug reports:** fix them in the same PR as new commits, and push to the PR branch.
- **Standing demand:** nobody outside the person using Lumen may ever get at their credentials; everything must be secure and transparent.

### Testing on the owner's PC (Windows 11)

- **Never steal focus.** Start test builds with `--tray` (no window). Never trigger the hotkey and never show windows. Stop test processes **by path only** (never by window title: it could kill the owner's Lumen).
- **Build into the scratchpad:** the owner's running Lumen locks `src/Lumen.App/bin`, so use `dotnet build src/Lumen.App -o <scratchpad>/verify-build`. Logs are in `%LOCALAPPDATA%\Lumen\logs`, and Lumen never logs secrets.
- **Single instance:** one Lumen per install folder, so a test build from another folder runs alongside. If the owner's Lumen holds Alt+Shift+Space, a "shortcut refused" warning in the test build is expected.
- **Database migrations:** `MigrationRunner` refuses a database newer than the code. A branch that adds a migration (e.g. 0013) breaks every other branch on the owner's PC once he runs it, so merge it first or rebase the others on it.
- **The owner's account:** never act on it from a test build. For example, revocations happen when he opens the build himself.

### Tooling pitfalls (this machine)

- **Backslashes:** the Bash tool collapses `\\` even inside quoted heredocs, so Windows paths and JSON escapes get mangled. Write such files with the Write tool, or use Python with raw strings or `chr(92)`.
- **resx edits:** do them by script. Keep CRLF, add each key to **both** `Strings.resx` and `Strings.es.resx`, and remove keys from both. A repo test checks that the two match and that every `{l:Loc}` key exists.
- **New views:** every `Views/*.xaml` needs its `.xaml.cs`, otherwise the page renders blank.
- **ListBox text:** a `ListBox` needs `Foreground="{DynamicResource Lumen.Brush.Text}"`, otherwise its text is black on dark.
- **Playnite:** for store behavior, Playnite's source (`JosefNemec/PlayniteExtensions`) is a good, safe reference.

## Commands

```bash
dotnet restore
dotnet build            # whole solution; EnableWindowsTargeting lets it build on Linux too
dotnet test             # Core/Providers/Data tests run on any OS
dotnet run --project src/Lumen.App   # Windows only
```

The WPF app only runs on Windows. Keep logic in the OS-agnostic projects (`Lumen.Core`, `Lumen.Data`, `Lumen.Providers`) and cover it with tests; keep `Lumen.Platform.Windows` and `Lumen.App` thin.

## Conventions

- **Language:** code, identifiers, comments, commit messages, PR text and technical docs are **English**. User-facing text goes in resx (`Strings.resx` = English, `Strings.es.resx` = Spanish, voseo). Never hard-code UI strings; always add both languages.
- **File header:** every source file starts with:
  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
  ```
- **C#:** nullable enabled, warnings as errors, file-scoped namespaces, `async` all the way with `CancellationToken`, no `.Result`/`.Wait()`. Logging uses `[LoggerMessage]`.
- **MVVM:** CommunityToolkit.Mvvm source generators; no logic in code-behind beyond view concerns.
- **Dependencies between projects:** `App → Platform.Windows → Providers → Data → Core`. `Lumen.Core` has no Windows/WPF references.
- **Git:** Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`), scope optional (`feat(steam): ...`). Branches are named `feat/<milestone>-<short-name>` (`fix/…`, `docs/…` for those).
- **Tests:** xUnit + Shouldly; parsing code is always fixture-driven (`tests/fixtures/<area>/...`). Fixtures must be synthetic: never someone's real library, SteamID, tokens or usernames (SteamIDs like `76561190000000001`).
- **Packages:** versions are pinned in `Directory.Packages.props`. Pick versions at least ~2 weeks old. NuGet audit runs with warnings as errors.

## Security rules (non-negotiable)

1. **No secrets in the repo.**
   - API keys (Steam, SteamGridDB) and account tokens (Epic, GOG) are entered by the user and stored with DPAPI (`DataProtectionScope.CurrentUser`).
   - Never log them, never sync them, never put them in URLs that get logged; requests with secrets use a plain `HttpClient` and errors carry status codes only.
   - Sign-in flows follow PLAN-v1 §4.8: no passwords when avoidable, refresh token only, revoke on disconnect, transparent in Settings.
2. **Read-only toward stores.** Never write to a store's files, folders or registry keys. `IFileSystem` only reads; Lumen's own files go through `IUserFiles`, which only writes inside Lumen's folders.
3. **No elevation.** Nothing requires or requests admin; routine programs start without a shell.
4. **No telemetry, no analytics, no remote config.** Network calls only to: Steam Web API, Steam CDN, SteamGridDB, Epic/GOG auth and library endpoints, GitHub Releases. Also Steam's own websites (store/login/help/community), and only inside the built-in Steam sign-in browser.
5. **Routines never run scripts or shell commands in v1.** Untrusted (new/changed) routines require explicit user approval via the preview before running. Blocked programs and protected processes are listed in `RoutineSafety`.
6. **No loading third-party code at runtime** (no plugins, no themes with code) in v1.
7. Providers never throw out of a scan; they return partial results + diagnostics.
8. **Never pose as a device or app Lumen isn't.** No sign-in that claims to be a store's mobile app or copies another device's identity. Store sign-ins only through the store's own web sign-in page, approved by the user:
   - Epic and GOG: the code the user pastes from the browser.
   - Steam: its website in Lumen's built-in browser, as Playnite does.

   The rule comes from the 2026-10-28 incident, when Steam flagged an account after Lumen signed in as its mobile app.

## Definition of done (every PR)

- Builds with zero warnings; tests pass; new logic has tests.
- New UI strings exist in English and Spanish.
- Docs updated if behavior or contracts changed (ARCHITECTURE, schemas, the Status section above).
- No secrets, personal data or real user files in the diff.
