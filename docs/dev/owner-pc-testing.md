# Testing on the owner's PC (Windows 11)

Read before building or running anything on his machine. The short version is in CLAUDE.md ("Testing essentials").

- **Never steal focus.** Test builds start with `--tray` (no window). Never trigger the hotkey, never show windows. Stop test processes **by path only** (`Get-Process | ? { $_.Path -eq $exe } | Stop-Process`), never by title (it could kill his Lumen). Visual work can't be seen from here: say so and hand over the manual test.
- **Build into the scratchpad:** his running Lumen locks `src/Lumen.App/bin`: `dotnet build src/Lumen.App -o <scratchpad>/verify-build`. A whole-solution `dotnet build` then fails only with file-lock errors (MSB3021/3027); that is expected, `dotnet test` still runs.
- **Smoke test:** start `<scratchpad>/verify-build/Lumen.exe --tray`, wait ~12 s, check it is alive and `%LOCALAPPDATA%\Lumen\logs` has no `[ERR]`/`[FTL]` since the start, stop it by path. Unhandled UI exceptions are logged there ("Unhandled exception on the UI thread"): read the logs first when he reports a crash.
- **Shared data:** a test build uses his real data folder (`%LOCALAPPDATA%\Lumen`) and his sync folder if he set one. Never act on his accounts from a test build; mention any side effect (e.g. the sync writing his own settings).
- **Single instance per install folder:** a scratchpad build runs alongside his; "shortcut refused" (Alt+Shift+Space taken) is expected.
- **Database migrations:** `MigrationRunner` refuses a database newer than the code. A branch with a migration breaks every other branch on his PC once he runs it: merge it first or rebase the others on it.
- **Local tools:** `gh` works (PRs, CI, merge); a PR opened with `gh` is bound to the app's PR tools automatically.
