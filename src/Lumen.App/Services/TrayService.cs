// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Lumen.App.ViewModels;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Lumen.App.Services;

/// <summary>
/// The notification-area icon (PLAN-v1 §4.3): a click opens Lumen; the menu has Open, Quick search, the most recently played games
/// (launched straight from the menu) and Exit. Closing the main window hides it here when "minimize to tray" is on,
/// with a one-time notice so Lumen never seems to vanish. Lives on the UI thread.
/// </summary>
public sealed partial class TrayService(
    HotkeyService hotkeys,
    LibraryService library,
    CollectionService collections,
    LaunchService launcher,
    WindowSettingsViewModel windowSettings,
    ILocalSettings settings,
    ILocalizer localizer,
    ILogger<TrayService> logger) : IDisposable
{
    /// <summary>Most games in the tray menu's recent list.</summary>
    public const int MaxRecent = 5;

    private TaskbarIcon? _icon;
    private Window? _window;
    private Func<Task>? _exit;
    private bool _exiting;

    /// <summary>Opens the quick search box (set once it exists).</summary>
    public Action? OpenQuickSearch { get; set; }

    /// <summary>Shows the icon and takes over the window's close button.</summary>
    /// <param name="exit">Shuts Lumen down (the menu's Exit).</param>
    public void Attach(Window window, Func<Task> exit)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(exit);

        _window = window;
        _exit = exit;
        var icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/Lumen.ico", UriKind.Absolute)),
            ToolTipText = localizer["App_Title"],
            NoLeftClickDelay = true,
            ContextMenu = new ContextMenu(),
        };
        icon.TrayLeftMouseUp += (_, _) => ShowWindow();
        icon.PreviewTrayContextMenuOpen += (_, _) => BuildMenu();
        icon.ForceCreate(enablesEfficiencyMode: false); // efficiency mode would slow playtime tracking down
        localizer.CultureChanged += (_, _) => icon.ToolTipText = localizer["App_Title"];
        _icon = icon;

        window.Closing += OnWindowClosing;
    }

    /// <summary>Brings the main window back (tray click, menu, or Lumen opened again).</summary>
    public void ShowWindow()
    {
        if (_window is not { } window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true; // Windows only raises a window above the active app this way
        window.Topmost = false;
        window.Focus();
    }

    /// <summary>Really closes Lumen (not just the window).</summary>
    public async Task ExitAsync()
    {
        _exiting = true;
        if (_exit is not null)
        {
            await _exit();
        }
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting || !windowSettings.CloseToTray || sender is not Window window)
        {
            return;
        }

        e.Cancel = true;
        window.Hide();
        _ = ShowHintOnceAsync();
    }

    private async Task ShowHintOnceAsync()
    {
        try
        {
            if (await settings.GetAsync(LocalSettingKeys.TrayHintShown, CancellationToken.None) == "1")
            {
                return;
            }

            _icon?.ShowNotification(localizer["Tray_Hint_Title"], localizer["Tray_Hint_Message"], NotificationIcon.Info);
            await settings.SetAsync(LocalSettingKeys.TrayHintShown, "1", CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHintFailed(ex);
        }
    }

    // Built each time it opens, so it always has the current recent games and language.
    private void BuildMenu()
    {
        if (_icon?.ContextMenu is not { } menu)
        {
            return;
        }

        menu.Items.Clear();
        menu.Items.Add(Item(localizer["Tray_Open"], ShowWindow, bold: true));
        if (OpenQuickSearch is { } openSearch)
        {
            menu.Items.Add(Item(string.Format(CultureInfo.CurrentCulture, localizer["Tray_QuickSearch"], hotkeys.Current), openSearch));
        }

        var recent = HomeFeed.Build(library.Items, library.FirstSeen, collections.Favorites).ContinuePlaying
            .Where(i => LaunchService.PrimaryAction(i.Primary) is not null)
            .Take(MaxRecent)
            .ToList();
        if (recent.Count > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = new TextBlock { Text = localizer["Tray_Recent"] }, IsEnabled = false });
            foreach (var item in recent)
            {
                menu.Items.Add(Item(item.Title, () => _ = LaunchAsync(item)));
            }
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(localizer["Tray_Exit"], () => _ = ExitAsync()));
    }

    private async Task LaunchAsync(LibraryItem item)
    {
        if (await launcher.RunPrimaryActionAsync(item.Primary, CancellationToken.None) != LaunchOutcome.Started)
        {
            _icon?.ShowNotification(
                localizer["App_Title"],
                string.Format(CultureInfo.CurrentCulture, localizer["Library_LaunchFailed"], item.Title),
                NotificationIcon.Warning);
        }
    }

    // Titles go in as plain text (no access keys), so a game called "_Hello" keeps its underscore.
    private static MenuItem Item(string text, Action onClick, bool bold = false)
    {
        var item = new MenuItem
        {
            Header = new TextBlock { Text = text },
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not show the tray notice")]
    private partial void LogHintFailed(Exception exception);
}
