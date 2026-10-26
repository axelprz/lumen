-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0007: when each entry first appeared in Lumen (for "Recently added"). Never overwritten by later scans.
-- Existing entries are backfilled with their current timestamp.

CREATE TABLE entry_first_seen (
    ref        TEXT NOT NULL PRIMARY KEY,
    first_seen TEXT NOT NULL
) STRICT;

INSERT OR IGNORE INTO entry_first_seen (ref, first_seen)
SELECT source || ':' || external_id, updated_at FROM entries;
