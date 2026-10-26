// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.App.ViewModels;

/// <summary>
/// One row of the library grid. WPF has no virtualizing wrap panel, so the grid is a virtualized list of rows:
/// only rows on screen (and their cover images) exist in memory.
/// </summary>
public sealed record TileRow(IReadOnlyList<LibraryTileViewModel> Tiles);
