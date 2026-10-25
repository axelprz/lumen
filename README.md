# Lumen

**[Leer en español](README.es.md)**

Lumen is a free, open-source launcher for Windows that brings all your games and apps into one place. Steam, Epic, GOG, Xbox / Microsoft Store, Ubisoft Connect, EA app and Battle.net show up in a single library — installed or not — next to the apps you choose. Launch anything in two seconds, with a mouse, a keyboard shortcut or a gamepad.

> **Status:** early development. Nothing is released yet. See [the v1 plan](docs/PLAN-v1.md).

## What it does (v1 goals)

- **One library for every store.** Installed games are detected locally; owned-but-not-installed games appear greyed out and open the original launcher to install them.
- **Your apps, curated.** Lumen finds the apps in your Start menu and lets you pick which ones belong in your library. No clutter.
- **Looks good.** Dark UI with backgrounds and accent colors that follow the selected game's artwork (Steam CDN + SteamGridDB).
- **Fast access.** Tray icon and a global hotkey for a quick search box.
- **Gamepad navigation** for the couch.
- **Playtime** tracked by Lumen, plus your existing Steam hours.
- **Routines** (optional): one click opens or closes a set of apps, sites and folders — e.g. "Work". Routines are plain JSON files you can sync through a folder or share.
- **In-app updates** via GitHub Releases. Installer or portable zip.

## Privacy and security principles

- Everything runs locally. No Lumen servers, no telemetry, no ads.
- Launcher files are only ever **read**, never modified.
- Account logins are **optional**. Tokens and API keys are encrypted with Windows DPAPI and never leave your PC except to talk to the service they belong to.
- No third-party plugins or themes are loaded in v1.
- Imported routines are shown in full and must be approved before they can run.

## Requirements

- Windows 10 (22H2) or Windows 11, x64
- Optional: a [Steam Web API key](https://steamcommunity.com/dev/apikey) to list non-installed Steam games and playtime
- Optional: a [SteamGridDB API key](https://www.steamgriddb.com/profile/preferences/api) for artwork

## Building from source

```powershell
git clone https://github.com/axelprz/lumen
cd lumen
dotnet build
dotnet test
```

Requires the .NET 10 SDK. To open the app: `dotnet run --project src/Lumen.App` (Windows only).

### With Docker (no SDK install)

```powershell
docker build --target test .              # build everything and run the tests
docker compose run --rm dev               # shell with the SDK and the repo mounted at /src
```

You can also open the repo in VS Code with "Reopen in Container" (`.devcontainer/`). The container builds and tests; the WPF app itself only opens on Windows.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the project layout.

## License

Copyright (C) 2026 Axel Ismael Perez (AxelPRZ).

Lumen is free software released under the [GNU General Public License v3.0 or later](LICENSE). See [COPYRIGHT](COPYRIGHT).

Lumen is not affiliated with Valve, Epic Games, CD PROJEKT, Microsoft, Ubisoft, Electronic Arts, Blizzard or SteamGridDB. All trademarks belong to their owners.
