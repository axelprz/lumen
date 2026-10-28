// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
using Lumen.Core.Shell;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>
/// Settings → General (PLAN-v1 §4.7): "minimize to tray" (closing the window keeps Lumen in the notification area,
/// on by default) and "start with Windows" (off by default; Lumen's own entry in the user's startup list, started in
/// the tray). The startup state is read from Windows, so turning it off in Task Manager shows here too.
/// </summary>
public sealed partial class WindowSettingsViewModel(
    ILocalSettings settings,
    IStartupRegistration startup,
    ILogger<WindowSettingsViewModel> logger) : ObservableObject
{
    private bool _loading;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _startWithWindows;

    public async Task InitializeAsync(CancellationToken ct)
    {
        _loading = true;
        try
        {
            CloseToTray = await settings.GetAsync(LocalSettingKeys.CloseToTray, ct) != "0";
            StartWithWindows = startup.IsEnabled();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex); // keep the defaults
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_loading)
        {
            _ = SaveAsync(value);
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            if (value && Environment.ProcessPath is { } path)
            {
                startup.Enable(StartupOptions.StartWithWindowsCommand(path));
            }
            else
            {
                startup.Disable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or ArgumentException)
        {
            LogStartupFailed(ex);
            _loading = true;
            StartWithWindows = startup.IsEnabled(); // show what Windows really has
            _loading = false;
        }
    }

    private async Task SaveAsync(bool closeToTray)
    {
        try
        {
            await settings.SetAsync(LocalSettingKeys.CloseToTray, closeToTray ? "1" : "0", CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex); // still applies for this session
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the window settings; using defaults")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the tray setting")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not change Lumen's start-with-Windows entry")]
    private partial void LogStartupFailed(Exception exception);
}
