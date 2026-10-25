-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0002: cache of provider output so the library shows instantly on start.
-- Enums are stored as text so the table stays readable in a DB browser.

CREATE TABLE entries (
    source       TEXT NOT NULL,
    external_id  TEXT NOT NULL,
    kind         TEXT NOT NULL,           -- Game | App
    title        TEXT NOT NULL,
    state        TEXT NOT NULL,           -- Installed | NotInstalled | Unknown
    install_dir  TEXT,
    launch_json  TEXT,                    -- LaunchSpec as JSON, or NULL
    install_json TEXT,                    -- LaunchSpec as JSON, or NULL
    extra_json   TEXT NOT NULL DEFAULT '{}',
    updated_at   TEXT NOT NULL,
    PRIMARY KEY (source, external_id)
) STRICT;
