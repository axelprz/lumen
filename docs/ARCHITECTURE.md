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

### Where to find things

| Area | Code (all under `src/` unless noted) | Notes |
|---|---|---|
| Entry point, DI | `Lumen.App/Program.cs` (Velopack hook, then WPF), `App.xaml.cs`, `Hosting/AppHost.cs` | Everything is a singleton registered in `AppHost` |
| Library, merging, search | `Lumen.Core/Library/*` | `LibraryService`, `LibraryMerger`, `QuickSearch`, `HomeFeed`, `Collections` |
| Store parsing | `Lumen.Providers/*` (one folder per store) | Fixtures in `tests/fixtures/<area>` |
| SQLite | `Lumen.Data/*`, migrations `Lumen.Data/Migrations/0001–0013` | DB version 13; a newer DB is refused |
| Artwork | `Lumen.Core/Artwork/*`, `Lumen.Providers/SteamGridDb`, `Lumen.App/Services/ImageCache.cs` | |
| Routines | `Lumen.Core/Routines/*`, `Lumen.App/ViewModels/Routine*.cs` | §7 |
| Sync folder | `Lumen.Core/Sync/*`, `Lumen.App/Services/SyncCoordinator.cs`, `ViewModels/SyncSettingsViewModel.cs` | §6 "Sync folder" |
| Updates, release | `Lumen.Core/Updates/*`, `Lumen.Platform.Windows/Updates`, `.github/workflows/release.yml` | §9 |
| Windows access | `Lumen.Platform.Windows/*` (registry, processes, DPAPI, hotkeys, gamepad, window interop) | Keep thin |
| UI | `Lumen.App/Views`, `ViewModels`, `Themes`, `Resources/Strings*.resx` | §8. UI font Exo 2 in `Assets/Fonts` (embedded resources, OFL); Motion: `Lumen.App/Services/Motion.cs` (durations, "Animation effects" check, page-in, `Motion.HoverScale`); Mica: `Platform.Windows/Windowing/SystemBackdrop.cs` + `Lumen.App/Services/MicaBackdrop.cs` (swaps `Lumen.Brush.Backdrop`/`SidebarBackdrop` for translucent ones); type scale tokens `Lumen.FontSize.*` and `Lumen.Text.*` styles in `Themes/Theme.Dark.xaml` |
| Tests | `tests/Lumen.{Core,Data,Providers}.Tests` | Fakes for sync in `Core.Tests/Sync/SyncFakes.cs` |

## 2. Key libraries

| Purpose | Library |
|---|---|
| MVVM | CommunityToolkit.Mvvm |
| DI / hosting | Microsoft.Extensions.Hosting (lower layers only take Microsoft.Extensions.Logging.Abstractions) |
| Logging | Serilog (+ file sink, + Serilog.Extensions.Hosting to plug into the host) |
| SQLite | Microsoft.Data.Sqlite + Dapper (hand-written SQL migrations) |
| JSON | System.Text.Json. Routines, collections and `config.json` are validated by hand-written readers (no JsonSchema.Net), so no extra dependency |
| Tray icon | H.NotifyIcon.Wpf |
| Gamepad | Windows.Gaming.Input (via the Windows TFM) |
| Updates | Velopack 1.2.161 (MIT, pinned 2026-10-30). Only `Lumen.Platform.Windows` and `Lumen.App` (the `Program.Main` hook) use it. |
| Steam VDF | small in-house parser (KeyValues text format is simple; keep it dependency-free and tested) |
| Built-in browser (Steam sign-in, M5) | Microsoft.Web.WebView2 (BSD-3-Clause; owner-approved 2026-10-29). Only `Lumen.App` references it. It uses the WebView2 runtime that ships with Windows 11. Replaced QRCoder when the QR sign-in was removed |
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
| Steam Family (M5) | **Current (2026-10-29):** sign-in on Steam's own website in a built-in browser (see *Steam family sign-in* below the table). Family games come from `IFamilyGroupsService/GetFamilyGroupForUser` → `GetSharedLibraryApps` (include_own/excluded/free/non_games = false; apps with exclude_reason ≠ 0 or app_type ≠ 1 dropped). They are added as not-installed entries with `Extra["family"]` and the badge "Steam (family)". Games the user owns win. `SteamProvider.ScanOwnedAsync` = owned ∪ family-only games; either part can be missing, and one failing makes the result Partial. **History:** the first version (M5, 2026-10-28) signed in by QR as the Steam *mobile app* (`IAuthenticationService` QR flow, refresh token in DPAPI). Minutes later Steam flagged the owner's account as accessed by an unexpected device and restricted it (lifted after contacting support). It was turned off (#35) and replaced by the website sign-in (#38); see security rule 8. The old `SteamAccount`/`SteamAccountApi` code only remains to revoke a refresh token stored by that version (`App.RetireSteamSignInAsync`, at start-up). | Same Steam URIs |
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

#### Steam family sign-in

Sign-in on Steam's own website, as Playnite does (2026-10-29).

- **Where:** Settings → Steam family → "Sign in on Steam's website" opens `SteamWebSession`: a WebView2 window on `store.steampowered.com/login`. The user signs in there (QR with the Steam app, or their account); Lumen never reads that page. The browser profile is Lumen's own and only for Steam (`%LOCALAPPDATA%\Lumen\webview\steam`); its cookies are protected by WebView2 with the Windows account's keys.
- **Hardening:**
  - top-level navigation limited to https `store/login/help/checkout.steampowered.com` and `steamcommunity.com`
  - pop-ups blocked
  - no dev tools, password saving, autofill, context menu, host objects or web messages
- **Getting the token:** once on a store page that isn't `/login`, Lumen runs one read-only script (`SteamStorePage.ReadConfigScript`) that reads `#application_config`'s `data-userinfo` and `data-store_user_config`. The `webapi_token` there is the access token Steam's store page gives the browser.
- **Family games:** `SteamFamilyAccess` calls `IFamilyGroupsService/GetFamilyGroupForUser` + `GetSharedLibraryApps` with it, the same requests as Playnite's `FamilyGroupsService`.
- **Own library too:** while signed in, `SteamProvider` reads the user's own owned games with the same token (`IPlayerService/GetOwnedGames` with `access_token`, as Playnite's `GetOwnedGamesWeb`). The Web API key is only a fallback. On 2026-10-29 the owner's key started returning 401, most likely revoked by Steam along with the account restriction.
- **Token lifetime:** in memory only, reused until 10 min before its `exp`. A new one comes from opening the store page in an invisible browser (a hidden popup window, never shown), about once a day.
- **When Steam stops accepting the session:** the card asks to sign in again.
- **Stored:** only the account's SteamID64, display name and sign-in date (`settings_local` `steam.web.*`).
- **Disconnect:** runs the store page's own `Logout()` (ends the session at Steam), then deletes the browser's cookies and data. If Steam's sign-out can't run, the card says so and how to end the session in Steam.

