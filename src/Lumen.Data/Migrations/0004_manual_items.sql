-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0004: games/apps added by hand. Same shape as config.json → manualItems so they can sync in M8.
-- Exactly one of path / url is set.

CREATE TABLE manual_items (
    id         TEXT NOT NULL PRIMARY KEY,  -- GUID, ItemRef "manual:{id}"
    kind       TEXT NOT NULL,              -- Game | App
    title      TEXT NOT NULL,
    path       TEXT,                       -- .exe / .lnk / .url, may contain %ENV% variables
    url        TEXT,
    args       TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    CHECK ((path IS NULL) <> (url IS NULL))
) STRICT;
