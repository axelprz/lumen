// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core;

namespace Lumen.Platform.Windows;

public static class WindowsLumenPaths
{
    /// <summary><c>%LOCALAPPDATA%\Lumen</c> and <c>%APPDATA%\Lumen</c> for the current user.</summary>
    public static LumenPaths Create() =>
        LumenPaths.FromBaseDirectories(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify));
}
