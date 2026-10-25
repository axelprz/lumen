// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Diagnostics.CodeAnalysis;

namespace Lumen.Core.Library;

/// <summary>
/// Stable identity of an entry across machines and syncs, written as <c>"{source}:{externalId}"</c>
/// (e.g. <c>steam:570</c>, <c>manual:{guid}</c>). Source is lowercase ASCII letters/digits.
/// </summary>
public readonly record struct ItemRef
{
    public ItemRef(string source, string externalId)
    {
        if (!IsValidSource(source))
        {
            throw new ArgumentException($"Invalid source '{source}'. Use lowercase letters and digits.", nameof(source));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        Source = source;
        ExternalId = externalId;
    }

    public string Source { get; }

    public string ExternalId { get; }

    public override string ToString() => $"{Source}:{ExternalId}";

    public static ItemRef Parse(string value) =>
        TryParse(value, out var result) ? result : throw new FormatException($"'{value}' is not a valid item reference.");

    public static bool TryParse([NotNullWhen(true)] string? value, out ItemRef result)
    {
        result = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == value.Length - 1)
        {
            return false;
        }

        var source = value[..colon];
        var externalId = value[(colon + 1)..];
        if (!IsValidSource(source) || string.IsNullOrWhiteSpace(externalId))
        {
            return false;
        }

        result = new ItemRef(source, externalId);
        return true;
    }

    private static bool IsValidSource(string? source) =>
        !string.IsNullOrEmpty(source) && source.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9'));
}
