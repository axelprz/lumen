-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0011: public metadata looked up once per item (e.g. "epic:catalog:{catalogItemId}" -> title and kind as JSON),
-- so scans don't repeat catalog requests. Never secrets. Safe to delete: it is refilled on demand.

CREATE TABLE metadata_cache (
    key        TEXT NOT NULL PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;
