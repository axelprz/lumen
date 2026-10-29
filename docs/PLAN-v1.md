# Lumen v1 — Product & Delivery Plan

This is the agreed scope for Lumen 1.0. Anything not listed here is out of scope for v1 (see [Backlog](#backlog-post-v1)).
Architecture and technical contracts live in [ARCHITECTURE.md](ARCHITECTURE.md); JSON contracts in [schemas/](schemas/).

## 1. Product summary

Lumen is a game-first launcher for Windows that unifies every game store and the user's chosen apps into one library.

Priorities, in order:

1. **Games** — the protagonist of the UI: artwork, playtime, last played, store of origin.
2. **Apps** — live in the same library with a more compact presentation; only apps the user curated.
3. **Routines** — a secondary feature: JSON-defined sets of actions ("Work" opens VS Code, Slack and a folder).

Audience: the author and his team first; published openly on GitHub for anyone.

## 2. Fixed decisions

| Topic | Decision |
|---|---|
| Stack | C# + WPF, .NET 10 (LTS), MVVM with CommunityToolkit.Mvvm |
| OS | Windows 10 22H2+ and Windows 11, x64 |
| License | GPL-3.0-or-later, © 2026 Axel Ismael Perez (AxelPRZ) |
| Languages | Code, commits, technical docs in English. UI in English + Spanish (resx). README in both. |
| Storage | SQLite (local, per machine) + JSON files (portable, syncable) |
| Artwork | Steam CDN for Steam games, SteamGridDB for everything else, manual override |
| Updates | Velopack + GitHub Releases, channels `stable` and `beta` |
| Distribution | Velopack installer (auto-update) + portable zip |
| Gamepad | Basic navigation inside the normal UI (no separate Big Picture mode) |
| Look | Dark, Steam-like density but more striking: background and accent color follow the selected item's artwork |
| Secrets | User-supplied API keys/tokens only, encrypted with DPAPI (CurrentUser). Never in the repo, never synced. |
| Network | Only: Steam Web API, Steam CDN, SteamGridDB, Epic/GOG auth & library endpoints, GitHub Releases. No telemetry. |

## 3. Store coverage in v1

| Store | Installed (local, no login) | Owned but not installed | Launch | Install action |
|---|---|---|---|---|
| Steam | Yes | Yes — Steam Web API key + SteamID64. Family-shared games (M5): optional Steam sign-in | `steam://rungameid/{id}` | `steam://install/{id}` |
| Epic Games | Yes | Yes — optional login | Epic launcher URI | Opens Epic launcher |
| GOG | Yes | Yes — optional login | GOG Galaxy / exe | Opens GOG Galaxy |
| Xbox / MS Store | Yes | No (v2) | `shell:AppsFolder\{AUMID}` | — |
| Ubisoft Connect | Yes | No (v2) | `uplay://launch/{id}/0` | — |
| EA app | Yes | No (v2) | EA app URI / exe | — |
| Battle.net | Yes | No (v2) | `Battle.net.exe --exec="launch {code}"` | — |

Every URI and file location must be verified on a real machine when implemented (see ARCHITECTURE.md §4 for the starting points). Epic and GOG logins use unofficial but widely used flows (the same ones Heroic/Legendary use), and so does the Steam sign-in for Family Sharing (Steam's own `IAuthenticationService` / `IFamilyGroupsService`, which are not officially documented). They must be isolated so that a breakage never affects local detection or the rest of the library. All of them follow §4.8.

## 4. Features in v1

### 4.1 Library
- Unified list of games (all stores) and curated apps.
- Not-installed games are shown greyed out; primary button reads "Install" and opens the store's launcher.
- **Merging:** the same game owned on several stores appears once; the detail view lets the user pick which store to launch/install from. Automatic merge by normalized title; manual merge/split always available.
- Manual items: drag & drop an `.exe`, `.lnk` or URL, or add via dialog.
- Collections (mixed games + apps) and Favorites.
- Per-item overrides: title, artwork (cover, hero, logo, icon), hidden flag, launch arguments.
- Filters: type (all/games/apps), store, installed/not installed, collection. Instant search as you type.
- Sort: name, last played, playtime, recently added.

### 4.2 Apps
- Sources: Start menu shortcuts (all-users + current user) and `shell:AppsFolder` (packaged apps).
- Junk filter (uninstallers, readmes, help, setup tools, known runtimes). The list of patterns is data, not code.
- **Curation screen ("Discover apps"):** shows detected apps not yet in the library; one click adds or ignores. Ignored apps stay ignored across rescans.
- Apps use the icon by default; cover artwork optional.

### 4.3 UI
- **Sidebar:** Home · Library · Collections · Routines · Settings.
- **Home:** hero with the last played game (blurred hero art background, logo, Play button, playtime, last session); rows "Continue playing", "Favorites", and a compact "Quick apps" dock; pinned routines appear as cards.
- **Library:** cover grid (600×900 portrait covers), filter bar, search.
- **Detail view:** hero art, logo, Play/Install, store selector for merged items, playtime, last played, actions (open install folder, open store page, edit, hide, add to collection).
- **Dynamic theming:** background crossfades to the focused item's blurred hero; accent color extracted from the artwork and applied to buttons/highlights, with a contrast safeguard.
- **Tray icon** with menu: Open, Quick search, recent items, routines, Exit. Closing the window minimizes to tray (configurable).
- **Quick search:** global hotkey (default `Alt+Shift+Space`, configurable) opens a small centered search box over any app; type, Enter launches.
- **Gamepad:** D-pad / left stick move focus, A activates, B goes back, Y opens search, LB/RB switch sidebar section. Works when Lumen is in the foreground.
- **Localization:** English and Spanish; defaults to OS language, switchable in Settings.
- First-run wizard: language → detected stores → optional keys/logins → app curation.

### 4.4 Playtime
- Lumen tracks sessions for anything it launches by watching processes whose image path is inside the item's install directory.
- Imports Steam's `playtime_forever` when a Steam key is configured; shown separately ("Steam") and combined in totals without double counting sessions Lumen already recorded for Steam games (Lumen's own sessions for Steam games are informational; the displayed total for Steam games is Steam's number).
- Stored in SQLite (per machine).

### 4.5 Routines (secondary)
- A routine is a JSON file with an ordered list of actions. v1 action types: `launch` (library item or path, with args), `url`, `folder`, `wait`, `close`.
- **No script/command execution in v1.** No elevation, ever, in v1.
- Run from: Routines page, tray menu, quick search, pinned card on Home.
- "Undo" a routine: close what it opened (only processes Lumen started during that run).
- **Sync folder:** user picks a folder (e.g. inside OneDrive/Google Drive/a git repo). Lumen reads/writes `routines/`, `collections/` and `config.json` there and watches for changes.
- **Import/export:** single routine as `.json`.
- **Trust model:** each routine has a locally stored SHA-256 of its canonical JSON. A routine that is new or changed outside Lumen is "untrusted" until the user reviews a full preview (every action listed; `close` actions and unknown executables highlighted) and approves it.

### 4.6 Updates and distribution
- Velopack checks GitHub Releases on startup (and daily) and offers the update; user chooses when to restart.
- `stable` channel = normal releases; `beta` = pre-releases. Selectable in Settings.
- Release workflow publishes `Setup.exe`, the portable zip, and Velopack delta packages.
- Unsigned builds in v1 (SmartScreen warning documented in the README). Code signing evaluated later.

### 4.7 Settings
- Language, theme accents on/off, start with Windows, minimize to tray, hotkey.
- Stores: enable/disable each, custom library paths, Steam key + SteamID64, Epic/GOG connect/disconnect.
- Artwork: SteamGridDB key, clear cache.
- Sync folder; update channel; about/licenses.

## 5. Milestones

Each milestone ends with a working, tested build on `main`. Order matters; later milestones assume earlier ones.

| # | Milestone | Done when |
|---|---|---|
| M0 | **Scaffold** — solution layout, Directory.Build.props, DI host, Serilog logging, SQLite + migrations, resx localization skeleton, CI (build + test on `windows-latest`), empty WPF shell with sidebar | `dotnet build` and `dotnet test` pass in CI; app opens an empty dark window |
| M1 | **Library core + Steam + manual** — domain model, `ILibraryProvider`, scan service, Steam local detection (VDF/ACF), Steam Web API owned games, manual items, launch/install, basic grid | User sees all their Steam games (installed and not) and manual items in a grid and can launch/install them |
| M2 | **Other stores (local)** — Epic, GOG, Xbox/MS Store, Ubisoft, EA, Battle.net installed detection + launch; title normalization and merging | All installed games from all stores appear once each and launch |
| M3 | **Artwork + UI** — artwork service (Steam CDN, SteamGridDB, cache, overrides), Home, Library filters/search, Detail view, dynamic background/accent, first-run wizard | UI matches §4.3 for mouse/keyboard |
| M4 | **Apps** — Start menu + AppsFolder providers, junk filter, Discover screen, collections & favorites | User can curate apps and organize mixed collections |
| M5 | **Accounts** (Steam sign-in for family games turned off on 2026-10-28 after Steam flagged the account; see ARCHITECTURE §4) — Epic and GOG optional login, owned-not-installed games for both; optional Steam sign-in (QR) for Steam Family shared games; all per §4.8 (the DPAPI secret store itself landed in M1, needed for the Steam key) | Connected Epic/GOG libraries show non-installed games; with Steam signed in, family-shared games appear (not-installed ones greyed out) |
| M6 | **Playtime** — session tracker, Steam playtime import, UI integration | Hours and last played are correct after playing |
| M7 | **Tray, hotkey, gamepad** — tray icon/menu, global hotkey quick search, gamepad navigation | All three work as in §4.3 |
| M8 | **Routines** — schema loader/validator, action executor, trust model, import preview, sync folder watcher, UI | §4.5 complete |
| M9 | **Updates & release** — Velopack integration, release workflow, portable zip, update UI, channels | Tagging a version (pre-release tags until 1.0) publishes an installable, self-updating release |
| M10 | **UI rework** (owner request, 2026-10-28) — see §4.9 | The owner approves the new look on real use; every screen and the gamepad still work |
| — | **1.0 hardening** — bug bash on real machines with every store installed, docs, screenshots (of the reworked UI) | Tagging `v1.0.0` releases |

### 4.8 Account security (Steam, Epic, GOG sign-ins)

Owner requirement: nobody outside the person using Lumen may be able to get at their credentials, and Lumen must be transparent about what it stores. Every sign-in follows these rules:

- **Lumen never sees passwords** when the store offers an alternative. Steam: sign in by scanning a QR code with the Steam mobile app (approval happens on the phone). Epic/GOG: the store's own login page in the system browser; Lumen only receives the authorization code.
- **Only what is needed is stored:** the long-lived refresh token, encrypted with DPAPI (CurrentUser + app entropy) in the local database. Short-lived access tokens live only in memory.
- **Never logged, never synced, never in the repo, never sent anywhere except the store that issued it.** Requests that carry a token use a plain `HttpClient` (no URL logging); errors carry status codes only.
- **Read-only:** Lumen only reads library data (owned/shared games). It never buys, changes account settings or touches anything else.
- **Disconnect** revokes the token at the store when the store supports it, then deletes it locally.
- **Transparency in Settings:** per account, show whether it is connected, which account, since when, and what Lumen can do with it.
- **Honest limits (documented in the README):** DPAPI protects against other Windows users and against a copied database or disk; it cannot protect against malware already running as the same Windows user. Signing out of Lumen or Windows does not revoke the token; Disconnect does.
- Any change to these flows is reviewed by the owner before merging.

### 4.9 UI rework (M10, before 1.0)

Owner request (2026-10-28): before 1.0, a modern, futuristic look that makes people want to use Lumen. It should be friendly and striking, at home next to Steam and other launchers. Same screens and features; this is the look, motion and type.

- **Type:** a new UI font in the spirit of game launchers. Steam's Motiva Sans is commercial, so it can't ship. Candidates with an open license (SIL OFL): Inter, Outfit, Exo 2, Rajdhani. The owner picks from a sample; the font file ships inside the app (it is not code, so security rule 6 is unaffected).
- **Motion:**
  - page transitions
  - tile hover/focus (scale, glow)
  - staggered entrance of rows
  - smooth hero crossfades

  Motion is short (≤ 250 ms for interactions) and respects Windows' "Animation effects" off setting.
- **Surfaces:** Windows 11 Mica/acrylic backdrop where available (fallback: the current dark palette), depth and glow on art, refined spacing and hierarchy.
- **Constraints:**
  - keeps the scroll performance of the M5 fix (no per-tile effects that cost a frame)
  - keeps contrast rules (§4.3 accent safeguard)
  - keyboard and gamepad focus stay clearly visible
  - all text stays in resx

## 6. Non-functional requirements

- Cold start to interactive library < 2 s with a cached library of ~1,000 items; rescans run in the background.
- A failing provider (corrupt file, store not installed, expired token) never blocks the others; it logs and shows a non-blocking notice.
- No admin rights required for anything.
- Logs in `%LOCALAPPDATA%\Lumen\logs`, rolling, no secrets or tokens ever logged.
- All parsing code (VDF, manifests, registry snapshots, schemas) has unit tests with fixture files.

## v1.1 — Deals (owner request, 2026-10-28)

A "Deals" section so players stay on top of offers and what's new. Planned for 1.1, after 1.0 is stable.

- **Search a game → price comparison** across stores, with the historical low. Sources to evaluate: CheapShark (no key), IsThereAnyDeal (free app key).
- **Best deals of the week** across all stores.
- **Free games now:** Epic's weekly free games (its public promotions feed), plus giveaways on other stores (e.g. GamerPower).
- **Trending:** Steam's most played and top sellers (public Steam endpoints). "Trending" means popular right now, never paid placement or ads.
- **Local price with taxes (Argentina):** the idea behind the Steamcito browser extension (final price as charged to a local card). Tax rates change often, so they live in a data file updated with releases and shown with their date. Advice on which bank card or promotion to use is **out of scope**: there is no reliable source and it goes stale quickly.

**Security and privacy (must be settled with the owner before starting):**
- It adds new network hosts to CLAUDE.md security rule 4.
- The section is **off until the user turns it on**. The prompt names the services it contacts.
- A search sends only the game's name. No account, token, library or ID ever goes to these services.
- Responses are cached. Requests carry no identifiers.
- Each service's terms are respected (e.g. links back to the deal).

## Backlog (post-v1)

- Owned-not-installed for Xbox, Ubisoft, EA, Battle.net.
- Routine actions: run script/command, window placement/monitors, power plan, audio device, Focus/Do Not Disturb.
- Routine triggers: schedule, monitor connected, game started/stopped ("when I play X, close Discord and Chrome").
- Full-screen gamepad mode.
- Code signing.
- Plugin system (only with a clear security model).
- Achievements, news, friends.
