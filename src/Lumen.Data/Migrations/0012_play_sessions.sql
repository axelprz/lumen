-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0012: play sessions Lumen saw for what it launched (PLAN-v1 §4.4). Per machine, never synced.
-- A session is written when it starts (open = 1) and its end moves forward while the game runs; open sessions left by
-- a closed Lumen are closed at their last recorded end on the next start.

CREATE TABLE play_sessions (
    id         INTEGER PRIMARY KEY,
    ref        TEXT    NOT NULL,
    started_at TEXT    NOT NULL,
    ended_at   TEXT    NOT NULL,
    open       INTEGER NOT NULL DEFAULT 1 CHECK (open IN (0, 1))
) STRICT;

CREATE INDEX play_sessions_ref ON play_sessions (ref);
