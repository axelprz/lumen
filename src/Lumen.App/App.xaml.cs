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
using Lumen.Core.Localization;
using Lumen.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Lumen.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Closed += OnMainWindowClosed;
            MainWindow = window;
            window.Show();
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

        // From now on, notice installs/uninstalls by themselves (store folders + coming back to the window).
        var autoRefresh = _host.Services.GetRequiredService<LibraryAutoRefresh>();
        await autoRefresh.StartAsync(CancellationToken.None).ConfigureAwait(true);
        MainWindow.Activated += (_, _) => autoRefresh.OnWindowActivated();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Lumen exiting with code {ExitCode}", e.ApplicationExitCode);
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e) =>
        await ShutdownAsync(exitCode: 0).ConfigureAwait(true);

    private async Task ShutdownAsync(int exitCode)
    {
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
