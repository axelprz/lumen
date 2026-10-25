// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

public enum ItemKind
{
    Game,
    App,
}

public enum InstallState
{
    Installed,
    NotInstalled,
    Unknown,
}

public enum LaunchKind
{
    /// <summary>Shell-executes a URI such as <c>steam://rungameid/570</c>.</summary>
    Uri,

    /// <summary>Starts an executable or shortcut directly.</summary>
    Executable,

    /// <summary>Starts a packaged app via <c>shell:AppsFolder\{AUMID}</c>.</summary>
    ShellAppsFolder,

    /// <summary>Starts an executable with store-specific arguments (e.g. GOG Galaxy, Battle.net).</summary>
    ProcessWithArgs,
}

public sealed record LaunchSpec(LaunchKind Kind, string Target, string? Arguments = null, string? WorkingDirectory = null);

/// <summary>One item as reported by one provider. Several entries may later merge into one library item (M2).</summary>
public sealed record LibraryEntry(
    ItemRef Ref,
    ItemKind Kind,
    string Title,
    InstallState State,
    string? InstallDirectory,
    LaunchSpec? Launch,
    LaunchSpec? Install,
    IReadOnlyDictionary<string, string> Extra)
{
    public static IReadOnlyDictionary<string, string> NoExtra { get; } = new Dictionary<string, string>();

    public bool IsInstalled => State == InstallState.Installed;
}
