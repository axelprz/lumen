// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Localization;
using Lumen.Providers.Steam;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>
/// Settings → Steam family (PLAN-v1 §4.8): sign in on Steam's own website in a built-in browser, as Playnite does. The
/// card shows the account, since when, and exactly what Lumen does with the session. Disconnect signs out on Steam and
/// deletes the browser's data.
/// </summary>
public sealed partial class SteamFamilyViewModel : ObservableObject
{
    private readonly SteamWebSession _session;
    private readonly SteamFamilyAccess _family;
    private readonly LibraryViewModel _library;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SteamFamilyViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool _isConnected;

    [ObservableProperty]
    private string? _accountText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private StatusKind _statusKind;

    public SteamFamilyViewModel(SteamWebSession session, SteamFamilyAccess family, LibraryViewModel library, ILocalizer localizer, ILogger<SteamFamilyViewModel> logger)
    {
        _session = session;
        _family = family;
        _library = library;
        _localizer = localizer;
        _logger = logger;
        var ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _family.Changed += (_, _) => ui.Post(_ => ShowReconnectIfNeeded(), null);
    }

    public bool IsDisconnected => !IsConnected;

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            ShowAccount(await _session.GetAccountAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        Status = null;
        if (Application.Current.MainWindow is not { } owner)
        {
            return;
        }

        var account = await _session.SignInAsync(owner);
        if (account is null)
        {
            return; // closed without signing in
        }

        _family.Reset();
        ShowAccount(account);
        SetStatus(StatusKind.Success, "SteamWeb_Connected");
        await _library.RescanCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        var ranSteamSignOut = await _session.SignOutAsync();
        _family.Reset();
        ShowAccount(null);
        SetStatus(ranSteamSignOut ? StatusKind.Success : StatusKind.Warning, ranSteamSignOut ? "SteamWeb_Disconnected" : "SteamWeb_DisconnectedLocally");
        await _library.RescanCommand.ExecuteAsync(null);
    }

    private void ShowAccount(SteamWebAccount? account)
    {
        IsConnected = account is not null;
        AccountText = account is null
            ? null
            : string.Format(CultureInfo.CurrentCulture, _localizer["SteamWeb_Account"], account.Name, account.Since.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));
        ShowReconnectIfNeeded();
    }

    private void ShowReconnectIfNeeded()
    {
        if (_family.NeedsReconnect && IsConnected)
        {
            SetStatus(StatusKind.Warning, "SteamWeb_NeedsReconnect");
        }
    }

    private void SetStatus(StatusKind kind, string key)
    {
        StatusKind = kind;
        Status = _localizer[key];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the Steam website sign-in")]
    private partial void LogLoadFailed(Exception exception);
}
