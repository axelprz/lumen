# HANDOFF — where Lumen stands

Overwritten at the end of every session (≤ 40 lines). The rules live in CLAUDE.md; scope and decisions in PLAN-v1.md.

**Last updated:** 2026-11-02 · **Branch / PR:** `main`, no open PR · **CI:** green

## Status

- **Done:** M0–M10 (M10, the UI rework, closed 2026-11-02) **M11** and **M12** (both closed 2026-11-02): #69 theme accent on/off (`ui.dynamicAccent`, synced), #70 per-game launch arguments for Steam games (migration 0014, DB is now at 14), #71 Steam achievements (unlocked of total, family games included, "View on Steam" button), #73 notification center (bell in the top bar, in memory only: updates, store problems, sync arrivals, failed launches).
- **Dropped from M11 by the owner (2026-11-02):** enable/disable each store, custom library paths (PLAN §7).
- **Released:** pre-releases up to `v0.2.0-beta.4`. Friends of the owner are testing; their feedback comes before `v1.0.0`. The next beta (with M11) only when the owner asks.

## Next task

1. **Friends' beta feedback:** fix what they report, one PR per issue.
2. **M13 Deals:** needs the owner's decisions first (sources such as CheapShark / IsThereAnyDeal / Epic promotions / GamerPower / Steam charts, which new hosts go into security rule 4, opt-in prompt, Argentina tax data file). No card or bank advice. First step: ask him those questions (question tool) before any code.
3. **1.0:** last bug bash, then `v1.0.0` (tag only on the owner's word).

## Open questions for the owner

- Deals sources and hosts (M13), see above.

## Open notes and gotchas

- The Library is the heaviest page to open (the owner said it felt better after #57; measure before touching it).
- A Steam game ignored Undo and Force close (tracked process ended, a window stayed); the owner accepted it, apps close fine.
- **`git reset --hard` / `gh pr merge` wipe uncommitted files.** CLAUDE.md, HANDOFF, docs/dev and .claude/rules are committed now (#72); never leave work uncommitted before a merge or reset.
- Steam asks the user to confirm every launch that carries custom arguments (its own protection); the hint in the detail says so. Whether a game honors an argument is up to the game.

## Last session

- Done: #69, #70 (M11), #71, #73 (M12). Nothing half-done. A beta with M11 and M12 can be cut when the owner asks.
