// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>Windows known folders, abstracted so providers can be tested with fixed paths on any OS.</summary>
public interface IKnownFolders
{
    /// <summary>%ProgramData%, e.g. C:\ProgramData.</summary>
    string ProgramData { get; }

    /// <summary>%ProgramFiles% (64-bit), e.g. C:\Program Files.</summary>
    string ProgramFiles { get; }

    /// <summary>%ProgramFiles(x86)%, e.g. C:\Program Files (x86).</summary>
    string ProgramFilesX86 { get; }

    /// <summary>All users' Start menu programs, e.g. C:\ProgramData\Microsoft\Windows\Start Menu\Programs.</summary>
    string CommonStartMenuPrograms { get; }

    /// <summary>The current user's Start menu programs, e.g. %AppData%\Microsoft\Windows\Start Menu\Programs.</summary>
    string UserStartMenuPrograms { get; }
}
