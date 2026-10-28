// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net.Http;
using Lumen.App.Localization;
using Lumen.App.ViewModels;
using Lumen.App.Views;
using Lumen.Core;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Data;
using Lumen.Data.Migrations;
using Lumen.Core.Settings;
using Lumen.Platform.Windows;
using Lumen.Platform.Windows.Security;
using Lumen.Providers;
using Lumen.Providers.Apps;
using Lumen.Providers.BattleNet;
using Lumen.Providers.Ea;
using Lumen.Providers.Epic;
using Lumen.Providers.Ubisoft;
using Lumen.Providers.Xbox;
using Lumen.Providers.Gog;
using Lumen.Providers.Manual;
using Lumen.Providers.Steam;
using Lumen.Providers.SteamGridDb;
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
        builder.Services.AddSingleton<IShortcutReader, ShellShortcutReader>();
        builder.Services.AddSingleton<IIndirectStrings, ShellIndirectStrings>();

        // Settings and secrets (DPAPI, CurrentUser). Secrets are never logged or synced.
        builder.Services.AddSingleton<ISecretProtector, DpapiProtector>();
        builder.Services.AddSingleton<ISecretStore, SqliteSecretStore>();
        builder.Services.AddSingleton<ILocalSettings, SqliteLocalSettings>();

        // Steam Web API: a plain HttpClient on purpose. IHttpClientFactory's logging handler would log
        // request URLs, and Steam only accepts the key as a query parameter (security rule 1).
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { BaseAddress = new Uri(SteamWebApi.BaseAddress), Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent());
            return new SteamWebApi(http);
        });

        // Steam sign-in for family sharing (M5): tokens only travel to api.steampowered.com, again on a plain
        // HttpClient (the access token goes in the query string, which must never be logged).
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent());
            return new SteamAccountApi(http);
        });
        builder.Services.AddSingleton(sp => new SteamAccount(
            sp.GetRequiredService<SteamAccountApi>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<ILocalSettings>()));

        // Epic sign-in (M5): tokens only travel to Epic's account, library and catalog services, on a plain HttpClient.
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent());
            return new EpicAccountApi(http);
        });
        builder.Services.AddSingleton(sp => new EpicAccount(
            sp.GetRequiredService<EpicAccountApi>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<ILocalSettings>()));
        builder.Services.AddSingleton<IMetadataCache, SqliteMetadataCache>();

        // GOG sign-in (M5): tokens only travel to auth.gog.com / embed.gog.com, on a plain HttpClient (GOG's token
        // endpoint takes them in the query string, which must never be logged).
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent());
            return new GogAccountApi(http);
        });
        builder.Services.AddSingleton(sp => new GogAccount(
            sp.GetRequiredService<GogAccountApi>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<ILocalSettings>()));

        // Library
        builder.Services.AddSingleton(sp => new SteamProvider(
            sp.GetRequiredService<IRegistryReader>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<ILocalSettings>(),
            sp.GetRequiredService<SteamWebApi>(),
            account: sp.GetRequiredService<SteamAccount>()));
        builder.Services.AddSingleton<ILibraryProvider>(sp => sp.GetRequiredService<SteamProvider>());
        builder.Services.AddSingleton<ILibraryProvider>(sp => new EpicProvider(
            sp.GetRequiredService<IRegistryReader>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IKnownFolders>(),
            sp.GetRequiredService<EpicAccount>(),
            sp.GetRequiredService<IMetadataCache>()));
        builder.Services.AddSingleton<ILibraryProvider>(sp => new GogProvider(
            sp.GetRequiredService<IRegistryReader>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<GogAccount>()));
        builder.Services.AddSingleton<ILibraryProvider, UbisoftProvider>();
        builder.Services.AddSingleton<ILibraryProvider, EaProvider>();
        builder.Services.AddSingleton<ILibraryProvider, BattleNetProvider>();
        builder.Services.AddSingleton<ILibraryProvider, XboxProvider>();
        builder.Services.AddSingleton<IManualItemStore, SqliteManualItemStore>();
        builder.Services.AddSingleton<ILibraryProvider, ManualItemsProvider>();

        // Apps on this PC (M4): suggestions until the user adds them in "Discover apps".
        builder.Services.AddSingleton<ILibraryProvider>(sp => new StartMenuProvider(
            sp.GetRequiredService<IKnownFolders>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IShortcutReader>(),
            sp.GetRequiredService<IIndirectStrings>()));
        builder.Services.AddSingleton<ILibraryProvider>(sp => new StoreAppsProvider(
            sp.GetRequiredService<IPackageCatalog>(),
            sp.GetRequiredService<IFileSystem>()));
        builder.Services.AddSingleton<IAppChoiceStore, SqliteAppChoiceStore>();
        builder.Services.AddSingleton<ICollectionStore, SqliteCollectionStore>();
        builder.Services.AddSingleton<CollectionService>();
        builder.Services.AddSingleton(_ => TitleCleanup.CreateNormalizer());
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<LibraryAutoRefresh>();
        builder.Services.AddSingleton<ILibraryCache, SqliteLibraryCache>();
        builder.Services.AddSingleton<IMergeRuleStore, SqliteMergeRuleStore>();
        builder.Services.AddSingleton<IItemOverrideStore, SqliteItemOverrideStore>();
        builder.Services.AddSingleton(_ => new LibraryMerger(TitleCleanup.CreateNormalizer()));
        builder.Services.AddSingleton(_ => new LibraryFilter(TitleCleanup.CreateNormalizer()));
        builder.Services.AddSingleton(sp => new LibraryService(
            sp.GetServices<ILibraryProvider>(),
            sp.GetRequiredService<ILibraryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LibraryService>>(),
            providerTimeout: null,
            merger: sp.GetRequiredService<LibraryMerger>(),
            ruleStore: sp.GetRequiredService<IMergeRuleStore>(),
            overrideStore: sp.GetRequiredService<IItemOverrideStore>(),
            appChoiceStore: sp.GetRequiredService<IAppChoiceStore>(),
            playtimeStore: sp.GetRequiredService<IPlaytimeStore>()));
        // Playtime (M6): sessions of what Lumen launches, from the process list (no admin), stored per machine.
        builder.Services.AddSingleton<IProcessList, WindowsProcessList>();
        builder.Services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
        builder.Services.AddSingleton<Lumen.Core.Shell.IGamepadReader, WindowsGamepadReader>();
        builder.Services.AddSingleton<IPlaytimeStore, SqlitePlaytimeStore>();
        builder.Services.AddSingleton(sp => new PlaytimeTracker(
            sp.GetRequiredService<IProcessList>(),
            sp.GetRequiredService<IPlaytimeStore>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PlaytimeTracker>>()));
        builder.Services.AddSingleton(sp => new LaunchService(
            sp.GetRequiredService<ILauncher>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LaunchService>>(),
            sp.GetRequiredService<PlaytimeTracker>()));

        // Artwork (allowed hosts only): Steam CDN first, then SteamGridDB when the user saved a key. Downloads are
        // cached under %LOCALAPPDATA%\Lumen\cache\artwork; user-picked covers live in artwork-overrides.
        builder.Services.AddSingleton<IArtworkLookupCache, SqliteArtworkLookupCache>();
        builder.Services.AddSingleton(_ =>
        {
            // Plain HttpClient on purpose: the API key is in a header, and no handler logs requests.
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent());
            return new SteamGridDbClient(http);
        });
        builder.Services.AddSingleton<IArtworkSource, SteamArtworkSource>();
        builder.Services.AddSingleton<IArtworkSource>(sp => new SteamGridDbArtworkSource(
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<SteamGridDbClient>(),
            sp.GetRequiredService<IArtworkLookupCache>(),
            TitleCleanup.CreateNormalizer()));
        // Icons read on this PC (no network): package logos, then programs' own icons.
        builder.Services.AddSingleton<ILocalArtworkSource>(sp => new PackageLogoSource(sp.GetRequiredService<IFileSystem>()));
        builder.Services.AddSingleton<ILocalArtworkSource>(sp => new ProgramIconSource(sp.GetRequiredService<IShortcutReader>(), sp.GetRequiredService<IFileSystem>()));
        builder.Services.AddSingleton(sp =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent());
            return new ArtworkService(
                http,
                sp.GetServices<IArtworkSource>(),
                paths.ArtworkCacheDirectory,
                paths.ArtworkOverridesDirectory,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ArtworkService>>(),
                localSources: sp.GetServices<ILocalArtworkSource>());
        });

        builder.Services.AddSingleton<AccentService>();
        builder.Services.AddSingleton<ImageCache>();
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<DiscoverAppsViewModel>();
        builder.Services.AddSingleton<CollectionsViewModel>();
        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddSingleton<SteamSettingsViewModel>();
        builder.Services.AddSingleton<SteamFamilyViewModel>();
        builder.Services.AddSingleton<EpicAccountViewModel>();
        builder.Services.AddSingleton<GogAccountViewModel>();
        builder.Services.AddSingleton<ArtworkSettingsViewModel>();
        builder.Services.AddSingleton<LanguageSettingsViewModel>();
        builder.Services.AddSingleton<WindowSettingsViewModel>();
        builder.Services.AddSingleton<TrayService>();
        builder.Services.AddSingleton<HotkeyService>();
        builder.Services.AddSingleton<GamepadService>();
        builder.Services.AddSingleton(_ => new QuickSearch(TitleCleanup.CreateNormalizer()));
        builder.Services.AddSingleton<QuickSearchViewModel>();
        builder.Services.AddSingleton<QuickSearchWindow>();
        builder.Services.AddSingleton<FirstRunViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private static string UserAgent() => "Lumen/" + (typeof(AppHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
}
