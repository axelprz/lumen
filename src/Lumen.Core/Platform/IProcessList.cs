// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>The image paths of running processes, for playtime tracking. Read-only; processes Windows won't describe without admin are skipped.</summary>
public interface IProcessList
{
    IReadOnlyList<string> GetImagePaths();
}
