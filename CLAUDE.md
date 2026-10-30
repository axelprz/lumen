# CLAUDE.md — working on Lumen

Lumen is a game-first launcher for Windows (C# + WPF, .NET 10) that unifies every game store and the user's curated apps, with optional JSON routines.

**Read first, in this order:**
1. This file (rules, status, pitfalls).
2. [docs/PLAN-v1.md](docs/PLAN-v1.md): scope, fixed decisions, milestones, and the **decision log** (§7). Anything not in the plan is out of scope unless the owner asks.
3. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): layers, contracts, what is implemented and verified. Start with its "Where to find things" table. JSON contracts are in [docs/schemas/](docs/schemas/) with examples in `docs/examples/`.

## Status (update when a milestone or a big PR lands)

- **Done:** M0–M9. M5 Steam family uses the website sign-in (2026-10-29). M8: routine engine, Routines page, shortcuts from tray/quick search/Home, sync folder engine and its Settings card (#36–#43). M9: Velopack updates + release workflow (#44); the first pre-releases (`v0.1.0-beta.*`) are tagged and the release/update flow works.
- **M10 UI rework (PLAN §4.9):** first pass done (#46–#51). **Second pass in progress** from the owner's HTML template (PLAN §4.9 lists the steps A–G; A = design base #53, B = shell #54, B2 = sidebar footer #55, C = Home #56, D = Library #57, E = detail #58, F = Collections and Routines #59, G = Settings and overlays). The owner approves each step on real use: fix what he reports in the same PR before moving on.
- **Next, in order:**
  1. **1.0 hardening** (scope decided 2026-10-30, PLAN §7): About and licenses (done in its PR); the owner verifies the `VERIFY` items on his PC (GOG Galaxy, Xbox app, EA, Ubisoft, Battle.net installed); README screenshots (he takes them, we wire them in English and Spanish); fix the small leftovers below; then he tags `v1.0.0` (the default update channel flips to stable by itself at 1.0).
  2. **v1.1 Deals** (PLAN): opt-in, new network hosts, needs the owner's decisions first; no card/bank advice.
- **Small leftovers:**
  - GOG Galaxy install URI is still `// VERIFY`; Xbox GDK games unverified
  - Hollow Knight shows no Steam data (owner report, never investigated)
  - the update channel and "check automatically" are not synced yet (the schema has `updates` keys; the sync keeps them untouched)
  - `LibraryAutoRefreshTests.ScansAreSpacedOut` is timing-sensitive and can fail on a loaded CPU
  - M10 loose ends: the Library is the heaviest page to open (the owner said the stutter felt better after #57; measure before touching it again); Home tiles glow with the global accent, which is not softened like the Library glow (`AccentPicker.Soften`)
- **Owner test notes:** a Steam game ignored Undo and Force close (the tracked process ended, a window stayed); the owner accepted this, and apps close fine.

## How to work

- **Order:** milestone by milestone. One milestone = one or more focused PRs; **one PR per step**; each leaves `main` building and green.
- **PR description:** starts with a checklist from the plan's "Done when" column, then security notes, then how to test. No migration or dependency without saying so.
- **Docs follow the code:** if the implementation contradicts ARCHITECTURE.md or a schema, update the doc in the same PR and say why. Record owner decisions **with dates** in PLAN §7.
- **Store facts:** store paths/URIs in ARCHITECTURE.md §4 are **starting points to verify**. When you can't verify on a real machine, write fixture-based tests from documented formats and leave a `// VERIFY:` comment plus a note in the PR.
- **Ask the owner before** (question tool, recommended option first): adding a dependency not listed in ARCHITECTURE.md §2; a breaking schema change; changing scope; anything touching the security rules below (what may be written where, accepting executable definitions from outside).
- **Merging needs the owner's explicit OK** ("mergealo", "Funciona, mergealo"), never on your own, and "Build & test (Windows)" must be green on the **latest** commit. Then `gh pr merge <n> --rebase --delete-branch` (or the GitHub tool). `main` is protected (PR + that check).
  - If CI is still running, say so and wait: you are only woken on CI *failures*, never on success, and CI is not polled in a loop. Ask the owner to say when it is green, then re-check once and merge.
- **Owner bug reports:** fix them in the same PR as new commits and push to the PR branch.
- **After merging:** switch the local checkout to `main` and reset it to `origin/main` (or restart the working branch from it and push with `--force-with-lease`). The owner updates his checkout with `git fetch origin; git reset --hard origin/<branch>`; **never `git pull`** (the rebase merge makes histories diverge and it conflicts).
- **Standing demand:** nobody outside the person using Lumen may ever get at their credentials; everything must be secure and transparent.

### Working with the owner

- **Language:** the owner writes in Spanish (rioplatense, voseo). Reply in Spanish, in plain words, outcome first. Code, commits, PR text and docs stay in English.
- **After every change:** switch the checkout to the branch worked on and say how to run it (branch + commands, each PowerShell command in its own code block; the owner's checkout is `C:\Github\lumen`), then give a **prioritized manual test guide** in Spanish (what to click, what should happen) so he can close the step.
- **Owner's wording:** "mergealo" = merge; "Todo bien / Funciona" = tested OK; he reports bugs with screenshots (look for text left in the wrong language, stale state, clipping at full screen vs windowed).
- **Taste (M10):** he likes calm, restrained effects and clear motion; he rejected exaggerated, saturated colors. When unsure about a look, make it subtler and say how to turn it up.

### Testing on the owner's PC (Windows 11) when running locally

- **Never steal focus.** Start test builds with `--tray` (no window). Never trigger the hotkey and never show windows. Stop test processes **by path only** (never by window title: it could kill the owner's Lumen). Most visual work can't be seen from here: say so in the PR and hand over the manual test.
- **Build into the scratchpad:** the owner's running Lumen locks `src/Lumen.App/bin`, so use `dotnet build src/Lumen.App -o <scratchpad>/verify-build`. (`dotnet build` / `dotnet test` of the whole solution also work when his Lumen is closed.) Logs are in `%LOCALAPPDATA%\Lumen\logs`, and Lumen never logs secrets.
- **Single instance:** one Lumen per install folder, so a test build from another folder runs alongside. If the owner's Lumen holds Alt+Shift+Space, a "shortcut refused" warning in the test build is expected.
- **Database migrations:** `MigrationRunner` refuses a database newer than the code (DB is at version 13). A branch that adds a migration breaks every other branch on the owner's PC once he runs it, so merge it first or rebase the others on it.
- **The owner's account:** never act on it from a test build. For example, revocations happen when he opens the build himself.
- **Local tools:** on the owner's machine `gh` works (PRs, CI, merge) and the app's PR tools (`get_status`, `bind_pr`) track the PR; a PR opened with `gh` is bound automatically.

### Working in the cloud sandbox (Linux)

- The WPF app **cannot run** there; it builds (`EnableWindowsTargeting`). Say so in the PR and give the owner the manual test.
- .NET SDK: `dotnet-install.sh --version 10.0.100 --install-dir ~/.dotnet` (needs `dot.net` and `builds.dotnet.microsoft.com` allowed; set `DOTNET_ROOT` and `PATH`).
- **Test baseline on Linux: 26 failures in `Lumen.Core.Tests`** (`RoutineSafety/Runner/Review/Service` tests with `C:\` paths). They pass on the Windows CI. Any other failure is yours.
- GitHub through the MCP tools (no `gh`). The `vpk` tool can run there (`dotnet tool install --tool-path … vpk --version 1.2.161`, needs `zstd`) to validate packaging.

### Tooling pitfalls

- **Backslashes:** the Bash tool collapses `\\` even inside quoted heredocs, so Windows paths and JSON escapes get mangled. Write such files with the Write tool, or use Python with raw strings or `chr(92)`.
- **Line endings:** `.cs`, `.xaml`, `.csproj`, `.props`, `.resx` are CRLF; Markdown and YAML are LF (`.gitattributes`). A script that edits them must normalize first (read, replace `\r\n` with `\n`, edit, write back with `\r\n`), or its search strings will not match.
- **resx edits:** do them by script. Add each key to **both** `Strings.resx` and `Strings.es.resx`, and remove keys from both. A repo test checks that the two match and that every `{l:Loc}` key exists (it can't see keys built in code, such as `"X_" + name`).
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
- **Threads:** events raised by background work (sync, updates, watchers, `SystemEvents`) are posted to the UI `SynchronizationContext` or `Dispatcher` before touching view models or the window.
- **Paths:** code must cope with `/` and `\` mixed (watchers, `Path.Combine` on Windows); compare normalized.
- **MVVM:** CommunityToolkit.Mvvm source generators; no logic in code-behind beyond view concerns.
- **Dependencies between projects:** `App → Platform.Windows → Providers → Data → Core`. `Lumen.Core` has no Windows/WPF references.
- **Git:** Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`), scope optional (`feat(m10): ...`). Branches are named `feat/<milestone>-<short-name>` (`fix/…`, `docs/…`); a cloud session may be told to use its own branch name.
- **Tests:** xUnit + Shouldly; parsing code is always fixture-driven (`tests/fixtures/<area>/...`). Fixtures must be synthetic: never someone's real library, SteamID, tokens or usernames (SteamIDs like `76561190000000001`). Tests must not depend on timing without a generous timeout. `tests/Lumen.Core.Tests/RepositoryChecks` read source files as text (localization, UI font, Mica brushes, motion rules, type scale): a failing one means a UI rule was skipped.
- **Packages:** versions are pinned in `Directory.Packages.props`. Pick versions at least ~2 weeks old. NuGet audit runs with warnings as errors.
- **Versions:** development builds are `0.0.0-dev`; releases get their version from the tag (`v0.2.0-beta.1`, a `-` makes it a pre-release). The `vpk` version in `release.yml` must match the Velopack package.

### UI rules (M10, enforced by the repo tests)

- **Font:** Exo 2 (SIL OFL), four static weights embedded from `Assets/Fonts` (WPF can't use variable axes). Applied through implicit `Window`/`ToolTip`/`ContextMenu` styles; icon glyphs keep Segoe Fluent Icons. Do not add literal font families.
- **Type scale:** use `Lumen.FontSize.*` (Label 11, Caption 12, Small 13, Body 14, Lead 15, BodyLarge 16, Subtitle 18, Section 20, Title 24, Headline 32, Display 44) and the `Lumen.Text.*` styles. A literal `FontSize="14"` (any scale size) in a view fails a repo test.
- **Motion:** every animation goes through `Services/Motion.cs` (`To`, `PageIn`, `PanelIn`, `Stagger`, `FadeIn`, `HoverScale`, `ShowIn`). Interactions ≤ 250 ms (Fast 140, Normal 220); only hero and accent crossfades are slower (450). `Motion` reads Windows' "Animation effects": off means values jump to their end state. New animation code outside `Motion` fails a repo test.
- **Performance:** no per-tile bitmap effects (blur, drop shadow) in the grid; glow is a brush plus opacity mask, collapsed until needed. Animate only the pointed-at tile, never every tile of a scrolling list. Heavy pages start their entrance after layout (background priority).
- **Room for scaled things:** a `VirtualizingStackPanel` and a `ScrollViewer` clip to their own bounds, so a scaled cover with ring and glow needs its padding inside the tile or row (Library tiles carry 8 px) or inside the scrolled content (Home rows use negative margin plus inner margin). Always check full screen and windowed.
- **Surfaces:** window and sidebar paint with `Lumen.Brush.Backdrop` / `SidebarBackdrop`; `MicaBackdrop` makes them translucent only on Windows 11 22H2+ with "Transparency effects" on (opaque otherwise, live). Cards are subtle glass (`Lumen.Card` = `Lumen.Brush.Glass`, ≈ 85 %); detail and overlays stay opaque (the wizard sits on a 95 % `Lumen.Brush.Scrim`). Never put translucent brushes on other windows.
- **Brand vs accent:** chrome (buttons, sidebar, inputs, focus rings, links) uses the fixed `Lumen.Brush.Brand*`; `Lumen.Brush.Accent` (the game's color) only tints art (hero, background, cover glow). Primary actions use `Lumen.Button.Primary`.
- **Ambient lights:** `Motion.Breathe` is the only long-running animation (20 fps cap, stops when hidden/minimized).
- **Accent and glow:** the global accent follows the featured game (`AccentService`); Library glow and ring use the cover's own color softened by `AccentPicker.Soften` (saturation ≤ 0.5), computed lazily on first hover (`CoverColor`).

## Security rules (non-negotiable)

1. **No secrets in the repo.**
   - API keys (Steam, SteamGridDB) and account tokens (Epic, GOG) are entered by the user and stored with DPAPI (`DataProtectionScope.CurrentUser`).
   - Never log them, never sync them, never put them in URLs that get logged; requests with secrets use a plain `HttpClient` and errors carry status codes only.
   - Sign-in flows follow PLAN-v1 §4.8: no passwords when avoidable, refresh token only, revoke on disconnect, transparent in Settings.
2. **Read-only toward stores.** Never write to a store's files, folders or registry keys. `IFileSystem` only reads; Lumen's own files go through `IUserFiles`, which only writes inside Lumen's folders **plus the sync folder the user picked** (never a drive root or a Windows/Program Files/ProgramData folder; only `config.json`, `collections/`, `routines/` there). Reading a Windows setting (such as `EnableTransparency`) is fine.
3. **No elevation.** Nothing requires or requests admin; routine programs start without a shell; the installer is per-user.
4. **No telemetry, no analytics, no remote config.** Network calls only to: Steam Web API, Steam CDN, SteamGridDB, Epic/GOG auth and library endpoints, GitHub Releases (updates; the repository URL is a constant). Also Steam's own websites (store/login/help/community), and only inside the built-in Steam sign-in browser.
5. **Routines never run scripts or shell commands in v1.** Untrusted (new/changed) routines require explicit user approval via the preview before running, including ones that arrive through the sync folder. Blocked programs and protected processes are listed in `RoutineSafety`.
6. **No loading third-party code at runtime** (no plugins, no themes with code) in v1. Font files are data, not code.
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
- The PR text says what could not be verified here (usually how it looks) and gives the owner a manual test.
