# HANDOFF — where Lumen stands

Overwritten at the end of every session (≤ 40 lines). The rules live in CLAUDE.md; scope and decisions in PLAN-v1.md.

**Last updated:** 2026-11-02 · **Branch / PR:** `main`, no open PR · **CI:** green

## Status

- **Done:** M0–M10 (M10, the UI rework, closed 2026-11-02) **M11**, **M12** and **M13** (all closed 2026-11-02): #69 theme accent on/off (`ui.dynamicAccent`, synced), #70 per-game launch arguments for Steam games (migration 0014, DB is now at 14), #71 Steam achievements (unlocked of total, family games included, "View on Steam" button), #73 notification center (bell in the top bar, in memory only: updates, store problems, sync arrivals, failed launches).
- **Dropped from M11 by the owner (2026-11-02):** enable/disable each store, custom library paths (PLAN §7).
- **M13 Deals** (#75, #79, #80): section off until turned on; free games (Epic feed + Steam 100 %-off specials), price search and best deals (CheapShark, Lumen's stores only), Steam top sellers, Steamcito card. GamerPower was tried and dropped (unreliable data, third-party links; PLAN §7).
- **Released:** pre-releases up to `v0.2.0-beta.5` (2026-11-02, M11 + M12 + M13; DB version 14). Friends of the owner are testing; their feedback comes before `v1.0.0`. Code signing waits until the project has more visibility (README says so; SignPath Foundation is the free option).

## Next task

1. **Friends' beta feedback and the owner's test of beta.5:** fix what they report, one PR per issue (the owner tests in batches through betas because Windows Smart App Control blocks local builds).
2. **1.0:** last bug bash, then `v1.0.0` (tag only on the owner's word). Unverified things to check on his PC: Epic and Steam feeds (unofficial), CheapShark `/redirect`, Steam free promotions.

## Open questions for the owner

- None. (Optional later: CheapShark historical low; more Epic free games than its weekly feed.)

## Open notes and gotchas

- The Library is the heaviest page to open (the owner said it felt better after #57; measure before touching it).
- A Steam game ignored Undo and Force close (tracked process ended, a window stayed); the owner accepted it, apps close fine.
- **`git reset --hard` / `gh pr merge` wipe uncommitted files.** CLAUDE.md, HANDOFF, docs/dev and .claude/rules are committed now (#72); never leave work uncommitted before a merge or reset.
- **Windows Smart App Control** (on, enforced) blocks freshly built unsigned binaries on the owner's PC, intermittently; my local test runs and `dotnet run` fail with "did not meet the Enterprise signing level" (Code Integrity log). CI is the test run; do not change the policy.
- When a release adds network hosts, update the README privacy list in the same PR (it lists them).
- Steam asks the user to confirm every launch that carries custom arguments (its own protection); the hint in the detail says so. Whether a game honors an argument is up to the game.

## Last session

- Done: #69, #70 (M11), #71, #73 (M12), #75, #79, #80 (M13), README policies (#76, #77), `v0.2.0-beta.5`. Nothing half-done.
