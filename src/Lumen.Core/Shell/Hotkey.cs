// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Lumen.Core.Shell;

/// <summary>Modifier keys; the values are Windows' <c>MOD_*</c> flags.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// The quick search shortcut (PLAN-v1 §4.3, default <c>Alt+Shift+Space</c>), stored as text like "Ctrl+Alt+K".
/// It needs Ctrl, Alt or Win (Shift alone would swallow normal typing), except for F-keys. Keys: A–Z, 0–9, F1–F24
/// and Space.
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public static Hotkey Default { get; } = new(HotkeyModifiers.Alt | HotkeyModifiers.Shift, "Space");

    public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            var modifier = part.ToUpperInvariant() switch
            {
                "ALT" => HotkeyModifiers.Alt,
                "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };

            if (modifier != HotkeyModifiers.None)
            {
                modifiers |= modifier;
            }
            else if (key is null && NormalizeKey(part) is { } k)
            {
                key = k;
            }
            else
            {
                return false; // unknown part, or a second key
            }
        }

        if (key is null || !IsAllowed(modifiers, key))
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    /// <summary>A key name Lumen accepts ("a" → "A", "f5" → "F5", "space" → "Space"), or null.</summary>
    public static string? NormalizeKey(string? name)
    {
        var key = name?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (key == "SPACE")
        {
            return "Space";
        }

        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return key;
        }

        return key.Length is 2 or 3 && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24
            ? key
            : null;
    }

    public static bool IsAllowed(HotkeyModifiers modifiers, string key) =>
        key.StartsWith('F') && key.Length > 1
        || (modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    /// <summary>Canonical text, modifiers in a fixed order: "Ctrl+Alt+Shift+Win+Key".</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key);
        return string.Join('+', parts);
    }
}
