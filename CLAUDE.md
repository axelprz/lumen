# CLAUDE.md — working on Lumen

Lumen is a game-first launcher for Windows (C# + WPF, .NET 10) that unifies every game store and the user's curated apps, with optional JSON routines. Repo `axelprz/lumen`, owner Axel (AxelPRZ), checkout `C:\Github\lumen`.

**Read first, in this order:**
1. This file (status, rules, pitfalls).
2. [docs/PLAN-v1.md](docs/PLAN-v1.md): scope, milestones (§5) and the **decision log** (§7). Anything not in the plan is out of scope unless the owner asks.
3. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): layers, contracts, what is implemented and verified. Start with its "Where to find things" table; §8 "Look and motion" has every UI piece. JSON contracts are in [docs/schemas/](docs/schemas/), examples in `docs/examples/`.

## Status (update when a milestone or a big PR lands)

- **Done:** M0–M10. M10 (UI rework) closed 2026-11-02: first pass #46–#51, the owner's HTML template pass #53–#60, About/licenses #61, crash fix #62, menus #63, quick search #64, README screenshots #65, leftovers #66 (Home glow, double scan, stores verified), update settings in the sync folder #67.
- **Released:** pre-releases `v0.1.0-beta.1`–`2` and `v0.2.0-beta.1`–`4` (latest `v0.2.0-beta.4`, 2026-11-02). Friends of the owner are testing; their feedback comes before `v1.0.0`.
- **Next, in order (owner, 2026-11-02, PLAN §5/§7: everything pending comes into v1):**
  1. **Friends' beta feedback:** fix what they report, one PR per issue.
  2. **M11 Settings completion:** theme accents on/off; enable/disable each store; custom library paths; per-item launch arguments. Ask the owner what "custom library paths" should cover before coding (stores already find their own folders). Launch arguments likely need a DB migration (DB is at 13): merge it before other branches.
  3. **M12:** Steam achievements (unlocked / total) in the detail; notification center (a bell in the top bar: updates, store problems, sync arrivals).
  4. **M13 Deals:** needs the owner's decisions first (sources such as CheapShark / IsThereAnyDeal / Epic promotions / GamerPower / Steam charts, which new hosts go into security rule 4, opt-in prompt, Argentina tax data file). No card or bank advice.
  5. **1.0:** last bug bash, then `v1.0.0` (tag only on the owner's word; the default update channel flips to stable by itself at 1.0).
- **Open notes:** the Library is the heaviest page to open (the owner said it felt better after #57; measure before touching it). A Steam game ignored Undo and Force close (tracked process ended, a window stayed); the owner accepted it, apps close fine.

## How to work

- **One PR per step**, each leaving `main` building and green. Branches `feat/<milestone>-<name>`, `fix/…`, `docs/…`.
- **PR description:** checklist from the plan's "Done when", then security notes, then what could not be verified here, then how to test. Say so when there is a migration or a dependency.
- **Docs follow the code** in the same PR (ARCHITECTURE, schemas, the Status above). Owner decisions go to PLAN §7 **with the date**.
- **Store facts** in ARCHITECTURE §4 are starting points: unverified ones get fixture tests, a `// VERIFY:` comment and a PR note. GOG Galaxy, Xbox (GDK), EA, Ubisoft and Battle.net were verified by the owner on 2026-11-02.
- **Ask the owner first** (question tool, recommended option first): a dependency not in ARCHITECTURE §2; a breaking schema change; scope changes; anything touching the security rules (what may be written where, new network hosts, executable definitions from outside).
- **Merging:** only with the owner's explicit OK ("mergealo", "Funciona, mergealo") and "Build & test (Windows)" green on the **latest** commit (`gh pr checks <n>`; confirm the run's `headSha` matches the PR head). Then `gh pr merge <n> --rebase --delete-branch`. `main` is protected.
  - CI still running: say so and ask him to tell you when it is green (you are woken only on failures; never poll), then check once and merge.
  - Several approved PRs that touch the same lines (CLAUDE.md leftovers, PLAN §7, `UiSurfaceTests`): merge one, rebase the next on `origin/main`, keep both sides, push with `--force-with-lease`, and wait for its CI again.
- **Owner bug reports** on an open PR: fix them on the same branch as new commits.
- **After merging:** `git fetch origin; git checkout main; git reset --hard origin/main`. The owner updates with `git fetch origin` + `git reset --hard origin/<branch>`; **never `git pull`** (rebase merges make histories diverge).
- **Releases:** only when the owner asks. Annotated tag on `main`'s merge commit, e.g. `git tag -a v0.2.0-beta.5 -m "…" <sha>` and `git push origin <tag>`; `release.yml` builds and publishes (≈3 min, 6 assets). A `-` in the tag makes it a pre-release; versions come from the tag (`0.0.0-dev` locally).
- **GitHub SSH can fail intermittently** ("Permission denied (publickey)" or a 500). Retry once; otherwise push over HTTPS with gh's credentials for that command only: `git -c credential.helper= -c "credential.helper=!gh auth git-credential" push https://github.com/axelprz/lumen.git <ref>`.
- **Standing demand:** nobody outside the person using Lumen may ever get at their credentials; everything must be secure and transparent.

### Working with the owner

- **Language:** he writes in Spanish (rioplatense, voseo). Reply in that Spanish, plain words, outcome first. Code, commits, PR text and docs stay in English.
- **After every change:** switch the checkout to the branch worked on and give the commands to run it (each PowerShell command in its own code block: `cd C:\Github\lumen`, `git fetch origin`, `git checkout <branch>`, `git reset --hard origin/<branch>`, `dotnet run --project src/Lumen.App`), then a **prioritized manual test guide** in Spanish (what to click, what should happen). After a merge, give the commands to go back to `main`.
- **His wording:** "mergealo" = merge; "Funciona / Todo bien" = tested OK; bugs come with screenshots (look for wrong-language text, stale state, clipping full screen vs windowed, misalignment).
- **Taste:** calm, restrained effects; clear motion; no saturated colors; square-cornered tags (no ovals); blocks aligned and centered (no empty band on one side); related buttons grouped, never at opposite edges. When unsure, make it subtler and name the constant that turns it up. He checks full screen and windowed.

### Testing on the owner's PC (Windows 11)

- **Never steal focus.** Test builds start with `--tray` (no window). Never trigger the hotkey, never show windows. Stop test processes **by path only** (`Get-Process | ? { $_.Path -eq $exe } | Stop-Process`), never by title (it could kill his Lumen). Visual work can't be seen from here: say so and hand over the manual test.
- **Build into the scratchpad:** his running Lumen locks `src/Lumen.App/bin`: `dotnet build src/Lumen.App -o <scratchpad>/verify-build`. A whole-solution `dotnet build` then fails only with file-lock errors (MSB3021/3027); that is expected, `dotnet test` still runs.
- **Smoke test:** start `<scratchpad>/verify-build/Lumen.exe --tray`, wait ~12 s, check it is alive and `%LOCALAPPDATA%\Lumen\logs` has no `[ERR]`/`[FTL]` since the start, stop it by path. Unhandled UI exceptions are logged there ("Unhandled exception on the UI thread"): read the logs first when he reports a crash.
- **Shared data:** a test build uses his real data folder (`%LOCALAPPDATA%\Lumen`) and his sync folder if he set one. Never act on his accounts from a test build; mention any side effect (e.g. the sync writing his own settings).
- **Single instance per install folder:** a scratchpad build runs alongside his; "shortcut refused" (Alt+Shift+Space taken) is expected.
- **Database migrations:** `MigrationRunner` refuses a database newer than the code (DB is at 13). A branch with a migration breaks every other branch on his PC once he runs it: merge it first or rebase the others on it.
- **Local tools:** `gh` works (PRs, CI, merge); a PR opened with `gh` is bound to the app's PR tools automatically.

### Working in the cloud sandbox (Linux)

- The WPF app **cannot run** there; it builds (`EnableWindowsTargeting`). Say so in the PR and give the owner the manual test.
- .NET SDK: `dotnet-install.sh --version 10.0.100 --install-dir ~/.dotnet` (needs `dot.net` and `builds.dotnet.microsoft.com`; set `DOTNET_ROOT` and `PATH`).
- **Baseline: 26 failures in `Lumen.Core.Tests`** there (`RoutineSafety/Runner/Review/Service` tests with `C:\` paths); they pass on Windows CI. Any other failure is yours.
- GitHub through the MCP tools (no `gh`). `vpk` can validate packaging (`dotnet tool install --tool-path … vpk --version 1.2.161`, needs `zstd`).

### Tooling pitfalls

- **Edit scripts:** write Python scripts to the scratchpad with the Write tool and run them; long Bash heredocs with quotes break, and Bash collapses `\\` (Windows paths, JSON escapes). Each replacement should assert it matched exactly once.
- **Line endings:** `.cs`, `.xaml`, `.csproj`, `.props`, `.resx` are CRLF; Markdown and YAML are LF in the repo but CRLF in this working copy. Normalize before editing (`\r\n` → `\n`, edit, write back with the file's own ending).
- **Cutting code by text search:** a pattern like `"    }\n"` also matches deeper indentation; after removing a block, look at the result and build.
- **resx:** by script, every key in **both** `Strings.resx` and `Strings.es.resx` (a repo test checks they match and that every `{l:Loc}` key exists; it can't see keys built in code).
- **New views:** every `Views/*.xaml` needs its `.xaml.cs`, or the page renders blank. A `ListBox` needs `Foreground="{DynamicResource Lumen.Brush.Text}"`.
- **New overlays** need a `GamepadLayer` priority (detail = 1, routines preview/editor = 1, Discover = 2, welcome = 3).
- **WPF traps that bit us:** bindings don't see static properties through the DataContext (expose instance properties); never add children to a panel while WPF walks the tree (a page being torn down): defer with the Dispatcher; a local `MenuItem`/`Button` style without `BasedOn` drops the shared look; `System.IO`/`System.Net.Http` aren't implicit in the WPF project.
- **Playnite** (`JosefNemec/PlayniteExtensions`) is a good, safe reference for store behavior.

## Commands

```bash
dotnet restore
dotnet build            # whole solution; EnableWindowsTargeting lets it build on Linux too
dotnet test             # Core/Providers/Data (≈820 tests: Core 535, Providers 236, Data 49)
dotnet run --project src/Lumen.App   # Windows only
```

Keep logic in the OS-agnostic projects (`Lumen.Core`, `Lumen.Data`, `Lumen.Providers`) with tests; keep `Lumen.Platform.Windows` and `Lumen.App` thin.

## Conventions

- **Language:** code, identifiers, comments, commits, PR text and technical docs are **English**. UI text lives in resx (`Strings.resx` English, `Strings.es.resx` Spanish with voseo); never hard-code it, not even key names like "Enter".
- **Never store translated text:** keep a key or a `Func<string>` and write it on read or on `CultureChanged`. Numbers and dates follow Lumen's language (`FormatCulture`: Windows' regional format when it is the same language).
- **File header** on every source file:
  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
  ```
- **C#:** nullable on, warnings as errors, file-scoped namespaces, `async` with `CancellationToken`, no `.Result`/`.Wait()`, logging with `[LoggerMessage]`.
- **Threads:** events from background work (sync, updates, watchers, `SystemEvents`, `LaunchService.Launched`) are posted to the UI `SynchronizationContext`/`Dispatcher` before touching view models or windows.
- **Paths:** cope with mixed `/` and `\`; compare normalized.
- **MVVM:** CommunityToolkit.Mvvm source generators; code-behind only for view concerns.
- **Project dependencies:** `App → Platform.Windows → Providers → Data → Core`; `Lumen.Core` has no Windows/WPF references.
- **Git:** Conventional Commits (`feat(m11): …`, `fix: …`, `docs: …`).
- **Tests:** xUnit + Shouldly; parsing is fixture-driven (`tests/fixtures/<area>/…`), fixtures synthetic (SteamIDs like `76561190000000001`, never real libraries, tokens or names). Timing tests need generous timeouts; a test for a race should fail without the fix (check it). `tests/Lumen.Core.Tests/RepositoryChecks` read source as text (localization, font, type scale, motion, surfaces, menus, licenses): a failing one means a rule was skipped.
- **Packages:** pinned in `Directory.Packages.props`, versions at least ~2 weeks old, NuGet audit as errors. **Every new package must be added to `THIRD-PARTY-NOTICES.txt` and `AboutViewModel`** (a repo test fails otherwise; read license and copyright from the package's `.nuspec`). The `vpk` version in `release.yml` must match Velopack (1.2.161).

### UI rules (M10, enforced by repo tests where possible)

- **Font:** Exo 2 (SIL OFL), four static weights in `Assets/Fonts`; icons stay Segoe Fluent Icons. No literal font families.
- **Type scale:** `Lumen.FontSize.*` (Label 11, Caption 12, Small 13, Body 14, Lead 15, BodyLarge 16, Subtitle 18, Section 20, Title 24, Headline 32, Display 44) and `Lumen.Text.*`. A literal scale size in a view fails a test.
- **Palette and brand:** base `#0A0D14`, glass cards (`Lumen.Card` = `Lumen.Brush.Glass` ≈ 85 %). Chrome (buttons, sidebar, inputs, focus rings, links, menus) uses the fixed brand `Lumen.Brush.Brand*` (blue → violet); the game's color (`Lumen.Brush.Accent`, `AccentService`) only tints art; glows around art use `Lumen.Brush.AccentGlow` / `AccentPicker.Soften`. Primary actions: `Lumen.Button.Primary`; others `Lumen.Button`, `Lumen.Button.Icon`; tags `Lumen.Badge`, `Lumen.Pill` (6 px corners); on/off `Lumen.Switch`.
- **Surfaces:** window and sidebar use `Lumen.Brush.Backdrop` / `SidebarBackdrop` (translucent only with Mica: Windows 11 22H2+ and "Transparency effects"; the owner confirmed the tint). Detail and overlays stay opaque; the wizard sits on `Lumen.Brush.Scrim` (95 %). Popups and other windows are never translucent.
- **Motion:** only through `Services/Motion.cs` (`To`, `PageIn`, `PanelIn`, `Stagger`, `FadeIn`, `HoverScale`, `ShowIn`, `Shown`, `Breathe`). Interactions ≤ 250 ms (Fast 140, Normal 220; hero/accent 450). "Animation effects" off = values jump. Covering panels bind `Motion.Shown` (start Collapsed): they hide at once and a picture fades out, added on the next dispatcher turn and never able to crash. `Motion.Breathe` (ambient lights) is the only long-running animation (20 fps, stops when hidden/minimized).
- **Performance:** no per-tile bitmap effects in grids; glow is a brush plus opacity mask, collapsed until needed; animate only the pointed-at tile; heavy pages start their entrance after layout.
- **Room for scaled things:** `VirtualizingStackPanel`/`ScrollViewer` clip to their bounds: Library tiles carry 8 px padding, Home rows use negative margin plus inner margin. The Library grid is centered and its header card sized to the tiles (`OnGridSizeChanged`).
- **Shell pieces:** top bar search = the Library filter (Ctrl+K/Ctrl+F); toasts via `ToastService` (text as `Func<string>`); sidebar footer = Steam profile from Steam's local files + CPU/RAM monitor (only while visible); quick search groups results and opens a detail with Ctrl+Enter; all menus share `Controls/Menus.xaml`.

## Security rules (non-negotiable)

1. **No secrets in the repo.** API keys (Steam, SteamGridDB) and tokens (Epic, GOG) are entered by the user and stored with DPAPI (`CurrentUser`). Never logged, synced or put in logged URLs; requests with secrets use a plain `HttpClient`, errors carry status codes only. Sign-ins follow PLAN §4.8 (no passwords when avoidable, refresh token only, revoke on disconnect, transparent in Settings).
2. **Read-only toward stores.** Never write store files, folders or registry keys. `IFileSystem` only reads; Lumen writes through `IUserFiles`, only inside its folders **plus the sync folder the user picked** (never a drive root or a Windows/Program Files/ProgramData folder; only `config.json`, `collections/`, `routines/`). Reading Windows settings or a store's own files (e.g. Steam's `loginusers.vdf` and avatar cache for the sidebar profile) is fine.
3. **No elevation.** Nothing requires admin; routine programs start without a shell; the installer is per-user.
4. **No telemetry, analytics or remote config.** Network only to: Steam Web API, Steam CDN, SteamGridDB, Epic/GOG auth and library endpoints, GitHub Releases (updates; constant repository URL), and Steam's websites only inside the built-in Steam sign-in browser. Deals (M13) will add hosts only after the owner approves them here. Opening a link in the user's browser is not a Lumen request.
5. **Routines never run scripts or shell commands in v1.** New or changed routines (also from the sync folder) need approval in the preview before running; blocked programs and protected processes are in `RoutineSafety`.
6. **No third-party code at runtime** (no plugins, no themes with code). Fonts are data.
7. Providers never throw out of a scan; they return partial results plus diagnostics.
8. **Never pose as a device or app Lumen isn't.** Store sign-ins only through the store's own web page, approved by the user: Epic and GOG with the pasted code; Steam on its website in Lumen's built-in browser (as Playnite does). Rule born from the 2026-10-28 incident (Steam flagged the owner's account after a mobile-app sign-in).
9. **Sync folder data is untrusted.** Manual items follow the add-item rules (`ManualItemValidator`); settings are validated (e.g. update channel only "stable"/"beta"); a missing, wiped, unreadable or newer-schema folder never deletes or overwrites local data (ARCHITECTURE "Sync folder").

## Definition of done (every PR)

- Builds with zero warnings; tests pass; new logic has tests; the tray smoke test is clean.
- New UI strings in English and Spanish; new packages in the notices.
- Docs updated when behavior or contracts change (ARCHITECTURE, schemas, PLAN §7, Status above).
- No secrets, personal data or real user files in the diff (the owner's README screenshots are his own choice).
- The PR says what could not be verified here and gives the owner a manual test.
