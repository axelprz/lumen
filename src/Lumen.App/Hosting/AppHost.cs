// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net.Http;
using Lumen.App.Localization;
using Lumen.App.ViewModels;
using Lumen.App.Views;
using Lumen.Core;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Data;
using Lumen.Data.Migrations;
using Lumen.Core.Settings;
using Lumen.Platform.Windows;
using Lumen.Platform.Windows.Security;
using Lumen.Providers.BattleNet;
using Lumen.Providers.Ea;
using Lumen.Providers.Epic;
using Lumen.Providers.Ubisoft;
using Lumen.Providers.Xbox;
using Lumen.Providers.Gog;
using Lumen.Providers.Manual;
using Lumen.Providers.Steam;
using Lumen.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Lumen.App.Hosting;

/// <summary>Composition root: every service, view model and view is registered here.</summary>
public static class AppHost
{
    public static IHost Build(LumenPaths paths, LocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(localizer);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Lumen",
            ContentRootPath = AppContext.BaseDirectory,
            DisableDefaults = true,
        });

        // Serilog's static logger is configured in App.OnStartup so startup failures are logged too.
        builder.Services.AddSerilog();

        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(localizer);
        builder.Services.AddSingleton<ILocalizer>(localizer);

        builder.Services.AddSingleton<ISqliteConnectionFactory>(new SqliteConnectionFactory(paths.DatabasePath));
        builder.Services.AddSingleton<IReadOnlyList<Migration>>(_ => MigrationLoader.LoadEmbedded());
        builder.Services.AddSingleton<MigrationRunner>();
        builder.Services.AddHostedService<DatabaseInitializer>();

        // Platform (read-only registry/files, shell launching)
        builder.Services.AddSingleton<IRegistryReader, WindowsRegistryReader>();
        builder.Services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        builder.Services.AddSingleton<IKnownFolders, WindowsKnownFolders>();
        builder.Services.AddSingleton<IPackageCatalog, WindowsPackageCatalog>();
        builder.Services.AddSingleton<ILauncher, ShellLauncher>();

        // Settings and secrets (DPAPI, CurrentUser). Secrets are never logged or synced.
        builder.Services.AddSingleton<ISecretProtector, DpapiProtector>();
        builder.Services.AddSingleton<ISecretStore, SqliteSecretStore>();
        builder.Services.AddSingleton<ILocalSettings, SqliteLocalSettings>();

        // Steam Web API: a plain HttpClient on purpose. IHttpClientFactory's logging handler would log
        // request URLs, and Steam only accepts the key as a query parameter (security rule 1).
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { BaseAddress = new Uri(SteamWebApi.BaseAddress), Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Lumen/" + (typeof(AppHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));
            return new SteamWebApi(http);
        });

        // Library
        builder.Services.AddSingleton<SteamProvider>();
        builder.Services.AddSingleton<ILibraryProvider>(sp => sp.GetRequiredService<SteamProvider>());
        builder.Services.AddSingleton<ILibraryProvider, EpicProvider>();
        builder.Services.AddSingleton<ILibraryProvider, GogProvider>();
        builder.Services.AddSingleton<ILibraryProvider, UbisoftProvider>();
        builder.Services.AddSingleton<ILibraryProvider, EaProvider>();
        builder.Services.AddSingleton<ILibraryProvider, BattleNetProvider>();
        builder.Services.AddSingleton<ILibraryProvider, XboxProvider>();
        builder.Services.AddSingleton<IManualItemStore, SqliteManualItemStore>();
        builder.Services.AddSingleton<ILibraryProvider, ManualItemsProvider>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<ILibraryCache, SqliteLibraryCache>();
        builder.Services.AddSingleton(sp => new LibraryService(
            sp.GetServices<ILibraryProvider>(),
            sp.GetRequiredService<ILibraryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LibraryService>>()));
        builder.Services.AddSingleton<LaunchService>();

        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddSingleton<SteamSettingsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
