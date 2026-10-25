// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Providers.Tests.Fakes;

internal sealed class FakeKnownFolders : IKnownFolders
{
    public string ProgramData { get; init; } = @"C:\ProgramData";

    public string ProgramFiles { get; init; } = @"C:\Program Files";

    public string ProgramFilesX86 { get; init; } = @"C:\Program Files (x86)";
}
