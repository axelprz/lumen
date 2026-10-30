// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Updates;

/// <summary>
/// Which releases Lumen offers (PLAN-v1 §4.6): <see cref="Stable"/> = normal GitHub releases, <see cref="Beta"/> = normal
/// releases and pre-releases (so a beta user also gets the stable ones).
/// </summary>
public enum UpdateChannel
{
    Stable,
    Beta,
}

public static class UpdateChannels
{
    public static string ToText(UpdateChannel channel) => channel == UpdateChannel.Beta ? "beta" : "stable";

    public static bool TryParse(string? text, out UpdateChannel channel)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "stable":
                channel = UpdateChannel.Stable;
                return true;
            case "beta":
                channel = UpdateChannel.Beta;
                return true;
            default:
                channel = UpdateChannel.Stable;
                return false;
        }
    }

    /// <summary>
    /// The channel to use when the user hasn't chosen one (owner decision, 2026-10-30): beta until 1.0, because every
    /// release before it is a pre-release; from 1.0 on, stable. Only the major number counts ("0.3.0-beta.2" → beta,
    /// "1.2.0" → stable); an unreadable version counts as a development build (beta).
    /// </summary>
    public static UpdateChannel DefaultFor(string? version)
    {
        var text = version?.Trim().TrimStart('v', 'V') ?? string.Empty;
        var end = text.IndexOfAny(['.', '-', '+']);
        var major = end < 0 ? text : text[..end];
        return int.TryParse(major, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1
            ? UpdateChannel.Stable
            : UpdateChannel.Beta;
    }
}
