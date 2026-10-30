// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows.Interop;
using Lumen.Core.Settings;
using Lumen.Core.Shell;
using Lumen.Platform.Windows.Windowing;
using Microsoft.Extensions.Logging;

namespace Lumen.App.Services;

/// <summary>
/// The global quick search shortcut (PLAN-v1 §4.3, default Alt+Shift+Space, configurable in Settings → General and
/// saved as <c>ui.hotkey</c>). Registered with Windows on a message-only window; Lumen never sees other keystrokes.
/// If another app already owns the shortcut, Lumen keeps working without it and Settings says so. UI thread only.
/// </summary>
public sealed partial class HotkeyService(ILocalSettings settings, ILogger<HotkeyService> logger) : IDisposable
{
    private const int QuickSearchId = 1;

    private HwndSource? _window;

    /// <summary>Raised on the UI thread when the shortcut is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>Raised when <see cref="Current"/> or <see cref="IsRegistered"/> change.</summary>
    public event EventHandler? Changed;

    public Hotkey Current { get; private set; } = Hotkey.Default;

    /// <summary>False when Windows refused the shortcut (another app owns it).</summary>
    public bool IsRegistered { get; private set; }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            if (Hotkey.TryParse(await settings.GetAsync(LocalSettingKeys.Hotkey, ct), out var saved))
            {
                Current = saved;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex); // use the default
        }

        // HWND_MESSAGE (-3): an invisible window that only receives messages.
        _window = new HwndSource(new HwndSourceParameters("Lumen hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(OnMessage);
        IsRegistered = GlobalHotkeys.Register(_window.Handle, QuickSearchId, Current);
        if (!IsRegistered)
        {
            LogTaken(Current.ToString());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switches to a new shortcut. If Windows refuses it, the previous one stays and false is returned.</summary>
    public async Task<bool> ChangeAsync(Hotkey hotkey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (_window is null)
        {
            return false;
        }

        GlobalHotkeys.Unregister(_window.Handle, QuickSearchId);
        if (!GlobalHotkeys.Register(_window.Handle, QuickSearchId, hotkey))
        {
            LogTaken(hotkey.ToString());
            IsRegistered = GlobalHotkeys.Register(_window.Handle, QuickSearchId, Current);
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        Current = hotkey;
        IsRegistered = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await settings.SetAsync(LocalSettingKeys.Hotkey, hotkey.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex); // works for this session
        }

        return true;
    }

    /// <summary>Picks up a shortcut changed from outside (the sync folder). If Windows refuses it, the current one stays.</summary>
    public async Task ReloadAsync(CancellationToken ct)
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            if (Hotkey.TryParse(await settings.GetAsync(LocalSettingKeys.Hotkey, ct), out var saved) && saved != Current)
            {
                await ChangeAsync(saved, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
        }
    }

    public void Dispose()
    {
        if (_window is not null)
        {
            GlobalHotkeys.Unregister(_window.Handle, QuickSearchId);
            _window.Dispose();
            _window = null;
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotkeys.WmHotkey && wParam.ToInt32() == QuickSearchId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the saved shortcut; using the default")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the shortcut")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused the shortcut {Hotkey} (another app uses it)")]
    private partial void LogTaken(string hotkey);
}
