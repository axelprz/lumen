-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)
--
-- 0014: per-entry launch arguments (M11). Text for the game (Steam passes it on), never a command.

ALTER TABLE item_overrides ADD COLUMN args TEXT NULL CHECK (args IS NULL OR length(args) BETWEEN 1 AND 2000);
