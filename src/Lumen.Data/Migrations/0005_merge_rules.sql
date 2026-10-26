-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0005: the user's merge decisions, keyed by ItemRef ("steam:570"). They always win over automatic merging.
-- separated = 1: never auto-merge this entry. preferred = 1: use this entry for Play when it is in a merged item.

CREATE TABLE merge_rules (
    ref        TEXT    NOT NULL PRIMARY KEY,
    separated  INTEGER NOT NULL DEFAULT 0 CHECK (separated IN (0, 1)),
    preferred  INTEGER NOT NULL DEFAULT 0 CHECK (preferred IN (0, 1)),
    updated_at TEXT    NOT NULL
) STRICT;
