# Lumen — Architecture

Companion to [PLAN-v1.md](PLAN-v1.md). This document defines the layers, contracts and technical starting points. When implementation proves a detail wrong, fix the code **and** this document.

## 1. Solution layout

```
Lumen.slnx
Directory.Build.props          # shared settings: net10.0, nullable, warnings as errors, EnableWindowsTargeting
Directory.Packages.props       # central package versions
global.json                    # .NET 10 SDK (latest feature band allowed)
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

Rule: **everything that can be pure stays out of the Windows projects** so it builds and tests on any OS (including Linux CI agents). Windows-only access (registry, file system locations under `%ProgramData%`, processes, AppX, shell) is abstracted behind interfaces in `Lumen.Core` and implemented in `Lumen.Platform.Windows`. Providers receive those abstractions, so a provider test feeds fixture data instead of a real registry. They live in `Lumen.Core.Platform`: `IRegistryReader` (read-only), `IFileSystem` (read-only; `PhysicalFileSystem` is the real one), `ILauncher` (`ShellLauncher` in Platform.Windows, shell-execute as the current user, never `runas`).

Dependency direction: `App → Platform.Windows → Providers → Data → Core` (each may depend only on things to its right; `App` composes everything).

## 2. Key libraries

| Purpose | Library |
|---|---|
| MVVM | CommunityToolkit.Mvvm |
| DI / hosting | Microsoft.Extensions.Hosting (lower layers only take Microsoft.Extensions.Logging.Abstractions) |
| Logging | Serilog (+ file sink, + Serilog.Extensions.Hosting to plug into the host) |
| SQLite | Microsoft.Data.Sqlite + Dapper (hand-written SQL migrations) |
| JSON | System.Text.Json (+ JsonSchema.Net for validating routines/collections) |
| Tray icon | H.NotifyIcon.Wpf |
| Gamepad | Windows.Gaming.Input (via the Windows TFM) |
| Updates | Velopack |
| Steam VDF | small in-house parser (KeyValues text format is simple; keep it dependency-free and tested) |
| QR codes (Steam sign-in, M5) | QRCoder (MIT; owner-approved 2026-10-28). Pulls in System.Drawing.Common transitively; only `Lumen.App` references it, and only its PNG writer is used |
| Tests | xUnit (v2) + Shouldly |

Versions are pinned in `Directory.Packages.props`. Prefer few dependencies; every new one must be MIT/BSD/Apache/LGPL/GPL-compatible.
NuGet audit runs on restore and, with warnings as errors, a package with a known vulnerability (including transitive ones) fails the build — upgrade it, don't suppress the warning.

## 3. Domain model

```csharp
// Stable identity across machines and syncs: "{source}:{externalId}"
// e.g. "steam:570", "epic:Fortnite", "gog:1207658924", "xbox:{PackageFamilyName}!{AppId}",
//      "startmenu:{relative .lnk path, normalized}", "manual:{guid}"
public readonly record struct ItemRef(string Source, string ExternalId);   // Source: lowercase [a-z0-9]+; parsed on the first ':'

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

Implemented in M2 (`TitleNormalizer`, `LibraryMerger`, `LibraryItem`):
- Normalizer also strips accents and apostrophes, turns `&` into "and", converts roman numerals II–XX (a lone "I" is left alone), and never strips the whole title. Edition suffixes live in `src/Lumen.Providers/Data/title-cleanup.json`. The list is deliberately conservative: "Remastered"/"Remake"/"Director's Cut" are often different products and are not stripped.
- A merged item holds at most one entry per store: two entries with the same title in one store are different products.
- Apps are never merged automatically.
- `LibraryItem.Primary` (what Play/Install uses) is the user's preferred entry if it can run, else the first installed entry by store priority (Steam, Epic, GOG, Xbox, Ubisoft, EA, Battle.net, Manual), else the first that can be installed.
- `LibraryService` publishes `Entries` and `Items` together (one snapshot), and `ReloadUserChoicesAsync` re-applies decisions without rescanning.
- User decisions (`merge_rules`, migration 0005): **separate** (an entry never auto-merges) and **preferred** (store used for Play in its item), plus reset-all in Settings → Library. A tile's context menu offers "Play/Install with {store}", "Always use {store}" and "Show stores separately". **Manual merge** (M3, migration 0008: `merge_rules.merged_with`): from the detail view the user searches another item of the same kind and picks it; each of its entries gets `merged_with = <this item's primary ref>`. `LibraryMerger` joins linked groups after automatic grouping (union–find, transitive; links win over "separate"; links to entries that are gone are ignored). "Show stores separately" also clears links from and to those entries; "Reset grouping" clears everything in `merge_rules`, links included.

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

public interface IAccountConnector                 // Epic, GOG, Steam (Family Sharing) — rules in PLAN-v1 §4.8
{
    string Source { get; }
    Task<ConnectResult> ConnectAsync(CancellationToken ct);      // interactive (browser)
    Task DisconnectAsync();
    AccountStatus Status { get; }
}
```

`ProviderResult` carries entries **plus** non-fatal diagnostics and a status: `Ok`, `Partial` (some items unreadable), `NotAvailable` (store not installed), `NotConfigured` (needs key/login), `Failed`. A provider must never throw out of a scan: catch, log, return what it could read.

