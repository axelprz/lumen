# HANDOFF — where Lumen stands

Overwritten at the end of every session (≤ 40 lines). The rules live in CLAUDE.md; scope and decisions in PLAN-v1.md.

**Last updated:** 2026-11-02 · **Branch / PR:** `main`, no open PR · **CI:** green

## Status

- **Done:** M0–M10 (M10, the UI rework, closed 2026-11-02) **M11** (closed 2026-11-02) and the first half of **M12**: #69 theme accent on/off (`ui.dynamicAccent`, synced), #70 per-game launch arguments for Steam games (migration 0014, DB is now at 14), #71 Steam achievements (unlocked of total, family games included, "View on Steam" button).
- **Dropped from M11 by the owner (2026-11-02):** enable/disable each store, custom library paths (PLAN §7).
- **Released:** pre-releases up to `v0.2.0-beta.4`. Friends of the owner are testing; their feedback comes before `v1.0.0`. The next beta (with M11) only when the owner asks.

## Next task

1. **Friends' beta feedback:** fix what they report, one PR per issue.
2. **M12, second half:** notification center (a bell in the top bar: updates, store problems, sync arrivals). Achievements are done (Steam only; other stores have no public way, PLAN §7).
   - First step: grep `PLAN-v1.md` §5 (M12) and ARCHITECTURE for toasts/updates; decide what the bell lists and where it is stored (probably memory + synced arrivals only). Ask the owner before persisting anything.
   - Files to open: `ShellViewModel.cs`, `ToastService`, `UpdateService`, `SyncCoordinator`, the top bar in `MainWindow.xaml`.
3. **M13 Deals:** needs the owner's decisions first (sources such as CheapShark / IsThereAnyDeal / Epic promotions / GamerPower / Steam charts, which new hosts go into security rule 4, opt-in prompt, Argentina tax data file). No card or bank advice.
4. **1.0:** last bug bash, then `v1.0.0` (tag only on the owner's word).

## Open questions for the owner

- Deals sources and hosts (M13), see above.

## Open notes and gotchas

- The Library is the heaviest page to open (the owner said it felt better after #57; measure before touching it).
- A Steam game ignored Undo and Force close (tracked process ended, a window stayed); the owner accepted it, apps close fine.
- **`git reset --hard` / `gh pr merge` wipe uncommitted files:** the owner keeps an uncommitted `CLAUDE.md` (shorter version). It was overwritten twice this session and restored; a backup copy is in the scratchpad. Back up or ask him to commit it before merging or resetting.
- Steam asks the user to confirm every launch that carries custom arguments (its own protection); the hint in the detail says so. Whether a game honors an argument is up to the game.

## Last session

- Done: #69, #70 (M11), #71 (M12 achievements). Nothing half-done.
