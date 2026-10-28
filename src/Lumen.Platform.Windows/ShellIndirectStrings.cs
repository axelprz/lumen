// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Runtime.InteropServices;
using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

/// <summary>
/// <c>SHLoadIndirectString</c>: reads a string resource from a system file (loaded as data, not run). Only
/// "@file,-id" references are resolved; anything else is returned as null.
/// </summary>
public sealed partial class ShellIndirectStrings : IIndirectStrings
{
    private const int Capacity = 512;

    public unsafe string? Resolve(string indirect)
    {
        if (string.IsNullOrWhiteSpace(indirect) || !indirect.StartsWith('@'))
        {
            return null;
        }

        var source = Environment.ExpandEnvironmentVariables(indirect);
        var buffer = stackalloc char[Capacity];
        var hr = SHLoadIndirectString(source, buffer, Capacity, IntPtr.Zero);
        if (hr != 0)
        {
            return null;
        }

        var text = new string(buffer);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHLoadIndirectString(string pszSource, char* pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);
}