Rules: providers only **read** store files; never write into a store's folder or registry. Junk filters and title-cleanup lists live in `src/Lumen.Providers/Data/*.json`.

**Apps (M4):**
- **Junk filter:** `Data/app-junk.json` (`AppJunkFilter`) matches whole words of the name, ignoring case and accents (uninstall, readme, help, website, runtime, updater…). It also drops targets that are uninstallers, setup programs, documents, `explorer.exe` or `control.exe`; web and store-game URL schemes; and the `Startup` / `Administrative Tools` folders.
- **Curation:** detected apps are suggestions, not library items (`AppCuration`, Core). The user's choice is stored per ref in `app_choices` (migration 0009: `added` / `ignored`, no row = suggestion). With a sync folder, the ignored apps travel in `config.json` → `ignoredApps` (apps the user added stay per PC).
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
| Routines (`Lumen.Core/Routines`) | **Implemented (M8).** `RoutineParser` validates, `RoutineJson` hashes, `RoutineSafety`/`RoutineReview` decide what may run, `RoutineRunner` executes sequentially and tracks started processes for undo, `RoutineService` owns the folder, trust and runs, `RoutineLookup` picks what the tray/search/Home offer. See §7. |
| `SyncFolderService` | **Implemented (M8, 2026-10-30).** Keeps `config.json` and `collections/*.json` in step with this PC both ways and announces changes in `routines/`. Details in *Sync folder* below. |
| `UpdateService` | **Implemented (M9, 2026-10-30).** Checks GitHub Releases at start-up and daily, offers the update, downloads when the user asks, installs on the user's restart. Details in *Updates* under §9. |

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

