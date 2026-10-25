// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Data.Migrations;

/// <summary>One hand-written SQL migration, embedded as <c>Migrations/NNNN_name.sql</c>.</summary>
public sealed record Migration(int Version, string Name, string Sql);
