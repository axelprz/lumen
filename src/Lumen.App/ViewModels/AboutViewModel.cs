// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.IO;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Hosting;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.App.ViewModels;

/// <summary>A bundled component and its license, as listed in THIRD-PARTY-NOTICES.txt.</summary>
public sealed record AboutComponent(string Name, string License);

/// <summary>
/// Settings → About (PLAN-v1 §4.7): version, license, the repository, and what Lumen is built with. The full license
/// texts ship next to Lumen.exe (LICENSE.txt, THIRD-PARTY-NOTICES.txt); a repo test keeps the list and the file in step
/// with the packages the projects reference.
/// </summary>
public sealed partial class AboutViewModel(ILauncher launcher)
{
    public const string NoticesFile = "THIRD-PARTY-NOTICES.txt";

    // Instance properties: WPF bindings do not see static ones through the DataContext.
    public string VersionText => ShellViewModel.VersionText;

    public IReadOnlyList<AboutComponent> Components => All;

    public static IReadOnlyList<AboutComponent> All { get; } =
    [
        new(".NET", "MIT"),
        new("CommunityToolkit.Mvvm", "MIT"),
        new("Dapper", "Apache-2.0"),
        new("H.NotifyIcon", "MIT"),
        new("Microsoft.Data.Sqlite", "MIT"),
        new("SQLitePCLRaw", "Apache-2.0"),
        new("SQLite", "Public domain"),
        new("Microsoft.Extensions", "MIT"),
        new("Microsoft WebView2", "BSD-style (Microsoft)"),
        new("Serilog", "Apache-2.0"),
        new("Velopack", "MIT"),
        new("Exo 2", "SIL OFL 1.1"),
    ];

    /// <summary>Opens the project's page in the default browser (the user's browser, not a request from Lumen).</summary>
    [RelayCommand]
    private Task OpenRepositoryAsync() => launcher.StartAsync(new LaunchSpec(LaunchKind.Uri, AppHost.RepositoryUrl), CancellationToken.None);

    /// <summary>Opens the bundled notices with the default text viewer; falls back to the copy in the repository.</summary>
    [RelayCommand]
    private Task OpenNoticesAsync()
    {
        var local = Path.Combine(AppContext.BaseDirectory, NoticesFile);
        var target = File.Exists(local) ? local : AppHost.RepositoryUrl + "/blob/main/" + NoticesFile;
        return launcher.StartAsync(new LaunchSpec(LaunchKind.Uri, target), CancellationToken.None);
    }
}
