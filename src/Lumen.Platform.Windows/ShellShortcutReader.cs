// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

/// <summary>
/// Reads .lnk files with Windows' own ShellLink object. Only loads and reads: it never calls Resolve (which may
/// search the disk and rewrite the shortcut) or Save, so Start menu files are never modified.
/// </summary>
public sealed class ShellShortcutReader : IShortcutReader
{
    private const int MaxPath = 1024;
    private const uint StgmRead = 0x00000000;
    private const uint SlgpRawPath = 0x4; // the stored path, without resolving

    public ShortcutTarget? Read(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, (int)StgmRead);
            var shell = (IShellLinkW)link;

            var target = new StringBuilder(MaxPath);
            shell.GetPath(target, target.Capacity, IntPtr.Zero, SlgpRawPath);
            var arguments = new StringBuilder(MaxPath);
            shell.GetArguments(arguments, arguments.Capacity);
            var icon = new StringBuilder(MaxPath);
            shell.GetIconLocation(icon, icon.Capacity, out var iconIndex);

            return new ShortcutTarget(
                Expand(target.ToString()),
                arguments.Length == 0 ? null : arguments.ToString(),
                Expand(icon.ToString()),
                iconIndex);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or InvalidCastException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    // Shortcuts often store "%ProgramFiles%\..." style paths.
    private static string? Expand(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
