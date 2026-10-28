// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Providers.Tests.Fakes;

internal sealed class FakeKnownFolders : IKnownFolders
{
    public string ProgramData { get; init; } = @"C:\ProgramData";

    public string ProgramFiles { get; init; } = @"C:\Program Files";

    public string ProgramFilesX86 { get; init; } = @"C:\Program Files (x86)";

    public string CommonStartMenuPrograms { get; init; } = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs";

    public string UserStartMenuPrograms { get; init; } = @"C:\Users\player\AppData\Roaming\Microsoft\Windows\Start Menu\Programs";
}
