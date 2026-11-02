// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>
/// Per-item launch arguments (PLAN-v1 M11, owner 2026-11-02). Only where the launch can carry them: Steam games, whose
/// <c>steam://rungameid/ID</c> becomes <c>steam://run/ID//arguments/</c> (Steam passes them to the game). Manual items
/// already have their own arguments field. Arguments are text for the game, never a command: nothing here starts a shell.
/// </summary>
public static class LaunchOptions
{
    /// <summary>Longest text accepted (matches <c>config.schema.json</c>).</summary>
    public const int MaxLength = 2000;

    /// <summary>True when the entry's launch can carry arguments (an installed Steam game).</summary>
    public static bool Supports(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Ref.Source == "steam"
            && entry.Ref.ExternalId.Length > 0
            && entry.Ref.ExternalId.All(char.IsAsciiDigit)
            && entry.Launch is { Kind: LaunchKind.Uri } launch
            && (launch.Target.StartsWith("steam://rungameid/", StringComparison.Ordinal)
                || launch.Target.StartsWith("steam://run/", StringComparison.Ordinal));
    }

    /// <summary>One line of text up to <see cref="MaxLength"/>, with no control characters.</summary>
    public static bool IsValid(string? arguments) =>
        arguments is not null && arguments.Length <= MaxLength && !arguments.Any(char.IsControl);

    /// <summary>The user's input, trimmed; null when empty or not valid (see <see cref="IsValid"/>).</summary>
    public static string? Clean(string? arguments)
    {
        var trimmed = arguments?.Trim();
        return string.IsNullOrEmpty(trimmed) || !IsValid(trimmed) ? null : trimmed;
    }

    /// <summary>The entry with its launch carrying <paramref name="arguments"/>; the entry itself when it can't or there are none.</summary>
    public static LibraryEntry Apply(LibraryEntry entry, string? arguments)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Clean(arguments) is not { } clean || !Supports(entry) || entry.Launch is null)
        {
            return entry;
        }

        var target = $"steam://run/{entry.Ref.ExternalId}//{Uri.EscapeDataString(clean)}/";
        return entry with { Launch = entry.Launch with { Target = target } };
    }
}
