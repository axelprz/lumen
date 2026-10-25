// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.App.ViewModels;

/// <summary>Page for sections that later milestones fill in. Shows the section title and "Coming soon".</summary>
public sealed class PlaceholderPageViewModel(SectionViewModel section)
{
    public SectionViewModel Section { get; } = section;
}