`item_overrides` (migration 0008): per-entry custom `title` (1–120 chars, NULL = the store's) and `hidden`, keyed by `ItemRef` like `config.schema.json`'s `overrides` (travel in `config.json` → `overrides` with a sync folder). A rename or hide is written to every entry of the item; when building items, the primary entry's title wins, then any other's, and an item is hidden if any entry is. Rows with no title and not hidden are deleted. Merging keeps the name and visibility of the item the user started from (copied to the joined entries). Searching and sorting use the custom title; SteamGridDB lookups keep using store titles.

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

### Sync folder (M8)

`Lumen.Core/Sync`: `PortableConfigJson` / `PortableCollectionJson` (read, write, hash), `LocalPortableState` (this PC's side), `SyncMerge`, `SyncFolderService`, `PhysicalFolderWatcher`.

- **What is synced:**
  - `config.json`: `language`, `ui.minimizeToTray`, `quickSearch.hotkey`, `manualItems`, `overrides` (title, hidden) and `ignoredApps`.
  - `collections/{id}.json`: every collection, and Favorites as the reserved id `favorites`.
  - No schema change was needed (`ignoredApps` was already there and Favorites is a collection).
  - Never synced: secrets, tokens, playtime, caches, routine approvals, and the sync folder path itself (`settings_local` `sync.folder`).
  - Added apps stay per PC (a detected app only exists on the PC that found it).
- **Keys Lumen doesn't manage** (`stores`, `ui.dynamicAccent`, `updates`, artwork choices, `icon`/`sort` of a collection) are kept when it rewrites a file. Reading ignores them, writing copies them.
- **Same content, same text:** files are written with sorted keys, so Lumen only writes when the text really changes, and a hash of the managed content (`Hash`) decides whether two sides differ, whatever the file's formatting.
- **One pass (`ReconcileAsync`)**, per document (`config`, `collection:{id}`). `baseline` is the hash last seen in step, kept in `settings_local` `sync.baseline`, with no migration:

  | Folder | This PC | Result |
  |---|---|---|
  | = this PC | | in step; baseline updated |
  | changed | unchanged | applied here |
  | unchanged | changed | written to the folder (or the file deleted) |
  | changed | changed | **the folder wins**; a deletion in the folder never beats an edit here |

- **What protects the user's data:**
  - A folder that is missing (`FolderUnavailable`) is not touched.
  - A folder with no config and no collections "looks wiped": nothing is deleted here, and this PC restores it. Deleting `config.json` doesn't wipe the PC either.
  - A file that can't be read (half written by a cloud client, bad JSON, id different from the file name) or comes from a newer schema is left alone and listed in `Problems`.
  - Only `config.json` and `collections/{id}.json` are written, atomically (temp then rename), through `IUserFiles` limited to the chosen folder.
- **First start (`InspectAsync` / `AttachAsync`):**
  - If only one side has data, it is used automatically.
  - If both do, the caller must pass a `SyncStart`: *UseFolder* (this PC takes the folder's data; what only this PC had is removed), *UseThisPc* (the folder takes this PC's data; Lumen's keys in `config.json` are emptied, other keys stay) or *Merge* (everything joined, the folder wins on the same thing; collections keep the folder's name and order, then what only this PC had).
  - The three are the normal pass with a prepared baseline, so there is no separate code path.
- **Watching:** a `FileSystemWatcher` (ignores `.tmp`), a 1 s debounce, and a rescan every 60 s because cloud clients don't always raise events. `NotifyLocalChanged()` wakes the loop when something local changes. `Applied` tells the pages what to reload; `RoutinesChanged` fires when something in `routines/` changes.
- **Manual items from the folder (owner decision, 2026-10-30):** imported only if they pass the same rules as the "add item" dialog (`ManualItemValidator`: `.exe`/`.lnk`/`.url`, absolute path, no dangerous URL schemes). Anything refused is skipped and listed. `SyncAppliedEventArgs.ManualItemsReceived` gives the titles so the UI can say "N items came from the sync folder". They never run on their own, only on the user's click.
- **Routines:** while sync is on, `RoutineService.Folder` is `<sync>/routines` (`SetFolder`); otherwise Lumen's own folder. Trust is unchanged: approvals are per PC (`routine_trust`, per id and hash), so a routine that arrives or changes from another PC needs review here.
  - `RoutineFolderMigration.CopyMissingAsync` only adds. Turning sync on copies this PC's routines into the folder, and turning it off (or switching folders) copies the folder's routines back, so none disappear.
  - A routine copied as it is stays approved. When both sides have a routine with the same id but different steps, both are kept and the copy gets a new id (`work-2`); that one needs review again. Nothing is overwritten.
- **Choosing the folder (`SyncFolderRules`):** the sync folder is the one place outside Lumen's own folders where Lumen writes, and only `config.json`, `collections/*.json` and `routines/*.json`. It must be a full path, not a drive or share root, and not under Windows, Program Files or ProgramData. `PhysicalUserFiles.AllowFolder` adds it as an allowed root (and `ForgetFolder` removes it); at start-up a saved folder that fails the rules is ignored.
- **In the app (`SyncCoordinator`, `SyncSettingsViewModel`, Settings → Sync folder):**
  - The card shows the folder, the status (up to date and when / folder not available / failed / off), *Choose folder…*, *Sync now* and *Stop syncing*. Files the last pass skipped are listed under *Details*.
  - If both the folder and this PC have data, an inline choice appears (combine, recommended / use the folder / use this PC) with a count of what each side has. The two that remove data ask for a confirmation that says what will be removed.
  - When something arrives, the app reloads without a restart: language, minimize to tray, hotkey, manual items (rescan of the `manual` source), renames, hidden games, ignored apps and collections. Manual items that arrived show a notice in the card and a tray balloon.
  - Changes made here (collections, library choices, language, hotkey, minimize to tray) wake the engine, which writes them after a 1 s pause.

## 7. Routine execution & trust

Implemented in M8 (engine: `Lumen.Core/Routines`).

- **Reading (`RoutineParser`):** hand-written validation against `routine.schema.json`, without JsonSchema.Net, so no new dependency.
  - Refused: unknown or repeated keys, wrong types, out-of-range values, files over 256 KB, comments and trailing commas.
  - Every problem is listed at once. A file with any error can never run.
  - Tightened from the first schema draft: `args` / `workingDirectory` only go with `path`. A library item opens the way its store does.
- **Hash (`RoutineJson.Hash`):** parse → re-write with sorted keys and no whitespace → SHA-256. Reformatting or reordering keys keeps a routine trusted; any change of meaning doesn't. Lumen's own writer (`RoutineJson.Serialize`) leaves defaults out, so the hash is stable from the first save.
- **Trust (`IRoutineTrustStore`, SQLite `routine_trust`, migration 0013, never synced):**
  - Approval stores id → hash on this PC.
  - `Trusted` = the file's hash matches; `NeedsReview` = new, or changed outside Lumen; `Invalid` = errors or a blocked step.
  - Only trusted routines run. Approval happens after the full preview.
- **Owner-approved limits (2026-10-28, `RoutineSafety`):**
  - **Open:** library items, or any `.exe` given by full path.
    - Never shells, script hosts, interpreters, or Windows tools that run other code, install, elevate or change the system: `cmd`, PowerShell, `wscript`, `mshta`, `python`, `node`, `java`, `rundll32`, `msiexec`, `reg`, `schtasks`, `explorer`… (full list in code). The same rule applies to a library app that is one of those (e.g. the Start menu's PowerShell).
    - Never `.bat`, `.cmd`, `.ps1`, `.vbs`, `.lnk` or any non-`.exe` file.
    - Never 8.3 short names (`POWERS~1.EXE`), device or `\\?\` paths.
    - Paths are normalized (`..`) before checking.
    - Right before starting, the exe's version-info original name is checked too, so a renamed `cmd.exe` is still refused.
  - **Close:** library items (processes in their install folder) or programs by name. Never Windows' own processes (`explorer`, `dwm`, `svchost`, `csrss`…) or Lumen, even when found through a folder.
  - A blocked step makes the whole routine impossible to approve.
- **Preview (`RoutineReview`):** every step in order with variables and `%ENV%` already expanded. It flags:
  - programs outside the library
  - close and force-close steps
  - items missing from this PC's library
  - missing files and folders
  - blocked steps
- **Runner (`RoutineRunner`):**
  - **Order and errors:** sequential, each step with a time limit (`timeoutMs`; default 60 s, or the wait plus 5 s). Failures are reported per step and later steps still run unless `"stopOnError": true` (then the rest are "not run").
  - **Safety:** every safety check runs again before acting.
  - **Programs:** start through `IProcessControl.Start` with `UseShellExecute = false` (as the current user; a program that requires admin fails instead of prompting).
  - **Library items:** launch through `LaunchService`, so playtime tracking applies, and only when installed (a routine never starts an install).
  - **Web pages and folders:** URLs must be http/https. Folders open with `explorer.exe "<folder>"` and only when the path is an existing folder.
  - **Already running:** `skipIfRunning` (default) skips a program already running from that path, or a game with a process in its folder.
  - **Close:** asks politely (`CloseMainWindow`), waits `graceMs`, and ends the program only with `"force": true`.
- **Undo:** closes politely only what that run opened. Games often ignore that request or just minimize. Undo then reports which programs are still open, and **Force close** (after a confirmation that unsaved progress is lost) ends exactly those processes (id + start time), never Windows' own or Lumen. Owner test 2026-10-29: a Steam game only minimized.
  - Programs: tracked by id + start time.
  - Library items: processes in the item's folder that started after the run began, because store launchers don't report the game's process.
- **Files and service (`RoutineService`):**
  - **Where:** routines are `{id}.json` in `%APPDATA%\Lumen\portable\routines`, or in `<sync folder>\routines` while sync is on. The file name must match the id.
  - **Writing:** goes through `IUserFiles` (`PhysicalUserFiles`), which only writes inside Lumen's portable folder (temp file, then rename) plus a file the user picked in a save dialog (export). `IFileSystem` stays read-only, so rule 2 holds.
  - **Running:** reads the file again and runs only when its hash still equals the approved one.
  - **Saving from the editor** approves the routine (the user wrote every step), unless it has a blocked step.
  - **Import** copies the file and always starts with the preview; replacing a routine drops its approval.
  - **Export** writes Lumen's own form of the routine. Delete removes the file and its approval.
  - **Undo:** the last run per routine is kept in memory for Undo.
- **UI (`RoutinesViewModel`, Routines page):**
  - **Cards:** name, step count, and state (Ready / Needs your review / Has a blocked step / Can't be used). Buttons: Run, Review, See why, Undo, Edit, Export, Delete; at the top, New, Import and Open folder.
  - **Run:** an unapproved routine opens the preview instead. After a run the card says how many steps worked and the first problem.
  - **Preview (`RoutineReviewViewModel`):** every step as a sentence (`RoutineText`). Programs outside the library and close steps are highlighted, blocked steps marked, validator errors listed. "Approve and run" and "Approve" only show when allowed.
  - **Editor (`RoutineEditorViewModel`):** a name, then steps added from a list:
    - open a game or app (library search)
    - open a program (.exe picker, optional arguments)
    - open a web page
    - open a folder (picker)
    - wait
    - close a game or app
    - close a program by name (optional force)

    Each step is checked with the same review as the preview, so the editor warns exactly the same way. Steps can be reordered and removed. Options: "stop at the first step that fails" and "show on Home".
  - The preview and the editor cover the page as a gamepad layer; Esc / B closes them.
- v1 never executes scripts or shell commands and never elevates.

## 8. UI composition (Lumen.App)

- Single main window with a custom dark chrome; navigation via a `NavigationService` and view-model-first `DataTemplate`s.
- Themes as `ResourceDictionary`: `Theme.Dark.xaml` (palette tokens), `Controls/*.xaml` (styled controls). Accent is a dynamic resource updated by `AccentService`.
- Virtualized grid for large libraries: rows of tiles in a recycling `VirtualizingStackPanel`.
  - **`ImageCache`** (M5, after a stutter report with ~370 games) decodes tile covers and icons off the UI thread, at their on-screen pixel size (display scaling from the main window). It keeps the most recent ones, up to 96 MB, so scrolling back over them costs nothing. A cover decode measured ~5 ms on the owner's PC, so a row of 7 was ~35 ms per frame.
  - Tiles never hold bitmaps themselves.
  - The tile context menu is built when it opens, not per tile.
- All user-visible text in `Resources/Strings.resx` / `Strings.es.resx`. No hard-coded strings in XAML or view models.
- **Look and motion (M10, PLAN §4.9):**
  - **Font:** Exo 2 (SIL OFL) in four static weights under `Assets/Fonts` (cut from Google Fonts' variable font with fontTools; WPF can't use variable axes), embedded as resources and referenced as `/Lumen;component/Assets/Fonts/#Exo 2` (`Lumen.Font.Ui`). Implicit `Window`, `ToolTip` and `ContextMenu` styles apply it; icons keep Segoe Fluent Icons. The licence ships next to the files and is credited in the READMEs.
  - **Type scale:** `Lumen.FontSize.*` tokens (Label 11 … Display 44) and `Lumen.Text.*` styles in `Themes/Theme.Dark.xaml`; no view uses a literal scale size (repo test).
  - **Mica:** `SystemBackdrop` (Platform.Windows: `DwmSetWindowAttribute` backdrop type + `DwmExtendFrameIntoClientArea`, build ≥ 22621, reads `EnableTransparency`) and `MicaBackdrop` (App: swaps `Lumen.Brush.Backdrop` and `SidebarBackdrop` between opaque and translucent, window ≈ 69 % and sidebar ≈ 55 % alpha; sets the HWND composition background transparent; re-evaluates on `UserPreferenceChanged`, hopping to the UI thread). Home's root is transparent over it; the detail, Discover and editors keep opaque backgrounds because they cover the page.
  - **Cover glow and ring:** a collapsed `Border` with the cover's color and a radial opacity mask, shown on hover and keyboard/gamepad focus (Library and Home), plus a 2 px ring in the same color (Library). Library color: `CoverColor.PickGlow` = `AccentPicker.Pick` + `Fit` + `Soften` on a 64-px decode, cached per path and file time, computed on first hover. Home uses the global accent.
  - **Motion** (`Services/Motion.cs`): `Fast` 140 ms, `Normal` 220 ms, `Hero` 450 ms; `Enabled` = Windows "Animation effects". `PageIn` (page host on section change), `PanelIn` / `ShowIn` (covering panels when they become visible; quick search uses Fast), `Stagger` (up to five blocks, 50 ms apart, started at background priority after layout, transform cleared afterwards), `FadeIn` (Home hero text), `HoverScale` 1.04 (tile cover + glow). `AccentService` and the Home background crossfade check the same switch. Closing a panel is instant.
  - **Clipping:** `VirtualizingStackPanel` and `ScrollViewer` clip to their bounds, so scaled covers need room inside them: Library tiles have 8 px padding (slot stays 188 px; row and grid margins cancel the offsets); Home rows use `ScrollViewer` negative margin with matching `ItemsControl` margin.
  - **Template look (M10 second pass, owner's HTML template, 2026-10-30):**
    - **Palette:** deep blue-black base (`#0A0D14`), slate text, translucent white dividers (`Theme.Dark.xaml`).
    - **Mixed accent:** `Lumen.Brush.Brand*` is a fixed blue → indigo → violet brand (`BrandGradient`, `BrandText`, `BrandSoft`, `BrandFade`) used by all chrome: buttons, sidebar selection, inputs, focus rings, links. `Lumen.Brush.Accent` (the featured game's color, `AccentService`) only tints art: hero, page background, cover glow and ring. A repo test keeps the accent out of `Themes/Controls`.
    - **Glass:** `Lumen.Card` (`Controls/Surfaces.xaml`) paints `Lumen.Brush.Glass` (≈ 85 % opaque) with a 1 px light edge (`GlassBorder`), radius 16. No backdrop blur (WPF has no cheap one). Detail, Discover and editor panels keep opaque backgrounds.
    - **Buttons** (`Controls/Buttons.xaml`): `Lumen.Button` (quiet translucent fill; hover lays a light sheen over any background; keyboard/gamepad focus draws a 2 px brand ring outside the button), `Lumen.Button.Primary` (brand gradient), `Lumen.Button.Icon` (one glyph), `Lumen.Button.Tile` (brand when installed). `Lumen.Badge` / `Lumen.Pill` / `Lumen.Text.Badge` for tags; an implicit thin `ScrollBar` (6 px thumb, no arrows).
    - **Ambient lights:** two large `Ellipse`s with radial brand brushes (`Lumen.Brush.AmbientBlue` / `AmbientViolet`, 15 % alpha) behind the sidebar and pages in `MainWindow`. `Motion.Breathe` fades them between 55 % and 100 % over 4 s each way, out of step, capped at 20 frames a second; it stops while the element is hidden (tray) or the window is minimized, and stays still with "Animation effects" off.
    - **Shell (step B):** the sidebar (256 px) opens with a brand header (gradient badge, "LUMEN", the version: `ShellViewModel.VersionText`, "dev" for `0.0.0-dev`). Entries carry a count pill (`SectionViewModel.Count`: the library's non-hidden items, `LibraryViewModel.LibraryCount`) and a dot (`SectionDot`: green while a routine runs, amber when one needs review; `RoutinesViewModel.IsBusy` / `NeedsAttention`). A top bar over the pages holds the search and up to two pinned routines (`HomeViewModel.TopBarRoutines`, run through `StartFromShortcutAsync`, so unreviewed ones open their preview). The search is the library's filter (`ShellViewModel.SearchText` ↔ `LibraryViewModel.SearchText`): typing switches to Library and closes an open detail; Ctrl+K or Ctrl+F focus it, Esc clears it. The Library page lost its own search box.
    - **Sidebar footer (step B2):** `SidebarFooterViewModel`. **Steam account:** `SteamProvider.ReadProfileAsync` → `SteamLocalProfile` reads Steam's own `config/loginusers.vdf` (public persona name; the account set in Settings first, else "MostRecent", else the newest "timestamp") and shows `config/avatarcache/<SteamID64>.png` when Steam cached it. Read-only, no network, no token; re-read after each full scan; hidden when nothing is found. **Monitor:** `ISystemUsage` (Core) with `WindowsSystemUsage` (`GetSystemTimes`, `GlobalMemoryStatusEx`); `CpuLoad.Between` turns two readings into a busy share. Sampled every 2 s only while the window is visible and not minimized (`SetActive` from `MainWindow`); bars are a `ScaleTransform` on a gradient.
    - **Home (step C):** the hero is a rounded card (radius 24) with the featured game's art (`BackgroundPath`, decoded at 1280 px, fading in with `Motion.FadeIn(HeroArt)`), shades from the bottom and the left, a "Featured" pill, last played, logo or title, Play (brand gradient) and Details, and the playtime as a big number (`FeaturedPlaytimeValue` / `FeaturedPlaytimeLabel`). The blurred page art now fades out down the page (opacity mask), so the ambient lights show below. Pinned routines are brand-tinted cards in three columns with "See all" (`RoutinesRequested` → Routines page). Row covers carry the store badge (`HomeTileViewModel.StoreName`, same names as the library), a bottom shade, a round play button while pointed at (mouse only, `PlayTileCommand`; Enter still opens the detail) and the playtime under the title. Quick apps are glass chips (icon + name).
    - **Library (step D):** the header is a glass card holding the title and count, Rescan / Discover apps / Add (primary) and the filter row. Tiles keep their layout and 188 px slot (8 px padding, fixed heights for smooth virtualized scrolling) with a rounder cover (14), the shared `Lumen.Badge` for store and favorite star, a bottom shade under Play (one plain gradient, no effect) and a semibold title. A repo test keeps bitmap effects out of the Library view.
    - **Toasts:** `ToastService` (App) keeps up to three notices (3.5 s each) in the window's bottom-right corner, text as `Func<string>` so it follows the language. Sources: `LaunchService.Launched` (Core event after a start or a failed start: "Starting X…", "Opening the installer for X…", the launch-failed text) and `RoutinesViewModel.FinishedOutsidePage`. The tray balloon for a routine finished from a shortcut now shows only while the window is hidden or minimized.
  - **Repo tests** (`Lumen.Core.Tests/RepositoryChecks`): `UiFontTests`, `UiSurfaceTests`, `UiMotionTests`.
- **Gamepad (M7):** every screen that works with the keyboard works with a gamepad. It only acts while a Lumen window (main or quick search) is the active window.
  - **Reading:** `WindowsGamepadReader` (Windows.Gaming.Input; Xbox and compatible pads) only reads and never vibrates. Several pads merge. Connections come from the added/removed events, because the pad list starts empty.
  - **Polling:** `GamepadService` polls about every 16 ms, only while a pad is connected and Lumen is active.
  - **Commands:** `GamepadNavigator` (Core, tested) turns readings into commands like a keyboard would: buttons act on press; a held D-pad/left stick (threshold 0.5, main axis) repeats after 400 ms every 110 ms. Coming back to Lumen ignores what was already held.
  - **Moving focus (spatial):** keyboard arrow navigation stops at row and list edges and is captured by text boxes and dropdowns. So the D-pad/stick don't send arrow keys: `SpatialNavigation` (Core, tested) picks the nearest control that lies entirely in that direction. Distance along the direction counts once and sideways offset counts double, so the pick stays in the row or column. It works across rows, panels and the sidebar.
    - If nothing is on screen that way, the nearest scroller moves a line (virtualized rows appear) and it looks again.
    - Candidates are visible, enabled, focusable tab stops. Lists and scrollers themselves are skipped.
  - **Layers:** panels that cover the page set `GamepadLayer.Priority`: detail = 1, routines preview/editor = 1, Discover apps = 2, welcome = 3. While one is visible, focus stays inside the highest one.
  - **Focus recovery:**
    - When focus is lost or hidden (a page, the detail or the wizard just changed), the next input only puts it back. It goes to the control used last on the page when coming back from a layer; otherwise to the top-left control, preferring the page over the sidebar.
    - After A or B, this happens right away: opening a detail focuses inside it, and closing it returns to the tile.
  - **Buttons:**
    - A → Enter (Space on checkboxes; opens a dropdown; selects a sidebar entry)
    - B → Esc

    Both go through WPF's own input pipeline (`InputManager.ProcessInput`), not Windows' `SendInput`, so they can only reach Lumen and existing Enter/Esc handling applies. Inside an open dropdown and in quick search, the D-pad sends arrow keys, since those lists handle them.
  - **Lumen actions:** Y opens quick search. LB/RB move to the previous/next sidebar section (no wrap; not while the welcome wizard is open) and focus the new page. With nothing focused yet, see focus recovery above.
- **Home (M3):** the start page. `HomeFeed` (Core, pure) picks "Continue playing" (visible games with a known last-played date, newest first, up to 12) and the featured game (the most recently played; with no history, the newest installed game). Hidden items and apps never appear. The featured game's hero (or cover) fills the page blurred (decoded at 640 px, two layers crossfading); logo (or title), playtime, last played, Play and Details over it. Pointing at or focusing a tile in the row features it after 180 ms. Details opens the game in the library's detail view and Back returns to Home. The row has no scrollbar: the wheel scrolls it sideways. Below it (M4):
  - **Favorites:** favorite games, in the order they were marked, up to 24.
  - **Quick apps:** a compact dock of app icons that open the app with one click. It shows favorite apps; with none marked, the library's apps by name (up to 12).

  `HomeFeed` builds both rows. Hidden items never appear, and both rows hide when empty. Pinned routines (`RoutineLookup.Pinned`) show as cards in their own row; a click calls `RoutinesViewModel.StartFromShortcutAsync`.
- **Dynamic accent (M3):** `AccentPicker.Pick` quantizes a 64-px-wide decode to 4 bits per channel, ignores transparent pixels, specks under 0.5 %, low chroma (< 0.15) and very dark/light buckets (HSL L < 0.12 or > 0.8), and scores by population × chroma² × closeness to mid lightness. `AccentPicker.Fit` keeps hue/saturation and moves lightness so white text on the accent is ≥ 4.5:1 and the accent on the background is ≥ 3:1. `AccentService` swaps the theme's frozen accent brush for one animatable copy and fades it (450 ms). The accent follows what is on screen: Home's featured game, an open detail view, otherwise the default. A toggle in Settings ("theme accents on/off", §4.7) is still to do.
- **Collections & favorites (M4):**
  - **Model:** `Collection` (Core) mirrors `collection.schema.json`: the id is a slug of the name (`^[a-z0-9][a-z0-9-]{0,63}$`, "-2" when taken), the name is 1–80 chars, items are entry refs. Favorites is the reserved `favorites` collection, created by migration 0010; it can't be renamed or deleted and shows translated.
  - **Merged games:** a merged game is stored with every store's ref, so it stays in a collection whichever store Play uses; `Contains` matches any of its entries.
  - **Storage:** `collections` + `collection_items` (position = order added, `ON DELETE CASCADE`), mirrored as `collections/*.json` when a sync folder is set.
  - **`CollectionService`** (Core) holds them in memory and raises `Changed` for every page.
  - **UI:**
    - Collections page: cards with Open / Rename / Delete (deleting never touches the games) and "Create collection". Open shows the collection in the library.
    - Library: a "Collection" filter (`LibraryQuery.Collection`).
    - Detail view: a favorite star button and More → "Collections…" (a checkbox per collection, "Create and add").
    - Tiles: "Add to / Remove from favorites" in the menu and a small star on favorites; only tiles whose star changed are refreshed.
- **Discover apps (M4):** `DiscoverAppsViewModel` lists suggestions (or the ignored ones), with a search box and Add / Ignore / Suggest again buttons. It opens over the library from the header button "Discover apps · n" and is also a step of the first-run wizard (same view). Added apps show "Open" instead of "Play"; their tile menu has "Remove from library" (back to suggestions). Store badges: "Windows" (Start menu) and "Microsoft Store".
- **First-run wizard (M3):** `FirstRunViewModel`, shown over the whole window (sidebar included) while `settings_local` has no `firstRun.completed = 1`; "Skip" and "Start using Lumen" both set it, and Settings → General → "Show the welcome again" reopens it. Steps: **language** (same list as Settings) → **stores** (`StoreFindings` in Core summarizes `LibraryService.LastFullScan`: found / empty / problem / not installed, installed count, Steam's owned-not-installed count; "Looking for your stores…" until the first full scan ends, refreshed on `FullScanCompleted`) → **optional keys** (the same `SteamSettingsCard` / `ArtworkSettingsCard` controls as Settings) → **apps** (Discover apps, M4) → **done**. Account sign-ins (Steam family, Epic, GOG) are only in Settings, not in the wizard.
- **Language (M3):** numbers and dates follow the chosen language too (`FormatCulture.For`, 2026-10-30): Windows' regional format when it is the same language, else that language's own (English shows "7.2" on a PC set to Argentina). "Same as Windows", Español, English (names in their own language), in Settings → General and the wizard. Stored in `settings_local` as `ui.language` (`system`/`en`/`es`); with a sync folder it travels in `config.json` → `language`. Applied before the main window opens and switchable live. Windows' language is captured at startup (`LocalizationService.SystemUiCulture`), so "Same as Windows" is right after trying another language.
- **Detail view (M3):** the cover is a button (click/Enter) that opens `GameDetailViewModel` over the library page; the library keeps its scroll, search and filters underneath. Esc/Back closes it.
  - Shows: hero (fading into the page), logo (falls back to the title), Play/Install, "Play with" store selector for merged games (choosing a store saves it as the preferred one), stores, playtime, last played and location.
  - Actions: open the install folder (`explorer.exe "<folder>"`, only when it exists) and the store page inside the store's own app (`steam://store/{id}`, `ms-windows-store://pdp/?PFN=…`; other stores have no reliable link and hide the button). Under "More": change cover/background/logo (overrides), restore originals, show stores separately.
  - Under "More" too: rename (inline box; empty or the store's name restores it), restore original name, hide / show again (a hidden item's detail says so, with a button to undo), merge with another item (inline search of the library, same kind, up to 30 results). The tile menu also has hide / show again.
  - **Uninstall** (owner-approved addition to PLAN-v1 §4.3): shown when a copy is installed (the selected store's first). Lumen hands it over and deletes nothing, runs no uninstaller and never asks for admin (security rules 2–3): Steam `steam://uninstall/{appid}` and Ubisoft `uplay://uninstall/{id}` open the store's own confirmation; Epic opens its library (`com.epicgames.launcher://store/library`, no per-game uninstall link); GOG, EA, Battle.net and Xbox open Windows Settings → Apps (`ms-settings:appsfeatures`). A notice says what to do next; the auto refresh picks up the result. Manual items have no uninstall ("Remove" takes them out of Lumen). Links taken from Playnite's store plugins. **Verified (owner, Windows 11, 2026-10):** Steam and Ubisoft open their own uninstall flow for the game, Epic opens its library, GOG/EA/Battle.net/Xbox open Settings → Apps.
- **Accessibility:** buttons whose content is icon + text set `AutomationProperties.Name`; dropdown options override `ToString()` with their label. Clickable things that share a grid cell must be checked for hit-testing (UI Automation `Invoke` bypasses it).
- **Tray (M7):** `TrayService` puts an H.NotifyIcon `TaskbarIcon` in the notification area (app icon `Assets/Lumen.ico`, also the window and exe icon).
  - Left click opens Lumen.
  - The menu is built each time it opens: Open, Quick search, up to 5 recently played games (the same list as Home's "Continue playing", only ones that can run; a click launches them like Play), a "Routines" submenu (up to `RoutineLookup.MaxInMenu`; valid routines only, ones that need review say so) and Exit. A finished run shows a notification. Quick search also lists up to 3 routines whose name matches the text (never for an empty box).
  - **Routines from outside the page** (tray, quick search, Home) all go through `StartFromShortcutAsync`: a routine that is new or changed never runs from there. The Routines page opens with its preview instead (`ReviewRequested` brings the window forward), and an editor already open is left alone.
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
  - The tray menu has "Quick search (shortcut)". The gamepad's Y opens it too.

## 9. Build, CI and release

- `Directory.Build.props`: `TargetFramework` defaults, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnableWindowsTargeting=true` (lets the full solution build on non-Windows agents; WPF still only runs on Windows).
- CI (`.github/workflows/ci.yml`): on push/PR — `windows-latest`: restore, build, test. The required check is "Build & test (Windows)". On Linux the same build works but 26 `Lumen.Core.Tests` (routine tests with `C:\` paths) fail; they pass on Windows.
- Development needs Windows with the .NET 10 SDK (`global.json`); the user-level install from Microsoft's `dotnet-install` script needs no admin. Logic in Core/Data/Providers also builds and tests on Linux/macOS thanks to `EnableWindowsTargeting`, but the WPF app only runs on Windows.
- Localization: `Lumen.App/Resources/Strings.resx` (en) + `Strings.es.resx` (es), read through `LocalizationService`; XAML uses `{l:Loc Key}`, which follows runtime language changes. `LanguageResolver` (Core) maps the `language` preference to a culture. A repository test fails if the two resx files have different keys or a XAML `{l:Loc}` key is missing.
- Release (`.github/workflows/release.yml`, M9): on a tag `v*` it checks the tag (`vMAJOR.MINOR.PATCH[-suffix]`, on `main`), builds and tests, `dotnet publish` self-contained `win-x64`, `vpk pack`, and `vpk upload github --publish` (a tag with `-` is created as a GitHub **pre-release**). It produces `Lumen-win-Setup.exe`, `Lumen-win-Portable.zip`, the full package and a delta from the previous release (`vpk download github` first, so the first release simply has no delta).
  - **Validated on Linux on 2026-10-30:** the same `dotnet publish` and `vpk pack` commands ran against this code (165 MB published folder, Setup.exe 82 MB, portable zip 74 MB; a delta between two builds was 125 KB). `vpk` checked that `VelopackApp.Run()` is in `Program.Main`. The upload and the Windows runner were not run until the first tag.
  - Builds are **unsigned** in v1: SmartScreen warns on first run (documented in the README).
  - **Verified on the real runner (2026-10-30):** the owner tagged `v0.1.0-beta.*` and reported the release and the update flow ready ("todo listo"). Details of what was exercised (install, delta, restart) were not recorded; re-check them in the 1.0 bug bash.
- Versioning: SemVer from the tag. `Directory.Build.props` sets `Version` to `0.0.0-dev` for development builds; the workflow passes `-p:Version=<tag without the v>`. No version package (no MinVer).

### Updates (M9)

`Lumen.Core/Updates` (`UpdateService`, `IUpdateClient`, `UpdateChannel`), `Lumen.Platform.Windows/Updates/VelopackUpdateClient`, `Settings → Updates`.

- **Entry point:** `Program.Main` runs `VelopackApp.Build().Run()` first (install, update and uninstall hooks run and exit; a normal start returns at once), then starts the WPF `App`. `App.xaml` is a `Page` and `StartupObject` is `Lumen.App.Program`.
- **Owner decisions (2026-10-30):**
  - **Default channel:** beta until 1.0 (every release before it is a pre-release), stable from 1.0 (`UpdateChannels.DefaultFor`, by the major number). The user can change it in Settings (`settings_local` `updates.channel`).
  - **Notice:** a card in Settings → Updates plus one tray notice per version (`updates.notifiedVersion`). No banner inside the window.
  - **Download:** only when the user presses *Download*. The user also decides when to restart. Nothing is installed by itself.
- **Channels** use the GitHub release flag, not separate Velopack channels: stable = `GithubSource(prerelease: false)` (normal releases), beta = `prerelease: true` (pre-releases and normal ones, so a beta user also receives the stable release). One set of packages per release.
- **When it checks:** 15 s after start-up and then every 24 h while Lumen runs, if "check automatically" is on (`updates.checkAutomatically`, default on). "Check now" works either way. A download left waiting from an earlier run shows as ready to restart.
- **States:** not installed (a build run from source: nothing to update) · idle · checking · up to date · available · downloading (with %) · ready to restart · failed. A failure shows a generic message (offline, GitHub down) and the next try starts clean; details only go to the log.
- **Security:** the repository URL is a constant in `AppHost` (no setting can point it elsewhere) and the repository is public, so no token is used. The only host is GitHub Releases (security rule 4). Velopack compares the SHA-256 of a download with the release manifest. There is no code signing in v1, so a compromised repository would be a compromised update; this is the accepted v1 trade-off. Nothing is sent about the user.
- **Restart:** `UpdateService.RestartToUpdate` first releases the tray icon, the hotkey, the gamepad and the single-instance lock (`App.ReleaseForRestart`), then Velopack installs and starts Lumen again.
- **Not synced:** the channel and the automatic-check switch stay on each PC for now (the schema's `updates` keys are kept in `config.json` untouched).

