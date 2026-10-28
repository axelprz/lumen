-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0010: collections of games and apps (docs/schemas/collection.schema.json). Items are entry refs ("steam:570").
-- "favorites" is the reserved collection behind the favorite star: always present, never renamed or deleted.
-- The sync folder (M8) will mirror these as collections/*.json.

CREATE TABLE collections (
    id         TEXT NOT NULL PRIMARY KEY CHECK (length(id) BETWEEN 1 AND 64),
    name       TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 80),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;

CREATE TABLE collection_items (
    collection_id TEXT    NOT NULL REFERENCES collections (id) ON DELETE CASCADE,
    ref           TEXT    NOT NULL,
    position      INTEGER NOT NULL,
    added_at      TEXT    NOT NULL,
    PRIMARY KEY (collection_id, ref)
) STRICT;

INSERT INTO collections (id, name, created_at, updated_at)
VALUES ('favorites', 'Favorites', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
