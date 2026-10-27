-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0008: the user's per-entry choices, keyed by ItemRef ("steam:570").
-- item_overrides: a custom title (NULL = the store's) and whether the entry is hidden from the library.
-- merge_rules.merged_with: manual "merge with" — this entry joins the item that contains that ref.

CREATE TABLE item_overrides (
    ref        TEXT    NOT NULL PRIMARY KEY,
    title      TEXT    NULL CHECK (title IS NULL OR length(title) BETWEEN 1 AND 120),
    hidden     INTEGER NOT NULL DEFAULT 0 CHECK (hidden IN (0, 1)),
    updated_at TEXT    NOT NULL
) STRICT;

ALTER TABLE merge_rules ADD COLUMN merged_with TEXT NULL;
