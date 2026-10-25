-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0003: user-supplied keys/tokens, encrypted with DPAPI (CurrentUser) before they reach this table.
-- Never synced, never logged.

CREATE TABLE secrets (
    name       TEXT NOT NULL PRIMARY KEY,
    value      BLOB NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;
