# Lumen — Architecture

Companion to [PLAN-v1.md](PLAN-v1.md). This document defines the layers, contracts and technical starting points. When implementation proves a detail wrong, fix the code **and** this document.

## 1. Solution layout

```
Lumen.slnx
Directory.Build.props          # shared settings: net10.0, nullable, warnings as errors, EnableWindowsTargeting
Directory.Packages.props       # central package versions
src/
  Lumen.Core/                  # net10.0 — domain, interfaces, services. NO Windows/WPF references.
  Lumen.Data/                  # net10.0 — SQLite repositories + migrations
  Lumen.Providers/             # net10.0 — store/app providers' parsing logic (pure, testable)
  Lumen.Platform.Windows/      # net10.0-windows10.0.19041.0 — registry, processes, shell, DPAPI, hotkey, gamepad, AppX
  Lumen.App/                   # net10.0-windows10.0.19041.0 — WPF app: views, view models, theming, composition root
tests/
  Lumen.Core.Tests/
  Lumen.Providers.Tests/       # fixture-driven: sample .vdf/.acf/.item/registry snapshots
  Lumen.Data.Tests/
  fixtures/
docs/
  PLAN-v1.md  ARCHITECTURE.md  schemas/  examples/
```

Rule: **everything that can be pure stays out of the Windows projects** so it builds and tests on any OS (including Linux CI agents). Windows-only access (registry, file system locations under `%ProgramData%`, processes, AppX, shell) is abstracted behind interfaces in `Lumen.Core` and implemented in `Lumen.Platform.Windows`. Providers receive those abstractions, so a provider test feeds fixture data instead of a real registry.

Dependency direction: `App → Platform.Windows → Providers → Data → Core` (each may depend only on things to its right; `App` composes everything).

## 2. Key libraries

| Purpose | Library |
|---|---|
| MVVM | CommunityToolkit.Mvvm |
| DI / hosting | Microsoft.Extensions.Hosting |
| Logging | Serilog (+ file sink) |
| SQLite | Microsoft.Data.Sqlite + Dapper (hand-written SQL migrations) |
| JSON | System.Text.Json (+ JsonSchema.Net for validating routines/collections) |
| Tray icon | H.NotifyIcon.Wpf |
| Gamepad | Windows.Gaming.Input (via the Windows TFM) |
| Updates | Velopack |
| Steam VDF | small in-house parser (KeyValues text format is simple; keep it dependency-free and tested) |
| Tests | xUnit + FluentAssertions alternative (e.g. Shouldly) |

Versions are pinned in `Directory.Packages.props`. Prefer few dependencies; every new one must be MIT/BSD/Apache/LGPL/GPL-compatible.

## 3. Domain model

```csharp
// Stable identity across machines and syncs: "{source}:{externalId}"
// e.g. "steam:570", "epic:Fortnite", "gog:1207658924", "xbox:{PackageFamilyName}!{AppId}",
//      "startmenu:{relative .lnk path, normalized}", "manual:{guid}"
public readonly record struct ItemRef(string Source, string ExternalId);

public enum ItemKind { Game, App }
public enum InstallState { Installed, NotInstalled, Unknown }

public sealed record LibraryEntry(          // one per store per item, produced by providers
    ItemRef Ref,
    ItemKind Kind,
    string Title,
    InstallState State,
    string? InstallDirectory,
    LaunchSpec? Launch,
    LaunchSpec? Install,
    IReadOnlyDictionary<string,string> Extra);  // store-specific bits (e.g. Steam appid, Epic namespace)

public sealed record LaunchSpec(LaunchKind Kind, string Target, string? Arguments, string? WorkingDirectory);
public enum LaunchKind { Uri, Executable, ShellAppsFolder, ProcessWithArgs }

// What the UI shows: one or more LibraryEntry merged into a single item
public sealed class LibraryItem
{
    public Guid Id { get; }                         // local
    public ItemKind Kind { get; }
    public string Title { get; }                    // effective (override ?? best entry title)
    public IReadOnlyList<LibraryEntry> Entries { get; }  // >1 when owned on several stores
    public ItemRef? PreferredEntry { get; }
    public ItemOverrides Overrides { get; }         // title, artwork, args, hidden
    public PlaytimeSummary Playtime { get; }
}
```

### Merging
- Normalize titles: lowercase, strip ™®©, edition suffixes list (data file), punctuation, roman numerals → digits, collapse whitespace.
- Merge entries of `Kind.Game` with identical normalized title across different sources.
- User merge/split decisions are stored (SQLite) and always win over automatic merging.

