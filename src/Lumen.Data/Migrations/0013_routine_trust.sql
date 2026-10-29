-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0013: routines approved on this PC (ARCHITECTURE §7). Per machine, never synced: a routine that changes outside
-- Lumen (another PC, an import, a text editor) has a different hash and needs a new review here.

CREATE TABLE routine_trust (
    id          TEXT NOT NULL PRIMARY KEY,
    sha256      TEXT NOT NULL CHECK (length(sha256) = 64),
    approved_at TEXT NOT NULL
) STRICT;
