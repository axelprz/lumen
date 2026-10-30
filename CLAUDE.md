# CLAUDE.md — working on Lumen

Lumen is a game-first launcher for Windows (C# + WPF, .NET 10) that unifies every game store and the user's curated apps, with optional JSON routines.

**Read first, in this order:**
1. This file (rules, status, pitfalls).
2. [docs/PLAN-v1.md](docs/PLAN-v1.md): scope, fixed decisions, milestones, and the **decision log** (§7). Anything not in the plan is out of scope unless the owner asks.
3. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): layers, contracts, what is implemented and verified. Start with its "Where to find things" table. JSON contracts are in [docs/schemas/](docs/schemas/) with examples in `docs/examples/`.

## Status (update when a milestone or a big PR lands)

- **Done:** M0–M9 (2026-10-30). M5 Steam family uses the website sign-in (2026-10-29). M8: routine engine (#36), Routines page (#39), routines from tray/quick search/Home (#41), sync folder engine (#42), sync folder in Settings (#43). M9: Velopack updates + release workflow (#44); the owner tagged the first pre-releases (`v0.1.0-beta.*`) and reported the release/update flow ready (2026-10-30).
- **Next, in order:**
  1. **M10 UI rework (PLAN §4.9).** First the owner picks the font from a sample (Inter / Outfit / Exo 2 / Rajdhani): prepare the sample, then plan the PRs. Keep scroll performance, contrast rules, visible keyboard/gamepad focus, motion ≤ 250 ms respecting "Animation effects".
  2. **1.0 hardening:** bug bash on real machines with every store installed, screenshots of the reworked UI, tag `v1.0.0` (the default update channel flips to stable by itself at 1.0).
  3. **v1.1 Deals** (PLAN): opt-in, new network hosts, needs the owner's decisions first; no card/bank advice.
- **Small leftovers:**
  - "theme accents on/off" setting (§4.7) and a launch-arguments override
  - GOG Galaxy install URI is still `// VERIFY`; Xbox GDK games unverified
  - Hollow Knight shows no Steam data (owner report, never investigated)
  - the update channel and "check automatically" are not synced yet (the schema has `updates` keys; the sync keeps them untouched)
  - `LibraryAutoRefreshTests.ScansAreSpacedOut` is timing-sensitive and can fail on a loaded CPU
- **Owner test notes:** a Steam game ignored Undo and Force close (the tracked process ended, a window stayed); the owner accepted this, and apps close fine.

## How to work

- **Order:** milestone by milestone. One milestone = one or more focused PRs; **one PR per step**; each leaves `main` building and green.
- **PR description:** starts with a checklist from the plan's "Done when" column, then security notes, then how to test. No migration or dependency without saying so.
- **Docs follow the code:** if the implementation contradicts ARCHITECTURE.md or a schema, update the doc in the same PR and say why. Record owner decisions **with dates** in PLAN §7.
- **Store facts:** store paths/URIs in ARCHITECTURE.md §4 are **starting points to verify**. When you can't verify on a real machine, write fixture-based tests from documented formats and leave a `// VERIFY:` comment plus a note in the PR.
- **Ask the owner before** (use the question tool, recommended option first):
  - adding a dependency not listed in ARCHITECTURE.md §2
  - a breaking schema change
  - changing scope
  - anything touching the security rules below (for example, what is allowed to be written where, or accepting executable definitions from outside)
- **Merging needs the owner's explicit OK** ("mergealo", "Funciona, mergealo"), never on your own. Then check "Build & test (Windows)" is green on the **latest** commit and rebase-merge (`gh pr merge <n> --rebase --delete-branch`, or the GitHub tool). `main` is protected (PR + that check). Subscribe to the PR so CI results wake the session.
- **Owner bug reports:** fix them in the same PR as new commits and push to the PR branch.
- **After merging:** restart the working branch from `origin/main` and push it with `--force-with-lease` (it only held already-merged history). The owner updates his checkout with `git fetch origin; git reset --hard origin/<branch>`; **never `git pull`** (the rebase merge makes histories diverge and it conflicts).
- **Standing demand:** nobody outside the person using Lumen may ever get at their credentials; everything must be secure and transparent.

### Working with the owner

- **Language:** the owner writes in Spanish (rioplatense, voseo). Reply in Spanish, in plain words, outcome first. Code, commits, PR text and docs stay in English.
- **After every change:** say how to run it (branch + commands) and give a **prioritized manual test guide** in Spanish (what to click, what should happen). Give PowerShell commands in separate code blocks. The owner's checkout is `C:\Github\lumen`.
- **Owner's wording:** "mergealo" = merge; "Todo bien / Funciona" = tested OK; he reports bugs with screenshots (look for text left in the wrong language, stale state).

### Testing on the owner's PC (Windows 11) when running locally

- **Never steal focus.** Start test builds with `--tray` (no window). Never trigger the hotkey and never show windows. Stop test processes **by path only** (never by window title: it could kill the owner's Lumen).
- **Build into the scratchpad:** the owner's running Lumen locks `src/Lumen.App/bin`, so use `dotnet build src/Lumen.App -o <scratchpad>/verify-build`. Logs are in `%LOCALAPPDATA%\Lumen\logs`, and Lumen never logs secrets.
- **Single instance:** one Lumen per install folder, so a test build from another folder runs alongside. If the owner's Lumen holds Alt+Shift+Space, a "shortcut refused" warning in the test build is expected.
- **Database migrations:** `MigrationRunner` refuses a database newer than the code (DB is at version 13). A branch that adds a migration breaks every other branch on the owner's PC once he runs it, so merge it first or rebase the others on it.
- **The owner's account:** never act on it from a test build. For example, revocations happen when he opens the build himself.

### Working in the cloud sandbox (Linux)

- The WPF app **cannot run** there; it builds (`EnableWindowsTargeting`). Say so in the PR and give the owner the manual test.
- .NET SDK: `dotnet-install.sh --version 10.0.100 --install-dir ~/.dotnet` (needs `dot.net` and `builds.dotnet.microsoft.com` allowed; set `DOTNET_ROOT` and `PATH`).
- **Test baseline on Linux: 26 failures in `Lumen.Core.Tests`** (`RoutineSafety/Runner/Review/Service` tests with `C:\` paths). They pass on the Windows CI. Any other failure is yours.
- GitHub through the MCP tools (no `gh`). The `vpk` tool can run there (`dotnet tool install --tool-path … vpk --version 1.2.161`, needs `zstd`) to validate packaging.

### Tooling pitfalls

- **Backslashes:** the Bash tool collapses `\\` even inside quoted heredocs, so Windows paths and JSON escapes get mangled. Write such files with the Write tool, or use Python with raw strings or `chr(92)`.
- **resx edits:** do them by script. Keep CRLF, add each key to **both** `Strings.resx` and `Strings.es.resx`, and remove keys from both. A repo test checks that the two match and that every `{l:Loc}` key exists (it can't see keys built in code, such as `"X_" + name`).
- **Line endings:** `.cs`, `.xaml`, `.csproj`, `.props` are CRLF; Markdown and YAML are LF (`.gitattributes`).
- **New views:** every `Views/*.xaml` needs its `.xaml.cs`, otherwise the page renders blank.
- **ListBox text:** a `ListBox` needs `Foreground="{DynamicResource Lumen.Brush.Text}"`, otherwise its text is black on dark.
- **New overlays** need a `GamepadLayer` priority (detail = 1, Discover = 2, welcome = 3; routines preview/editor = 1).
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
- **Never store translated text.** Keep a key or a `Func<string>` and write it again when read or on `CultureChanged` (it bit us twice: routine results, the sync card). Texts built from state (status, counts) are computed on read.
- **File header:** every source file starts with:
  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
  ```
- **C#:** nullable enabled, warnings as errors, file-scoped namespaces, `async` all the way with `CancellationToken`, no `.Result`/`.Wait()`. Logging uses `[LoggerMessage]`.
- **Threads:** events raised by background work (sync, updates, watchers) are posted to the UI `SynchronizationContext` captured at construction before touching view models.
- **Paths:** code must cope with `/` and `\` mixed (watchers, `Path.Combine` on Windows); compare normalized.
- **MVVM:** CommunityToolkit.Mvvm source generators; no logic in code-behind beyond view concerns.
- **Dependencies between projects:** `App → Platform.Windows → Providers → Data → Core`. `Lumen.Core` has no Windows/WPF references.
- **Git:** Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`), scope optional (`feat(steam): ...`). Branches are named `feat/<milestone>-<short-name>` (`fix/…`, `docs/…`); a cloud session may be told to use its own branch name.
- **Tests:** xUnit + Shouldly; parsing code is always fixture-driven (`tests/fixtures/<area>/...`). Fixtures must be synthetic: never someone's real library, SteamID, tokens or usernames (SteamIDs like `76561190000000001`). Tests must not depend on timing without a generous timeout.
- **Packages:** versions are pinned in `Directory.Packages.props`. Pick versions at least ~2 weeks old. NuGet audit runs with warnings as errors.
- **Versions:** development builds are `0.0.0-dev`; releases get their version from the tag (`v0.2.0-beta.1`, a `-` makes it a pre-release). The `vpk` version in `release.yml` must match the Velopack package.

## Security rules (non-negotiable)

1. **No secrets in the repo.**
   - API keys (Steam, SteamGridDB) and account tokens (Epic, GOG) are entered by the user and stored with DPAPI (`DataProtectionScope.CurrentUser`).
   - Never log them, never sync them, never put them in URLs that get logged; requests with secrets use a plain `HttpClient` and errors carry status codes only.
   - Sign-in flows follow PLAN-v1 §4.8: no passwords when avoidable, refresh token only, revoke on disconnect, transparent in Settings.
2. **Read-only toward stores.** Never write to a store's files, folders or registry keys. `IFileSystem` only reads; Lumen's own files go through `IUserFiles`, which only writes inside Lumen's folders **plus the sync folder the user picked** (never a drive root or a Windows/Program Files/ProgramData folder; only `config.json`, `collections/`, `routines/` there).
3. **No elevation.** Nothing requires or requests admin; routine programs start without a shell; the installer is per-user.
4. **No telemetry, no analytics, no remote config.** Network calls only to: Steam Web API, Steam CDN, SteamGridDB, Epic/GOG auth and library endpoints, GitHub Releases (updates; the repository URL is a constant). Also Steam's own websites (store/login/help/community), and only inside the built-in Steam sign-in browser.
5. **Routines never run scripts or shell commands in v1.** Untrusted (new/changed) routines require explicit user approval via the preview before running, including ones that arrive through the sync folder. Blocked programs and protected processes are listed in `RoutineSafety`.
6. **No loading third-party code at runtime** (no plugins, no themes with code) in v1.
7. Providers never throw out of a scan; they return partial results + diagnostics.
8. **Never pose as a device or app Lumen isn't.** No sign-in that claims to be a store's mobile app or copies another device's identity. Store sign-ins only through the store's own web sign-in page, approved by the user:
   - Epic and GOG: the code the user pastes from the browser.
   - Steam: its website in Lumen's built-in browser, as Playnite does.

   The rule comes from the 2026-10-28 incident, when Steam flagged an account after Lumen signed in as its mobile app.
9. **Data from the sync folder is untrusted input.** Manual items follow the same rules as the add-item dialog (`ManualItemValidator`); a missing/wiped/unreadable/newer-schema folder or file never deletes or overwrites the user's data (see ARCHITECTURE "Sync folder").

## Definition of done (every PR)

- Builds with zero warnings; tests pass; new logic has tests.
- New UI strings exist in English and Spanish.
- Docs updated if behavior or contracts changed (ARCHITECTURE, schemas, PLAN §7, the Status section above).
- No secrets, personal data or real user files in the diff.
