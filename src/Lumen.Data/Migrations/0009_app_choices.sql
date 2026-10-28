-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0009: the user's choices about detected apps (Start menu shortcuts, Store apps), keyed by ItemRef
-- ("startmenu:7-Zip/7-Zip File Manager.lnk"). 'added' = in the library, 'ignored' = never suggested again.
-- No row = a suggestion in "Discover apps".

CREATE TABLE app_choices (
    ref        TEXT NOT NULL PRIMARY KEY,
    choice     TEXT NOT NULL CHECK (choice IN ('added', 'ignored')),
    updated_at TEXT NOT NULL
) STRICT;