## 4. Providers

```csharp
public interface ILibraryProvider
{
    string Source { get; }                         // "steam", "epic", ...
    string DisplayName { get; }
    ProviderCapabilities Capabilities { get; }    // InstalledScan, OwnedLibrary(requires account), Playtime
    Task<ProviderResult> ScanInstalledAsync(CancellationToken ct);
    Task<ProviderResult> ScanOwnedAsync(CancellationToken ct);   // returns NotConfigured if no key/login
}

public interface IAccountConnector                 // Epic, GOG (v1)
{
    string Source { get; }
    Task<ConnectResult> ConnectAsync(CancellationToken ct);      // interactive (browser)
    Task DisconnectAsync();
    AccountStatus Status { get; }
}
```

`ProviderResult` carries entries **plus** non-fatal diagnostics. A provider must never throw out of a scan: catch, log, return what it could read.

### Starting points per store (verify on a real machine before relying on them)

| Store | Installed detection | Launch / install |
|---|---|---|
| Steam | Install path from `HKCU\Software\Valve\Steam\SteamPath`; libraries in `steamapps\libraryfolders.vdf`; each game in `steamapps\appmanifest_{appid}.acf` (`name`, `installdir`, `StateFlags`). Owned: `IPlayerService/GetOwnedGames/v1` with `include_appinfo=1&include_played_free_games=1` (key + SteamID64). Exclude tools/redistributables (e.g. appid 228980). | `steam://rungameid/{appid}`, `steam://install/{appid}` |
| Epic | `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON: `DisplayName`, `AppName`, `CatalogNamespace`, `CatalogItemId`, `InstallLocation`, `LaunchExecutable`). Owned (login): Epic library service via the launcher OAuth flow used by Legendary/Heroic — research and document current endpoints. | `com.epicgames.launcher://apps/{namespace}%3A{catalogItemId}%3A{appName}?action=launch&silent=true`; install: same with `action=install` (verify) |
| GOG | `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\{gameID}` (`gameName`, `path`, `exe`, `launchCommand`). Owned (login): GOG embed API with the Galaxy client flow (research current endpoints). | Galaxy: `GalaxyClient.exe /command=runGame /gameId={id} /path="{path}"`; fallback: exe directly. Install: open Galaxy to the game. |
| Xbox / MS Store | `Windows.Management.Deployment.PackageManager.FindPackagesForUser("")`; a package is a game if its install location contains `MicrosoftGame.config` (or it is registered as a game). Read AppId from the manifest. | `explorer.exe shell:AppsFolder\{PackageFamilyName}!{AppId}` |
| Ubisoft | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\{id}\InstallDir`; title from install dir / uninstall registry key `Uplay Install {id}`. | `uplay://launch/{id}/0` |
| EA app | Uninstall registry entries + EA app install data (research current location/format). | EA app URI (research) or game exe |
| Battle.net | `%ProgramData%\Battle.net\Agent\product.db` (protobuf) or uninstall registry entries; map product codes (data file). | `Battle.net.exe --exec="launch {code}"` |
| Start menu | `%ProgramData%\Microsoft\Windows\Start Menu\Programs` and `%AppData%\Microsoft\Windows\Start Menu\Programs`, recursive `.lnk`/`.url`. Resolve target via IShellLink. | Shell execute the `.lnk` |
| AppsFolder | Enumerate `shell:AppsFolder` (packaged apps with AUMIDs). Skip games already found by the Xbox provider. | `shell:AppsFolder\{AUMID}` |
| Manual | SQLite | as configured |

Rules: providers only **read** store files; never write into a store's folder or registry. Junk filters and title-cleanup lists live in `src/Lumen.Providers/Data/*.json`.

## 5. Services (Lumen.Core)

| Service | Responsibility |
|---|---|
| `LibraryService` | Runs providers (in parallel, with timeouts), merges entries, applies overrides, persists, raises change events |
| `LaunchService` | Executes `LaunchSpec`, records "last launched", hands off to `PlaytimeTracker` |
| `PlaytimeTracker` | Every ~5 s lists processes (`QueryFullProcessImageName`, no admin) and matches image paths under the item's install dir; a session starts on first match and ends after ~30 s with no match; persists sessions |
| `ArtworkService` | Resolves cover/hero/logo/icon: override → Steam CDN (Steam items) → SteamGridDB (search by title, cache the SGDB game id) → icon extracted from exe → generated placeholder. Disk cache in `%LOCALAPPDATA%\Lumen\cache\artwork`. |
| `AccentService` | Extracts a dominant, saturated color from hero/cover art (quantize + score by saturation and population), ensures WCAG contrast vs. the dark background |
| `SecretStore` | DPAPI (`ProtectedData`, `DataProtectionScope.CurrentUser`) protected values in SQLite. Never logged, never synced. |
| `RoutineEngine` | Loads/validates routines, trust checks, executes actions sequentially with cancellation, tracks started processes for undo |
| `SyncFolderService` | Watches the sync folder (`FileSystemWatcher` + debounce + periodic rescan fallback), loads routines/collections/config, writes changes back atomically (write temp → rename) |
| `UpdateService` | Velopack: check, download, apply on restart; channel selection |

