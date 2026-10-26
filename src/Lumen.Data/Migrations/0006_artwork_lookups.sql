-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0006: ids found while looking for artwork (e.g. "sgdb:steam:570" -> SteamGridDB game id), so each search runs
-- once per item. value NULL = searched, not found (retried after a while by the source).

CREATE TABLE artwork_lookups (
    key        TEXT    NOT NULL PRIMARY KEY,
    value      INTEGER,
    updated_at TEXT    NOT NULL
) STRICT;
