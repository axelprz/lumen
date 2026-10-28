// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

public sealed class WindowsKnownFolders : IKnownFolders
{
    public string ProgramData { get; } = Get(Environment.SpecialFolder.CommonApplicationData);

    public string ProgramFiles { get; } = Get(Environment.SpecialFolder.ProgramFiles);

    public string ProgramFilesX86 { get; } = Get(Environment.SpecialFolder.ProgramFilesX86);

    public string CommonStartMenuPrograms { get; } = Get(Environment.SpecialFolder.CommonPrograms);

    public string UserStartMenuPrograms { get; } = Get(Environment.SpecialFolder.Programs);

    private static string Get(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
}
