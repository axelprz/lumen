// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Lumen.App.Hosting;
using Lumen.App.Localization;
using Lumen.App.ViewModels;
using Lumen.App.Views;
using Lumen.Core.Library;
using Lumen.App.Services;
using Lumen.Core.Localization;
using Lumen.Core.Shell;
using Lumen.Platform.Windows;
using Lumen.Providers.Steam;
using Lumen.Platform.Windows.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lumen.Core.Updates;
using Serilog;

namespace Lumen.App;

public partial class App : Application
{
    private IHost? _host;
    private SingleInstance? _instance;
    private TrayService? _tray;
    private HotkeyService? _hotkeys;
    private QuickSearchWindow? _quickSearch;
    private GamepadService? _gamepad;
    private bool _showRequested;
    private bool _shuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Lumen lives on in the tray, so it decides when to exit (Exit in the tray menu, or closing the window
        // with "minimize to tray" off), not WPF.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var options = StartupOptions.Parse(e.Args);

        // Opening Lumen while it already runs brings that one back instead of starting a second copy.
        _instance = SingleInstance.TryAcquire(AppContext.BaseDirectory, () => Dispatcher.BeginInvoke(OnShowRequested));
        if (_instance is null)
        {
            Shutdown(0);
            return;
        }

        var paths = WindowsLumenPaths.Create();
        Log.Logger = LoggingSetup.CreateLogger(paths);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // config.json is not loaded until the sync folder exists (M8); until then follow the OS language.
        var localizer = LocalizationService.Instance;
        localizer.Culture = LanguageResolver.Resolve(LanguageResolver.SystemPreference, LocalizationService.SystemUiCulture);

        try
        {
            Log.Information("Lumen starting (UI culture {Culture})", localizer.Culture.Name);
            _host = AppHost.Build(paths, localizer);
            await _host.StartAsync().ConfigureAwait(true);

            // Saved language first (the database is ready once the host started), then decide on the wizard,
            // so the window opens already in the right language and on the right screen.
            await _host.Services.GetRequiredService<LanguageSettingsViewModel>().InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            await _host.Services.GetRequiredService<FirstRunViewModel>().OpenIfNeededAsync(CancellationToken.None).ConfigureAwait(true);
            await _host.Services.GetRequiredService<CollectionService>().LoadAsync(CancellationToken.None).ConfigureAwait(true);

            // Playtime: close sessions a previous run left open (Lumen closed mid-game), then keep totals current.
            var playtime = _host.Services.GetRequiredService<PlaytimeTracker>();
            await playtime.RecoverAsync(CancellationToken.None).ConfigureAwait(true);
            var library = _host.Services.GetRequiredService<LibraryService>();
            playtime.SessionsChanged += (_, _) => _ = library.ReloadUserChoicesAsync(CancellationToken.None);

            await _host.Services.GetRequiredService<WindowSettingsViewModel>().InitializeAsync(CancellationToken.None).ConfigureAwait(true);

            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Closed += OnMainWindowClosed;
            MainWindow = window;
            _tray = _host.Services.GetRequiredService<TrayService>();
            _tray.Attach(window, () => ShutdownAsync(exitCode: 0));

            // Quick search: the global shortcut and the tray open the same box over any app.
            _quickSearch = _host.Services.GetRequiredService<QuickSearchWindow>();
            _hotkeys = _host.Services.GetRequiredService<HotkeyService>();
            _hotkeys.Pressed += (_, _) => _quickSearch.Open();
            _tray.OpenQuickSearch = _quickSearch.Open;

            // Sync folder: pick up the one chosen before, and show a balloon when manual items arrive from it.
            var syncCoordinator = _host.Services.GetRequiredService<SyncCoordinator>();
            syncCoordinator.Notify += _tray.Notify;

            // A routine chosen from the tray or the search that needs its preview brings the window forward.
            _host.Services.GetRequiredService<RoutinesViewModel>().ReviewRequested += (_, _) => _tray.ShowWindow();
            await _hotkeys.StartAsync(CancellationToken.None).ConfigureAwait(true);

            // Gamepad: works while a Lumen window is in front.
            _gamepad = _host.Services.GetRequiredService<GamepadService>();
            _gamepad.Attach(window, _quickSearch, _quickSearch.Open, () => window.PageRoot);

            // Started with Windows: stay in the tray until the user opens Lumen.
            if (!options.StartInTray || _showRequested)
            {
                window.Show();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Lumen failed to start");
            MessageBox.Show(
                localizer["Startup_Error_Message"],
                localizer["Startup_Error_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ShutdownAsync(exitCode: 1).ConfigureAwait(true);
            return;
        }

        // The window is already visible: load settings, show the cached library, then rescan in the background.
        await _host.Services.GetRequiredService<SettingsViewModel>().InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await _host.Services.GetRequiredService<LibraryViewModel>().InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await _host.Services.GetRequiredService<RoutinesViewModel>().InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await _host.Services.GetRequiredService<SidebarFooterViewModel>().InitializeAsync().ConfigureAwait(true);
        await _host.Services.GetRequiredService<SyncCoordinator>().StartAsync(CancellationToken.None).ConfigureAwait(true);

        // Updates: look for a new version at start-up and daily (if on); the user is told once per version and decides
        // when to download and when to restart.
        var updates = _host.Services.GetRequiredService<UpdateService>();
        updates.BeforeRestart = ReleaseForRestart;
        updates.Announce += (_, offer) => Dispatcher.BeginInvoke(() =>
        {
            var localizer = _host?.Services.GetRequiredService<ILocalizer>();
            if (localizer is not null)
            {
                _tray?.Notify(localizer["Update_NoticeTitle"], string.Format(CultureInfo.CurrentCulture, localizer["Update_NoticeMessage"], offer.Version));
            }
        });
        await updates.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        updates.StartAutomaticChecks();

        // From now on, notice installs/uninstalls by themselves (store folders + coming back to the window).
        var autoRefresh = _host.Services.GetRequiredService<LibraryAutoRefresh>();
        await autoRefresh.StartAsync(CancellationToken.None).ConfigureAwait(true);

        // Steam sign-in is turned off; a sign-in kept from an earlier version is revoked at Steam and deleted.
        _ = RetireSteamSignInAsync(_host.Services.GetRequiredService<SteamAccount>());
        MainWindow.Activated += (_, _) => autoRefresh.OnWindowActivated();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Lumen exiting with code {ExitCode}", e.ApplicationExitCode);
        Log.CloseAndFlush();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e) =>
        await ShutdownAsync(exitCode: 0).ConfigureAwait(true);

    private static async Task RetireSteamSignInAsync(SteamAccount account)
    {
        try
        {
            if (await account.GetInfoAsync(CancellationToken.None).ConfigureAwait(false) is null)
            {
                return;
            }

            var revoked = await account.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            Log.Information("Steam sign-in removed (feature turned off); revoked at Steam: {Revoked}", revoked);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warning(ex, "Could not remove the stored Steam sign-in");
        }
    }

    private void OnShowRequested()
    {
        if (_tray is null)
        {
            _showRequested = true; // still starting: show the window once it exists
            return;
        }

        _tray.ShowWindow();
    }

    // Just before Velopack installs an update and starts Lumen again: let go of what the new copy needs (tray icon, shortcut,
    // the "one Lumen per folder" lock), as a normal exit would.
    private void ReleaseForRestart()
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _gamepad?.Dispose();
        _instance?.Dispose();
        Log.Information("Lumen restarting to install an update");
        Log.CloseAndFlush();
    }

    private async Task ShutdownAsync(int exitCode)
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _tray?.Dispose(); // removes the icon right away instead of on the next mouse-over
        _hotkeys?.Dispose();
        _quickSearch?.CloseForExit();
        _gamepad?.Dispose();
        if (_host is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _host.StopAsync(timeout.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while stopping the host");
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }

        Shutdown(exitCode);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
}
