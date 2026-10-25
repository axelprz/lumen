-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0001: baseline. `schema_version` is created by MigrationRunner itself.
-- Feature tables (entries, items, playtime_sessions, ...) arrive with the milestone that uses them.

CREATE TABLE settings_local (
    key        TEXT NOT NULL PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;
