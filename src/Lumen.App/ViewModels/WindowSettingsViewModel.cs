// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
using Lumen.Core.Shell;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>
/// Settings → General (PLAN-v1 §4.7): "minimize to tray" (closing the window keeps Lumen in the notification area,
/// on by default) and "start with Windows" (off by default; Lumen's own entry in the user's startup list, started in
/// the tray). The startup state is read from Windows, so turning it off in Task Manager shows here too. Also the
/// quick search shortcut: "Change" records the next key combination pressed (the view forwards it).
/// </summary>
public sealed partial class WindowSettingsViewModel : ObservableObject
{
    private readonly ILocalSettings _settings;
    private readonly IStartupRegistration _startup;
    private readonly HotkeyService _hotkeys;
    private readonly ILocalizer _localizer;
    private readonly ILogger<WindowSettingsViewModel> _logger;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyText))]
    private bool _isRecordingHotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkeyStatus))]
    private string? _hotkeyStatus;

    public WindowSettingsViewModel(
        ILocalSettings settings,
        IStartupRegistration startup,
        HotkeyService hotkeys,
        ILocalizer localizer,
        ILogger<WindowSettingsViewModel> logger)
    {
        _settings = settings;
        _startup = startup;
        _hotkeys = hotkeys;
        _localizer = localizer;
        _logger = logger;
        hotkeys.Changed += (_, _) => RefreshHotkey();
        localizer.CultureChanged += (_, _) => RefreshHotkey();
    }

    /// <summary>The current shortcut, or the "press keys" prompt while recording.</summary>
    public string HotkeyText => IsRecordingHotkey ? _localizer["Settings_Hotkey_Recording"] : _hotkeys.Current.ToString();

    public bool HasHotkeyStatus => !string.IsNullOrEmpty(HotkeyStatus);

    /// <summary>Called by the view with the keys pressed while recording (null key = not a key Lumen accepts).</summary>
    public async Task CaptureHotkeyAsync(HotkeyModifiers modifiers, string? key)
    {
        IsRecordingHotkey = false;
        if (key is null || !Hotkey.IsAllowed(modifiers, key))
        {
            HotkeyStatus = _localizer["Settings_Hotkey_Invalid"];
            return;
        }

        await ChangeHotkeyAsync(new Hotkey(modifiers, key));
    }

    [RelayCommand]
    private void RecordHotkey()
    {
        HotkeyStatus = null;
        IsRecordingHotkey = true;
    }

    [RelayCommand]
    private void CancelRecording() => IsRecordingHotkey = false;

    [RelayCommand]
    private Task ResetHotkeyAsync() => ChangeHotkeyAsync(Hotkey.Default);

    private async Task ChangeHotkeyAsync(Hotkey hotkey)
    {
        HotkeyStatus = await _hotkeys.ChangeAsync(hotkey, CancellationToken.None)
            ? null
            : string.Format(CultureInfo.CurrentCulture, _localizer["Settings_Hotkey_Taken"], hotkey);
    }

    private void RefreshHotkey()
    {
        OnPropertyChanged(nameof(HotkeyText));
        if (!_hotkeys.IsRegistered && HotkeyStatus is null)
        {
            HotkeyStatus = string.Format(CultureInfo.CurrentCulture, _localizer["Settings_Hotkey_Taken"], _hotkeys.Current);
        }
    }

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _startWithWindows;

    public async Task InitializeAsync(CancellationToken ct)
    {
        _loading = true;
        try
        {
            CloseToTray = await _settings.GetAsync(LocalSettingKeys.CloseToTray, ct) != "0";
            StartWithWindows = _startup.IsEnabled();
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
                _startup.Enable(StartupOptions.StartWithWindowsCommand(path));
            }
            else
            {
                _startup.Disable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or ArgumentException)
        {
            LogStartupFailed(ex);
            _loading = true;
            StartWithWindows = _startup.IsEnabled(); // show what Windows really has
            _loading = false;
        }
    }

    private async Task SaveAsync(bool closeToTray)
    {
        try
        {
            await _settings.SetAsync(LocalSettingKeys.CloseToTray, closeToTray ? "1" : "0", CancellationToken.None);
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
