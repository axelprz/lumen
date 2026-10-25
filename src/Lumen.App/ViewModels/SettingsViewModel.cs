// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.App.ViewModels;

/// <summary>Settings page. Grows section by section (language, stores, artwork, sync...) in later milestones.</summary>
public sealed class SettingsViewModel(SteamSettingsViewModel steam)
{
    public SteamSettingsViewModel Steam { get; } = steam;

    public Task InitializeAsync(CancellationToken ct) => Steam.InitializeAsync(ct);
}