**Keeping the library current (M3, `LibraryAutoRefresh`):**
- **Watches** the folders providers report via `IWatchableProvider`: Steam: each library's `steamapps\appmanifest_*.acf`; Epic: `Manifests\*.item`. Read-only `FileSystemWatcher`s; nothing is ever written there.
- **Rescans when the main window is activated**, which covers stores without files to watch (GOG, Ubisoft, EA, Battle.net, Xbox) and installs finished while Lumen was in the background.
- **Debounce and spacing:** events are debounced (3 s; a download rewrites manifests many times) and automatic scans are at least 10 s apart; a change during a scan schedules one more.
- **No extra network:** automatic scans don't hit the network for cached data. `ICachingProvider`: Steam keeps its owned list for 10 min. The manual **Rescan** calls `RefreshAsync`, which invalidates those caches first.
- **Quiet UI:** `LibraryService` doesn't raise `Changed` when a scan finds the same entries (`EntryContentComparer` compares `Extra` by content). When something did change, the library reuses the tiles of unchanged items and updates grid rows in place, so covers don't reload and the scroll position stays.

`LibraryService.ScanSourceAsync(source)` rescans a single provider (used after editing manual items, so Steam isn't queried again). `LibraryService` runs every provider in parallel with a 30 s timeout per scan (and still contains a provider that throws). Per source it combines the installed scan with the owned scan: installed entries win, owned ones fill in as `NotInstalled`. A `Failed` scan falls back to the matching part of the previous cache, so a broken provider never wipes the library; `NotAvailable`/`NotConfigured` are real answers and do clear stale entries. Results are persisted per source and published sorted by title.

### Starting points per store (verify on a real machine before relying on them)

| Store | Installed detection | Launch / install |
|---|---|---|
| Steam | **Verified (M1, Windows 11):** `SteamPath` is lowercase with forward slashes; `libraryfolders.vdf` uses the `"libraryfolders" { "0" { "path" ... } }` format (the pre-2021 `"1" "D:\..."` form is also parsed); manifests have `StateFlags` (bit 4 = fully installed; others are skipped) and `LastPlayed` (unix seconds, kept in `Extra`). Install path from `HKCU\Software\Valve\Steam\SteamPath`; libraries in `steamapps\libraryfolders.vdf`; each game in `steamapps\appmanifest_{appid}.acf` (`name`, `installdir`, `StateFlags`). Owned: `IPlayerService/GetOwnedGames/v1` with `include_appinfo=1&include_played_free_games=1` (key + SteamID64). Implemented in M1 (`SteamWebApi`): Steam only accepts the key as a query parameter, so it uses a plain `HttpClient` (no `IHttpClientFactory` logging handler, which logs URLs) and errors carry only status codes. `{"response":{}}` means the profile's game details are private (scan = `Partial` + warning). 401/403 = rejected key. `playtime_forever` and `rtime_last_played` are kept in `Extra` (`playtimeMinutes`, `lastPlayed`) for M6. Settings → Steam tests the key before saving it; a rejected key is never stored. Exclude tools/redistributables (e.g. appid 228980) — list in `src/Lumen.Providers/Data/steam-excluded-apps.json`. | `steam://rungameid/{appid}`, `steam://install/{appid}` |
| Steam Family (M5) | Starting points to verify: sign-in via `IAuthenticationService/BeginAuthSessionViaQR` → show the challenge URL as a QR → `PollAuthSessionStatus` returns refresh + access tokens; refresh with `GenerateAccessTokenForApp`; revoke on disconnect. Library: `IFamilyGroupsService/GetFamilyGroupForUser` (family group id) → `GetSharedLibraryApps` (shared apps + owners). Takes an access token, **not** the Web API key; access tokens last ~24 h. Only shareable titles are returned. Undocumented by Valve: isolate behind the provider and degrade to `Failed` + cache. **Implemented in M5 (QR flow and request format verified against Steam 2026-10-28; full sign-in pending the owner's scan):** `SteamAccountApi` posts `input_json` forms to `BeginAuthSessionViaQR` (platform 1 = Steam client, device name just "Lumen") and `PollAuthSessionStatus` (Steam rotates the QR via `new_challenge_url`; `x-eresult` 9/27/29 = expired). `GenerateAccessTokenForApp` renews access tokens (memory only, refreshed 5 min before `exp`). `RevokeToken` (revoke_action Permanent) runs on Disconnect; if Steam doesn't confirm, the token is still deleted locally and the user is told how to remove the device. Family: `GetFamilyGroupForUser` → `GetSharedLibraryApps` (include_own/excluded/free/non_games = false; apps with exclude_reason ≠ 0 or app_type ≠ 1 dropped). `SteamAccount` stores the refresh token as `steam.refreshToken` (DPAPI) and the account name, SteamID64 and connection date in `settings_local` (`steam.account.*`) for Settings. `SteamProvider.ScanOwnedAsync` = owned (key) ∪ family-only games (`Extra["family"]`, badge "Steam (family)"); either part can be missing, and one failing makes the result Partial. | Same Steam URIs |
| Epic | `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON: `DisplayName`, `AppName`, `CatalogNamespace`, `CatalogItemId`, `InstallLocation`, `LaunchExecutable`). **Verified (M2, Windows 11):** manifests in the default ProgramData folder (`HKLM\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher\AppDataPath` was absent; it is only honored as an override); `AppCategories` like `games,applications`; `MainGameAppName` empty for base games; the launch URI below works. skipped: `bIsIncompleteInstall`, DLC (`MainGameAppName` ≠ `AppName`), `AppCategories` with `addons`/`engines`/`plugins`; `software` without `games` → `App`. Ref `epic:{AppName}`. Owned (login), **implemented in M5** following Legendary (pending the owner's real sign-in):
  - **Sign-in:** the user signs in on `www.epicgames.com/id/login` (redirect to `/id/api/redirect?clientId=…&responseType=code`). The page shows JSON with `authorizationCode`; the user pastes it (bare or the whole JSON).
  - **Tokens:** `POST account-public-service-prod03…/account/api/oauth/token` with Basic auth of Epic's public launcher client (id `34a02cf8…`, secret published in Legendary/Heroic; owner-approved) and `grant_type=authorization_code|refresh_token`, `token_type=eg1`. `corrective_action_required` means "act on Epic's website first".
  - **Storage:** refresh tokens rotate on every use, and the newest one is stored (`epic.refreshToken`, DPAPI). Access tokens stay in memory. `epic.account.*` in `settings_local` holds the display name, id and date.
  - **Disconnect:** `DELETE …/oauth/sessions/kill/{access token}`. That URL contains the token, so it is sent on the plain, never-logged `HttpClient`.
  - **Library:** `library-service.live.use1a…/library/api/public/items?includeMetadata=true`, following `responseMetadata.nextCursor`. Namespace `ue` and non-`APPLICATION` records are skipped.
  - **Titles:** from `catalog-public-service-prod06…/catalog/api/shared/namespace/{ns}/bulk/items?id=…` (6 in parallel). The result is cached in `metadata_cache` (migration 0011, key `epic:catalog:{catalogItemId}`), so each item is looked up once. DLC (`mainGameItem`), add-ons, engines and plugins are skipped; "software" without "games" is an app.
  - The owned list is cached for 10 min like Steam's. | `com.epicgames.launcher://apps/{namespace}%3A{catalogItemId}%3A{appName}?action=launch&silent=true`; install: same with `action=install` (verify) |
| GOG | `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\{gameID}` (`gameName`, `path`, `exe`, `launchCommand`). **Verified (M2, Windows 11):** values exist as listed; `dependsOn`/`launchParam` are present but empty for base games; Galaxy launch works with Galaxy open or closed. Keys with a non-empty `dependsOn` are DLC (skipped); a missing `path` folder means a leftover key (info, skipped). Galaxy from `...\GOG.com\GalaxyClient\paths\client` + `clientExecutable`; when its exe is missing, launch the game's `exe` with `launchParam` and `workingDir`. Ref `gog:{gameID}`. Owned (login): GOG embed API with the Galaxy client flow (research current endpoints). Owned (login), **implemented in M5** following Heroic's gogdl (pending the owner's real sign-in): the user signs in on `auth.gog.com/auth?client_id=…&redirect_uri=https://embed.gog.com/on_login_success?origin=client&response_type=code&layout=client2` and pastes the address the browser ends on (or its `code`); tokens from `GET auth.gog.com/token` with GOG Galaxy's public client id/secret (published in gogdl; owner-approved) — GOG takes them in the query string, so the plain never-logged `HttpClient` again; refresh tokens rotate (newest kept, `gog.refreshToken`, DPAPI); user name from `embed.gog.com/userData.json`; owned games from `embed.gog.com/account/getFilteredProducts?mediaType=1&page=N` (titles included, `isGame` false skipped). GOG has no revocation endpoint: Disconnect deletes locally and Settings says so. Not-installed games open in Galaxy (`goggalaxy://openGameView/{id}`, VERIFY). Owned list cached 10 min. | Galaxy: `GalaxyClient.exe /command=runGame /gameId={id} /path="{path}"`; fallback: exe directly. Install: open Galaxy to the game. |
| Xbox / MS Store | `Windows.Management.Deployment.PackageManager.FindPackagesForUser("")`; a package is a game if its install location contains `MicrosoftGame.config` (or it is registered as a game). Read AppId from the manifest. **Implemented in M2:** `IPackageCatalog` (Core) / `WindowsPackageCatalog` (reads each package property defensively; framework and resource packages skipped). Game = `MicrosoftGame.config` present (GDK) **or** family name listed in `Data/store-games.json` (older UWP games such as Solitaire; "registered as a game" has no public API). AppId = first `Application` whose `VisualElements` is not `AppListEntry="none"`. Title: resolved package display name → `ShellVisuals@DefaultDisplayName` → manifest `DisplayName`, never an unresolved `ms-resource:` key. Ref `xbox:{PackageFamilyName}!{AppId}`. Verified on Windows 11 with a UWP game (Solitaire: detected and launched); GDK detection still to verify with a real GDK game. Other Store apps are handled by the AppsFolder provider (M4). | `explorer.exe shell:AppsFolder\{PackageFamilyName}!{AppId}` |
| Ubisoft | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\{id}\InstallDir`; title from install dir / uninstall registry key `Uplay Install {id}`. **Verified (M2, Windows 11):** `InstallDir` uses forward slashes with a trailing slash; title from the `Uplay Install {id}` entry, else the folder name; the launcher's own entry is `Uplay`; a missing folder is skipped (info). Ref `ubisoft:{id}`. | `uplay://launch/{id}/0` |
| EA app | **Verified (M2, Windows 11):** DiP 4.0 `installerdata.xml` (its content id list may contain empty entries); the EA app's own uninstall entries have no `InstallLocation`; `origin2://` launches work. Uninstall entries whose `UninstallString` points to `Common Files\EAInstaller\…\Cleanup.exe` (or publisher "Electronic Arts…") and whose `InstallLocation` has `__Installer\installerdata.xml`; that file gives the content id (`contentIDs/contentID`) and a fallback title. Parsed with DTD processing prohibited and no resolver (the file is untrusted). Ref `ea:{contentId}`. | `origin2://game/launch?offerIds={contentId}`; without a content id, open `EADesktop.exe` |
| Battle.net | `%ProgramData%\Battle.net\Agent\product.db` (protobuf) or uninstall registry entries; map product codes (data file). **Implemented in M2 with uninstall entries, verified on Windows 11:** entries whose `UninstallString` runs `Agent\Blizzard Uninstaller.exe --uid={product}`. The Battle.net app itself uses the same uninstaller with `--uid=battle.net`, so it is excluded via `excludedUids` in the data file. Only `s1`→`S1` (StarCraft) has been checked so far; uid → launch code in `Data/battlenet-products.json` (variants like `wow_classic` fall back to `wow`). Unknown uid → Play opens Battle.net. Ref `battlenet:{uid}`. | `Battle.net.exe --exec="launch {code}"` |
| Start menu | `%ProgramData%\Microsoft\Windows\Start Menu\Programs` and `%AppData%\Microsoft\Windows\Start Menu\Programs`, recursive `.lnk`/`.url`. Resolve target via IShellLink. **Implemented in M4, verified on Windows 11 (owner's PC: 61 shortcuts):** `StartMenuProvider` (source `startmenu`, ref id = path relative to `Programs` with `/`, the current user's copy wins). `.lnk` read with the ShellLink object (`IPersistFile.Load` + `GetPath(SLGP_RAWPATH)`, never `Resolve`/`Save`, so nothing is rewritten); env vars expanded. Names translated through each folder's `desktop.ini` `[LocalizedFileNames]` + `SHLoadIndirectString` ("Character Map.lnk" → "Mapa de caracteres"). `.url` kept only for app protocols. Skipped: missing targets, non-file targets, junk (below), copies with the same target + arguments. Scanned on a worker thread. | Shell execute the `.lnk` (keeps its arguments and working folder) |
| AppsFolder | Enumerate `shell:AppsFolder` (packaged apps with AUMIDs). Skip games already found by the Xbox provider. **Implemented in M4, verified on Windows 11 (36 apps):** `StoreAppsProvider` (source `appsfolder`) lists the same `PackageManager` packages as the Xbox provider: not framework/resource, a visible `Application` in `AppxManifest.xml`, a name Windows resolved, and not a game (`MicrosoftGame.config` or `store-games.json`). Win32 programs already come from the Start menu, so `shell:AppsFolder` itself isn't enumerated. | `shell:AppsFolder\{AUMID}` |
| Manual | SQLite `manual_items` (migration 0004, same shape as `config.json → manualItems` for M8 sync). Targets: `.exe`, `.lnk`, `.url` files (absolute path, `%ENV%` allowed) or a URL. URL schemes that run code or abuse Windows protocol handlers are refused (`file`, `javascript`, `ms-msdt`, `search-ms`, … — list in `ManualItemValidator.BlockedSchemes`). A file missing on this PC is listed as "not found" with no action, never deleted. Added via dialog or drag & drop; drops always open the dialog so the user confirms (max 10 per drop). | `.exe` with optional arguments (working dir = exe folder), `.lnk`/`.url` shell-executed as-is, URLs via the shell |

Rules: providers only **read** store files; never write into a store's folder or registry. Junk filters and title-cleanup lists live in `src/Lumen.Providers/Data/*.json`.

**Apps (M4):**
- **Junk filter:** `Data/app-junk.json` (`AppJunkFilter`) matches whole words of the name, ignoring case and accents (uninstall, readme, help, website, runtime, updater…). It also drops targets that are uninstallers, setup programs, documents, `explorer.exe` or `control.exe`; web and store-game URL schemes; and the `Startup` / `Administrative Tools` folders.
- **Curation:** detected apps are suggestions, not library items (`AppCuration`, Core). The user's choice is stored per ref in `app_choices` (migration 0009: `added` / `ignored`, no row = suggestion). It is machine-local until the sync folder (M8) moves ignored apps to `config.json` → `ignoredApps`.
- **What the library shows:** `LibraryService.Entries` keeps everything scanned, while `Items` only include added apps.
- **Never suggested:** shortcuts to games Lumen already shows through a store (same normalized title, or a target inside the game's install folder).
- **Icons (M4):** `ILocalArtworkSource` (Core) supplies images that live on this PC. They are validated (format, ≤ 10 MB) and cached like downloads, and tried before remote sources. Two implementations:
  - `PackageLogoSource` (Providers), for Store apps and Xbox games: the manifest's `Square44x44Logo`, then `Square150x150Logo`, then the Store logo. Among the shipped variants it prefers `altform-unplated`, then the largest up to 256 px (`targetsize-N`, else `scale-N`); it skips `contrast-*` and light-theme variants and never leaves the package folder.
  - `ProgramIconSource` (Platform.Windows), for Start menu apps and manual items: the shortcut's icon (path + index), else the target `.exe`. It is read at 256 px with `PrivateExtractIcons` (the file is read as data; nothing runs, no shell extension loads) and converted to PNG by `PngEncoder` (Core, no imaging dependency). Icons without alpha use their mask.
  - Tiles show the cover when there is one, else the icon centered (apps, and Store games SteamGridDB can't name, e.g. "Solitaire & Casual Games").
  - SteamGridDB only looks up games (`Kind == Game`), never apps.
- **Copies of one app** (same target and name, e.g. Discord's two Start menu shortcuts with different arguments) count once: they're never suggested when one copy is in the library, and listed once otherwise.

## 5. Services (Lumen.Core)

| Service | Responsibility |
|---|---|
| `LibraryService` | Runs providers (in parallel, with timeouts), merges entries, applies overrides, persists, raises change events |
| `LaunchService` | Executes `LaunchSpec`, records "last launched", hands off to `PlaytimeTracker` |
| `PlaytimeTracker` | Every ~5 s lists processes (`QueryFullProcessImageName`, no admin) and matches image paths under the item's install dir; a session starts on first match and ends after ~30 s with no match; persists sessions. **Implemented in M6:** watching starts when `LaunchService` plays an installed entry with a folder, and the 5 s timer only runs while something is watched (~11 ms per poll on the owner's PC, 184 readable processes). Path matching uses the folder plus a trailing `\`, so `Sample` doesn't match `SampleTwo`. A watch is dropped if nothing appears within 5 min. Sessions are written at start and extended every minute (`play_sessions`, migration 0012, per machine, never synced); sessions a closed Lumen left open are closed at their last saved time on the next start; sessions under 1 min are dropped. Totals reach entries as `Extra["lumenMinutes"/"lumenLastPlayed"]` when items are built. `LibraryFilter.PlaytimeMinutes` uses the store's figure when there is one (Steam), else Lumen's, so nothing counts twice. Last played is the later of the two. The detail view says where the hours come from (store / Lumen / both). |
| `ArtworkService` | Resolves cover/hero/logo/icon: override → local sources (package logos, program icons; M4) → Steam CDN (Steam items) → SteamGridDB (games only; search by title, cache the SGDB game id) → the tile's initial as placeholder. Disk cache in `%LOCALAPPDATA%\Lumen\cache\artwork`. |
| `AccentService` | Extracts a dominant, saturated color from hero/cover art (quantize + score by chroma and population), ensures WCAG contrast vs. the dark background. Implemented in M3: pure logic in `Lumen.Core.Theming.AccentPicker`, WPF side in `Lumen.App.Services.AccentService` |
| `SecretStore` | DPAPI with CurrentUser scope (`ISecretProtector`; `DpapiProtector` calls `CryptProtectData` directly, with app-specific entropy, so no extra package) protected values in the SQLite `secrets` table (`ISecretStore` / `SqliteSecretStore`). A value that no longer decrypts (other user or machine) reads as not set. Never logged, never synced. Machine-local non-secret values (e.g. SteamID64) go to `settings_local` via `ILocalSettings`. |
| `RoutineEngine` | Loads/validates routines, trust checks, executes actions sequentially with cancellation, tracks started processes for undo |
| `SyncFolderService` | Watches the sync folder (`FileSystemWatcher` + debounce + periodic rescan fallback), loads routines/collections/config, writes changes back atomically (write temp → rename) |
| `UpdateService` | Velopack: check, download, apply on restart; channel selection |

Steam CDN artwork (**verified 2026-10**): `https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appid}/…` (current) and `https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/…` (fallback) both serve `library_600x900.jpg`, `library_hero.jpg`, `logo.png`, `header.jpg`. Cover falls back to `header.jpg` for old games.

`ArtworkService` as implemented (M3):
- **Order:** user override → disk cache → sources in registration order (Steam CDN, then SteamGridDB). For a merged item, the primary entry is tried first, then the item's other stores.
- **Sources:** pluggable async `IArtworkSource`s returning `ArtworkCandidates` (URLs on allowed hosts only, plus `Definitive`). "Unavailable" (not configured, offline, rate-limited, rejected key) never records a miss. A source that throws is contained.
- **Overrides:** "Change cover…" copies a user-picked image (validated JPEG/PNG/WebP, ≤ 10 MB) to `%LOCALAPPDATA%\Lumen\artwork-overrides\…`, outside the cache, so "Clear artwork cache" keeps it. "Restore original cover" deletes it. Only the edited tile is refreshed (scroll position kept).
- **Cache:** `%LOCALAPPDATA%\Lumen\cache\artwork\{source}\{sha256(ref)[..16]}\{kind}.{jpg|png|webp}`, written atomically (temp → move).
- **Safety:** downloads are limited to 4 at a time and deduplicated per key. Images are capped at 10 MB, and the bytes must be JPEG/PNG/WebP by their magic numbers. Anything else is discarded, never written as an image.
- **Misses:** a definitive "not found" (every candidate answered 404/403/410 or a non-image) is remembered for 7 days in a `.miss` file. Network errors and 5xx are retried on the next request.
- **UI:** covers load lazily when a tile is realized and are decoded at display size (`DecodePixelWidth`). The grid is a virtualized list of rows (WPF has no virtualizing wrap panel), so memory stays flat as the library grows.
SteamGridDB title search (non-Steam games) accepts only an exact normalized name match. When the full title has none, it retries with the part before the first subtitle separator (` - `, ` – `, ` — `, `: `), e.g. "Home - A Unique Horror Adventure" → "Home" (M4, reported by the owner). Settings → "Clear artwork cache" also clears remembered misses, so existing libraries pick this up.

SteamGridDB API v2: `https://www.steamgriddb.com/api/v2` with `Authorization: Bearer {key}` — `/search/autocomplete/{term}`, `/grids/game/{id}?dimensions=600x900`, `/heroes/game/{id}`, `/logos/game/{id}`, `/icons/game/{id}`.

As implemented (M3, `SteamGridDbClient` / `SteamGridDbArtworkSource`):
- **Key handling:** the key only travels in the Authorization header (never in a URL), through a plain `HttpClient` with no logging handler. It is tested with a search before it's saved, stored with DPAPI, never shown back.
- **Finding the game:** Steam entries use `/games/steam/{appid}`; others search by title and only accept a result whose normalized title matches exactly, so a game never gets the wrong art. The SteamGridDB id (or "not found", retried after 30 days) is cached in `artwork_lookups` (migration 0006).
- **Images:** requests add `types=static&nsfw=false&humor=false&epilepsy=false`. Only `https` URLs on `steamgriddb.com` / `*.steamgriddb.com` are accepted (images come from `cdn2.steamgriddb.com`), top 3 per kind.
- **Live API checked (2026-10):** without a key every endpoint answers `401 {"success":false,"errors":["Authentication Required"]}`; a wrong key is rejected the same way, and Settings shows "rejected".
- Saving a key clears remembered misses and lookups so items without art are retried immediately.

## 6. Data

### Local — SQLite at `%LOCALAPPDATA%\Lumen\lumen.db`
Tables (initial; M2 note: `items`/`item_entries` were not needed. Items are computed in memory from `entries` + `merge_rules` on every change, which keeps one source of truth and is fast for thousands of entries): `entries` (provider output cache), `items`, `item_entries`, `merge_rules`, `overrides`, `playtime_sessions`, `steam_playtime`, `ignored_apps`, `secrets`, `routine_trust` (routine id → sha256 + approved_at), `settings_local`, `schema_version`.

Migrations are hand-written SQL files embedded in `Lumen.Data` as `Migrations/NNNN_lower_snake_name.sql` (versions contiguous from 1). `MigrationRunner` creates `schema_version` itself, applies each pending migration in its own transaction on startup (before the main window opens), and refuses a database whose version is newer than the app knows. `0001_initial` only creates `settings_local`; every other table is added by the milestone that first uses it, so its columns are designed with real code in hand. Connections use WAL, `foreign_keys=ON` and a 5 s busy timeout.

`item_overrides` (migration 0008): per-entry custom `title` (1–120 chars, NULL = the store's) and `hidden`, keyed by `ItemRef` like `config.schema.json`'s `overrides` (machine-local until sync in M8). A rename or hide is written to every entry of the item; when building items, the primary entry's title wins, then any other's, and an item is hidden if any entry is. Rows with no title and not hidden are deleted. Merging keeps the name and visibility of the item the user started from (copied to the joined entries). Searching and sorting use the custom title; SteamGridDB lookups keep using store titles.

`app_choices` (migration 0009): see "Apps (M4)" in §4.

`entry_first_seen` (migration 0007): when each `ItemRef` first appeared, written with `INSERT OR IGNORE` on every cache replace (never overwritten, kept after uninstalls); drives "Recently added". Existing rows were backfilled with their `updated_at`.

Library search/filters/sort (M3, `LibraryQuery` + `LibraryFilter` in Core, pure): search is case-, accent- and symbol-insensitive and also matches on normalized titles ("witcher iii" finds "The Witcher® 3"); filters are type, store (matches any store of a merged item) and installed state; hidden items are a separate view (`LibraryQuery.Hidden`, the "Hidden (n)" button, only shown once something is hidden), not a filter; sorts are name, last played (store data for now, Lumen's own in M6), playtime (store data, summed), recently added. The chosen sort is remembered in `settings_local` (`library.sort`); filters reset each start. Tiles are reused across filter changes, so covers stay loaded.

`entries` (migration 0002): one row per provider entry, primary key `(source, external_id)`; enums stored as text (`Game`, `Installed`, ...), launch/install specs and `Extra` as JSON. Rows a newer Lumen wrote with unknown values are skipped on read. A source is always replaced as a whole, in one transaction.

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
- Virtualized grid for large libraries: rows of tiles in a recycling `VirtualizingStackPanel`.
  - **`ImageCache`** (M5, after a stutter report with ~370 games) decodes tile covers and icons off the UI thread, at their on-screen pixel size (display scaling from the main window). It keeps the most recent ones, up to 96 MB, so scrolling back over them costs nothing. A cover decode measured ~5 ms on the owner's PC, so a row of 7 was ~35 ms per frame.
  - Tiles never hold bitmaps themselves.
  - The tile context menu is built when it opens, not per tile.
- All user-visible text in `Resources/Strings.resx` / `Strings.es.resx`. No hard-coded strings in XAML or view models.
- Gamepad input is translated into `FocusNavigationDirection` moves and routed commands so every screen that works with the keyboard works with a gamepad.
- **Home (M3):** the start page. `HomeFeed` (Core, pure) picks "Continue playing" (visible games with a known last-played date, newest first, up to 12) and the featured game (the most recently played; with no history, the newest installed game). Hidden items and apps never appear. The featured game's hero (or cover) fills the page blurred (decoded at 640 px, two layers crossfading); logo (or title), playtime, last played, Play and Details over it. Pointing at or focusing a tile in the row features it after 180 ms. Details opens the game in the library's detail view and Back returns to Home. The row has no scrollbar: the wheel scrolls it sideways. Below it (M4):
  - **Favorites:** favorite games, in the order they were marked, up to 24.
  - **Quick apps:** a compact dock of app icons that open the app with one click. It shows favorite apps; with none marked, the library's apps by name (up to 12).

  `HomeFeed` builds both rows. Hidden items never appear, and both rows hide when empty. Pinned routines join in M8.
- **Dynamic accent (M3):** `AccentPicker.Pick` quantizes a 64-px-wide decode to 4 bits per channel, ignores transparent pixels, specks under 0.5 %, low chroma (< 0.15) and very dark/light buckets (HSL L < 0.12 or > 0.8), and scores by population × chroma² × closeness to mid lightness. `AccentPicker.Fit` keeps hue/saturation and moves lightness so white text on the accent is ≥ 4.5:1 and the accent on the background is ≥ 3:1. `AccentService` swaps the theme's frozen accent brush for one animatable copy and fades it (450 ms). The accent follows what is on screen: Home's featured game, an open detail view, otherwise the default. A toggle in Settings ("theme accents on/off", §4.7) is still to do.
- **Collections & favorites (M4):**
  - **Model:** `Collection` (Core) mirrors `collection.schema.json`: the id is a slug of the name (`^[a-z0-9][a-z0-9-]{0,63}$`, "-2" when taken), the name is 1–80 chars, items are entry refs. Favorites is the reserved `favorites` collection, created by migration 0010; it can't be renamed or deleted and shows translated.
  - **Merged games:** a merged game is stored with every store's ref, so it stays in a collection whichever store Play uses; `Contains` matches any of its entries.
  - **Storage:** `collections` + `collection_items` (position = order added, `ON DELETE CASCADE`), machine-local until the sync folder (M8) mirrors them as `collections/*.json`.
  - **`CollectionService`** (Core) holds them in memory and raises `Changed` for every page.
  - **UI:**
    - Collections page: cards with Open / Rename / Delete (deleting never touches the games) and "Create collection". Open shows the collection in the library.
    - Library: a "Collection" filter (`LibraryQuery.Collection`).
    - Detail view: a favorite star button and More → "Collections…" (a checkbox per collection, "Create and add").
    - Tiles: "Add to / Remove from favorites" in the menu and a small star on favorites; only tiles whose star changed are refreshed.
- **Discover apps (M4):** `DiscoverAppsViewModel` lists suggestions (or the ignored ones), with a search box and Add / Ignore / Suggest again buttons. It opens over the library from the header button "Discover apps · n" and is also a step of the first-run wizard (same view). Added apps show "Open" instead of "Play"; their tile menu has "Remove from library" (back to suggestions). Store badges: "Windows" (Start menu) and "Microsoft Store".
- **First-run wizard (M3):** `FirstRunViewModel`, shown over the whole window (sidebar included) while `settings_local` has no `firstRun.completed = 1`; "Skip" and "Start using Lumen" both set it, and Settings → General → "Show the welcome again" reopens it. Steps: **language** (same list as Settings) → **stores** (`StoreFindings` in Core summarizes `LibraryService.LastFullScan`: found / empty / problem / not installed, installed count, Steam's owned-not-installed count; "Looking for your stores…" until the first full scan ends, refreshed on `FullScanCompleted`) → **optional keys** (the same `SteamSettingsCard` / `ArtworkSettingsCard` controls as Settings) → **apps** (Discover apps, M4) → **done**. Sign-ins (M5) will be added as a step by that milestone.
- **Language (M3):** "Same as Windows", Español, English (names in their own language), in Settings → General and the wizard. Stored in `settings_local` as `ui.language` (`system`/`en`/`es`) until the sync folder (M8) moves it to `config.json` → `language`. Applied before the main window opens and switchable live. Windows' language is captured at startup (`LocalizationService.SystemUiCulture`), so "Same as Windows" is right after trying another language.
- **Detail view (M3):** the cover is a button (click/Enter) that opens `GameDetailViewModel` over the library page; the library keeps its scroll, search and filters underneath. Esc/Back closes it.
  - Shows: hero (fading into the page), logo (falls back to the title), Play/Install, "Play with" store selector for merged games (choosing a store saves it as the preferred one), stores, playtime, last played and location.
  - Actions: open the install folder (`explorer.exe "<folder>"`, only when it exists) and the store page inside the store's own app (`steam://store/{id}`, `ms-windows-store://pdp/?PFN=…`; other stores have no reliable link and hide the button). Under "More": change cover/background/logo (overrides), restore originals, show stores separately.
  - Under "More" too: rename (inline box; empty or the store's name restores it), restore original name, hide / show again (a hidden item's detail says so, with a button to undo), merge with another item (inline search of the library, same kind, up to 30 results). The tile menu also has hide / show again.
  - **Uninstall** (owner-approved addition to PLAN-v1 §4.3): shown when a copy is installed (the selected store's first). Lumen hands it over and deletes nothing, runs no uninstaller and never asks for admin (security rules 2–3): Steam `steam://uninstall/{appid}` and Ubisoft `uplay://uninstall/{id}` open the store's own confirmation; Epic opens its library (`com.epicgames.launcher://store/library`, no per-game uninstall link); GOG, EA, Battle.net and Xbox open Windows Settings → Apps (`ms-settings:appsfeatures`). A notice says what to do next; the auto refresh picks up the result. Manual items have no uninstall ("Remove" takes them out of Lumen). Links taken from Playnite's store plugins. **Verified (owner, Windows 11, 2026-10):** Steam and Ubisoft open their own uninstall flow for the game, Epic opens its library, GOG/EA/Battle.net/Xbox open Settings → Apps.
- **Accessibility:** buttons whose content is icon + text set `AutomationProperties.Name`; dropdown options override `ToString()` with their label. Clickable things that share a grid cell must be checked for hit-testing (UI Automation `Invoke` bypasses it).
- **Tray (M7):** `TrayService` puts an H.NotifyIcon `TaskbarIcon` in the notification area (app icon `Assets/Lumen.ico`, also the window and exe icon).
  - Left click opens Lumen.
  - The menu is built each time it opens: Open, Quick search, up to 5 recently played games (the same list as Home's "Continue playing", only ones that can run; a click launches them like Play) and Exit. Routines join in M8.
  - Efficiency mode stays off (`ForceCreate(false)`), so playtime polling isn't throttled while the window is hidden.
  - **Minimize to tray** (Settings → General, on by default, `settings_local` `ui.closeToTray`): closing the window hides it; the first time, a notification explains where Lumen went (`ui.trayHintShown`). Exit in the menu really closes Lumen. With the setting off, closing the window exits as before. `ShutdownMode` is `OnExplicitShutdown`; `App.ShutdownAsync` is the only way out.
  - **Start with Windows** (Settings → General, off by default): the string value `Lumen` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` = `"<Lumen.exe>" --tray` (`WindowsStartupRegistration`). Per user, no admin, also listed in Task Manager → Startup apps; the checkbox reads it from the registry each time. Only Lumen's own value is touched. `--tray` (`StartupOptions`) starts Lumen without opening the window.
  - **One Lumen at a time** (`SingleInstance`): a named mutex per Windows session and install folder (SHA-256 of the folder, so the path isn't in the name). Opening Lumen again signals the running copy (named event) to show its window, then exits. A copy in another folder (portable, development build) runs separately.
- **Quick search (M7):** a global shortcut (default `Alt+Shift+Space`) opens a small box over any app; type, Enter runs the result.
  - **Shortcut:** `HotkeyService` registers it with `RegisterHotKey` on a message-only window (`GlobalHotkeys` in Platform.Windows, with `MOD_NOREPEAT`). Windows only reports that exact combination: no keyboard hook, nothing else typed is seen, no admin.
  - **Changing it:** Settings → General → "Change" records the next combination (Esc cancels); "Restore default" goes back. `Hotkey` (Core) is the model and the saved text (`settings_local` `ui.hotkey`, e.g. `Ctrl+Alt+K`). It needs Ctrl, Alt or Win (F-keys alone are fine) plus A–Z, 0–9, F1–F24 or Space.
  - **Shortcut taken:** if another app owns it, Windows refuses; Lumen keeps the previous one (or none) and Settings says so.
  - **Results:** `QuickSearch` (Core) matches like the library search (accents, case, symbols, roman numerals), then ranks:
    1. title starts with the text
    2. a word starts with it
    3. anywhere

    Ties go to installed items, then the most recently played. Hidden items never appear, at most 8 results, and with no text it lists recently played items. Only items that can run are listed; not-installed games say so, and Enter installs.
  - **Window:** `QuickSearchWindow` is borderless, topmost and not in the taskbar. It opens centered in the upper part of the main screen with the text field focused. ↑/↓ choose, Enter or click runs, Esc or clicking elsewhere hides it. It is reused, never rebuilt.
  - The tray menu has "Quick search (shortcut)". The gamepad's Y joins in the gamepad PR.

## 9. Build, CI and release

- `Directory.Build.props`: `TargetFramework` defaults, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnableWindowsTargeting=true` (lets the full solution build on non-Windows agents; WPF still only runs on Windows).
- CI (`.github/workflows/ci.yml`): on push/PR — `windows-latest`: restore, build, test.
- Development needs Windows with the .NET 10 SDK (`global.json`); the user-level install from Microsoft's `dotnet-install` script needs no admin. Logic in Core/Data/Providers also builds and tests on Linux/macOS thanks to `EnableWindowsTargeting`, but the WPF app only runs on Windows.
- Localization: `Lumen.App/Resources/Strings.resx` (en) + `Strings.es.resx` (es), read through `LocalizationService`; XAML uses `{l:Loc Key}`, which follows runtime language changes. `LanguageResolver` (Core) maps the `language` preference to a culture. A repository test fails if the two resx files have different keys or a XAML `{l:Loc}` key is missing.
- Release (`.github/workflows/release.yml`): on tag `v*` — `dotnet publish` self-contained `win-x64`, `vpk pack`, `vpk upload github` (pre-release tags like `v1.1.0-beta.1` go to the `beta` channel). Produces `Setup.exe`, portable zip, delta packages.
- Versioning: SemVer; `MinVer` or tag-based versioning.