Steam CDN artwork (starting point, verify): `https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/library_600x900.jpg`, `library_hero.jpg`, `logo.png`, `header.jpg`.
SteamGridDB API v2: `https://www.steamgriddb.com/api/v2` with `Authorization: Bearer {key}` — `/search/autocomplete/{term}`, `/grids/game/{id}?dimensions=600x900`, `/heroes/game/{id}`, `/logos/game/{id}`, `/icons/game/{id}`.

## 6. Data

### Local — SQLite at `%LOCALAPPDATA%\Lumen\lumen.db`
Tables (initial): `entries` (provider output cache), `items`, `item_entries`, `merge_rules`, `overrides`, `playtime_sessions`, `steam_playtime`, `ignored_apps`, `secrets`, `routine_trust` (routine id → sha256 + approved_at), `settings_local`, `schema_version`.

### Portable — JSON (sync folder, or `%APPDATA%\Lumen\portable` when no sync folder is set)
```
<sync folder>/
  config.json            # schemas/config.schema.json
  collections/*.json     # schemas/collection.schema.json
  routines/*.json        # schemas/routine.schema.json
```
What syncs: preferences that are not machine-specific, collections, favorites, routines, manual items' definitions, artwork **choices** (URLs/ids, not image files).
What never syncs: secrets, tokens, playtime sessions, caches, absolute paths that only make sense on one machine (store them in SQLite keyed by `ItemRef`).

Portable files always reference items by `ItemRef` string (`"steam:570"`), never by local Guid or absolute path. Missing refs on another machine are shown as "not available here", never deleted.

Every portable file has `"schema": <int>`; loaders migrate older versions and refuse newer ones with a clear message.

## 7. Routine execution & trust

- Canonical JSON for hashing: parse → re-serialize with sorted keys, no whitespace → SHA-256.
- States: `Trusted` (hash matches `routine_trust`), `Untrusted` (new or changed). Untrusted routines cannot run until approved in the preview dialog.
- Preview lists every action in plain language. `close` actions and `launch` with a raw executable path outside known library items are highlighted.
- Executor: sequential, each action with a timeout; failures are reported per action and do not stop later actions unless `"stopOnError": true`.
- `close`: `CloseMainWindow` first, wait `graceMs`, then `Kill` only if `"force": true`.
- Undo closes only processes started by that run (tracked by PID + start time).
- v1 never executes scripts/shell commands and never elevates.

## 8. UI composition (Lumen.App)

- Single main window with a custom dark chrome; navigation via a `NavigationService` and view-model-first `DataTemplate`s.
- Themes as `ResourceDictionary`: `Theme.Dark.xaml` (palette tokens), `Controls/*.xaml` (styled controls). Accent is a dynamic resource updated by `AccentService`.
- Virtualized grid (`VirtualizingWrapPanel` or equivalent) for large libraries; images decoded at display size and cached.
- All user-visible text in `Resources/Strings.resx` / `Strings.es.resx`. No hard-coded strings in XAML or view models.
- Gamepad input is translated into `FocusNavigationDirection` moves and routed commands so every screen that works with the keyboard works with a gamepad.
- Global hotkey via `RegisterHotKey` on a hidden message window.

## 9. Build, CI and release

- `Directory.Build.props`: `TargetFramework` defaults, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnableWindowsTargeting=true` (lets the full solution build on non-Windows agents; WPF still only runs on Windows).
- CI (`.github/workflows/ci.yml`): on push/PR — `windows-latest`: restore, build, test.
- Release (`.github/workflows/release.yml`): on tag `v*` — `dotnet publish` self-contained `win-x64`, `vpk pack`, `vpk upload github` (pre-release tags like `v1.1.0-beta.1` go to the `beta` channel). Produces `Setup.exe`, portable zip, delta packages.
- Versioning: SemVer; `MinVer` or tag-based versioning.
