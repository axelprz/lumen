// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;

namespace Lumen.Core.Localization;

/// <summary>Looks up user-facing strings for the current UI culture.</summary>
public interface ILocalizer
{
    CultureInfo Culture { get; }

    /// <summary>Returns the localized string, or <c>[key]</c> when the key is missing.</summary>
    string this[string key] { get; }

    event EventHandler? CultureChanged;
}
