# CLAUDE.md — working on Lumen

Lumen is a game-first launcher for Windows (C# + WPF, .NET 10) that unifies every game store and the user's curated apps, with optional JSON routines. Repo `axelprz/lumen`, owner Axel (AxelPRZ), checkout `C:\Github\lumen`.

## Start of a session

1. Read [docs/HANDOFF.md](docs/HANDOFF.md): status, the next task and its first step. It is the only status file; this file holds rules only.
2. Load only what the task needs. [docs/PLAN-v1.md](docs/PLAN-v1.md) (scope; §5 milestones, §7 decision log; anything not in it is out of scope unless the owner asks) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (layers, contracts; start with its "Where to find things" table) are big: grep their headings and open only the sections you need, never the whole file. JSON contracts: `docs/schemas/`, examples: `docs/examples/`.
3. Rules in `.claude/rules/` load by themselves when you touch matching paths (UI/WPF, Data, Providers, tests, packages). Longer how-tos are on demand: [docs/dev/owner-pc-testing.md](docs/dev/owner-pc-testing.md) (before building or running anything on his PC) and [docs/dev/tooling-and-release.md](docs/dev/tooling-and-release.md) (git/SSH fallback, releases, cloud sandbox, edit-script pitfalls).
4. Send heavy reading (many files, wide searches, reviews) to a subagent so only its summary enters this context.

## How to work

- **One PR per step**, each leaving `main` building and green. Branches `feat/<milestone>-<name>`, `fix/…`, `docs/…`. Conventional Commits (`feat(m11): …`, `fix: …`, `docs: …`).
- **PR description:** checklist from the plan's "Done when", then security notes, then what could not be verified here, then how to test. Say so when there is a migration or a dependency.
- **Docs follow the code** in the same PR (ARCHITECTURE, schemas, HANDOFF). Owner decisions go to PLAN §7 **with the date**.
- **Ask the owner first** (question tool, recommended option first): a dependency not in ARCHITECTURE §2; a breaking schema change; scope changes; anything touching the security rules (what may be written where, new network hosts, executable definitions from outside).
- **Merging:** only with the owner's explicit OK ("mergealo", "Funciona, mergealo") and "Build & test (Windows)" green on the **latest** commit (`gh pr checks <n>`; the run's `headSha` must match the PR head). Then `gh pr merge <n> --rebase --delete-branch`. `main` is protected.
  - CI still running: say so and ask him to tell you when it is green (you are woken only on failures; never poll), then check once and merge.
  - Several approved PRs touching the same lines: merge one, rebase the next on `origin/main`, keep both sides, push with `--force-with-lease`, wait for its CI again.
- **Owner bug reports** on an open PR: fix them on the same branch as new commits.
- **After merging:** `git fetch origin; git checkout main; git reset --hard origin/main`. The owner updates with `git fetch origin` + `git reset --hard origin/<branch>`; **never `git pull`** (rebase merges make histories diverge).
- **Releases:** only when the owner asks (steps in docs/dev/tooling-and-release.md).
- **Standing demand:** nobody outside the person using Lumen may ever get at their credentials; everything must be secure and transparent.

### Working with the owner

- **Language:** he writes in Spanish (rioplatense, voseo). Reply in that Spanish, plain words, outcome first. Code, commits, PR text and docs stay in English.
- **After every change:** switch the checkout to the branch worked on and give the commands to run it (each PowerShell command in its own code block: `cd C:\Github\lumen`, `git fetch origin`, `git checkout <branch>`, `git reset --hard origin/<branch>`, `dotnet run --project src/Lumen.App`), then a **prioritized manual test guide** in Spanish (what to click, what should happen). After a merge, give the commands to go back to `main`.
- **His wording:** "mergealo" = merge; "Funciona / Todo bien" = tested OK; bugs come with screenshots (look for wrong-language text, stale state, clipping full screen vs windowed, misalignment).

### Testing essentials (full guide: docs/dev/owner-pc-testing.md)

- **Never steal focus:** test builds start with `--tray`; never trigger the hotkey or show windows. Stop test processes **by path only**, never by title (it could kill his Lumen).
- **Build into the scratchpad** (`dotnet build src/Lumen.App -o <scratchpad>/verify-build`): his running Lumen locks `src/Lumen.App/bin`.
- A test build uses his real data folder and sync folder: never act on his accounts from it.
- The WPF app cannot run in the cloud sandbox (it builds): say so in the PR and hand over the manual test. Visual work can't be seen from here either.

## End of a session (handoff)

When the owner says the session is closing, or a PR/milestone lands, **overwrite `docs/HANDOFF.md`** (≤ 40 lines, English): where we are (branch, PR number and state, CI), done this session, **the next task with its exact first step and the files to open**, open questions for the owner, gotchas learned. Edit CLAUDE.md only when a rule changes: it is re-sent on every turn and editing it mid-session invalidates the prompt cache.

## Compact instructions

When compacting, keep: current branch and PR (number, state, CI), files changed and why, failing tests and their cause, owner decisions of this session, the next step. Drop exploration output, file dumps and command logs.

## Commands

```bash
dotnet restore
dotnet build            # whole solution; EnableWindowsTargeting lets it build on Linux too
dotnet test             # Core/Providers/Data (≈820 tests)
dotnet run --project src/Lumen.App   # Windows only
```

Keep logic in the OS-agnostic projects (`Lumen.Core`, `Lumen.Data`, `Lumen.Providers`) with tests; keep `Lumen.Platform.Windows` and `Lumen.App` thin.

## Conventions

- **Language:** code, identifiers, comments, commits, PR text and technical docs are **English**. UI text lives in resx (English + Spanish with voseo); never hard-code it, not even key names like "Enter".
- **Never store translated text:** keep a key or a `Func<string>` and write it on read or on `CultureChanged`. Numbers and dates follow Lumen's language (`FormatCulture`).
- **File header** on every source file:
  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
  ```
- **C#:** nullable on, warnings as errors, file-scoped namespaces, `async` with `CancellationToken`, no `.Result`/`.Wait()`, logging with `[LoggerMessage]`.
- **Threads:** events from background work (sync, updates, watchers, `SystemEvents`, `LaunchService.Launched`) are posted to the UI `SynchronizationContext`/`Dispatcher` before touching view models or windows.
- **Paths:** cope with mixed `/` and `\`; compare normalized.
- **MVVM:** CommunityToolkit.Mvvm source generators; code-behind only for view concerns.
- **Dependencies between projects:** `App → Platform.Windows → Providers → Data → Core`; `Lumen.Core` has no Windows/WPF references.

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
- Docs updated when behavior or contracts change (ARCHITECTURE, schemas, PLAN §7, HANDOFF).
- No secrets, personal data or real user files in the diff (the owner's README screenshots are his own choice).
- The PR says what could not be verified here and gives the owner a manual test.
