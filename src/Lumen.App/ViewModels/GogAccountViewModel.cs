// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Providers.Gog;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>
/// Settings → GOG account (PLAN-v1 §4.8): sign in on GOG's own page in the browser and paste the address it ends on
/// (Lumen never sees the password). Connected, the card shows the account, since when and what Lumen can do;
/// Disconnect deletes the sign-in here (GOG offers no way to revoke it from outside, and the card says so).
/// </summary>
public sealed partial class GogAccountViewModel : ObservableObject
{
    private readonly GogAccount _account;
    private readonly LibraryViewModel _library;
    private readonly ILauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly ILogger<GogAccountViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isEnteringCode;

    [ObservableProperty]
    private string _codeInput = string.Empty;

    [ObservableProperty]
    private string? _accountText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private StatusKind _statusKind;

    public GogAccountViewModel(GogAccount account, LibraryViewModel library, ILauncher shell, IDialogService dialogs, ILocalizer localizer, ILogger<GogAccountViewModel> logger)
    {
        _account = account;
        _library = library;
        _shell = shell;
        _dialogs = dialogs;
        _localizer = localizer;
        _logger = logger;
    }

    public bool IsDisconnected => !IsConnected;

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            await ShowAccountAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
        }
    }

    /// <summary>Opens GOG's sign-in page in the default browser and shows the field for the address.</summary>
    [RelayCommand]
    private async Task OpenSignInAsync()
    {
        SetStatus(StatusKind.None, null);
        CodeInput = string.Empty;
        IsEnteringCode = true;
        try
        {
            await _shell.StartAsync(new LaunchSpec(LaunchKind.Uri, GogAccountApi.SignInPage.AbsoluteUri), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            SetStatus(StatusKind.Error, "GogAccount_BrowserFailed");
        }
    }

    [RelayCommand]
    private void CancelSignIn()
    {
        IsEnteringCode = false;
        CodeInput = string.Empty;
        SetStatus(StatusKind.None, null);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        SetStatus(StatusKind.Working, "GogAccount_Connecting");
        try
        {
            var (result, detail) = await _account.ConnectAsync(CodeInput, CancellationToken.None);
            CodeInput = string.Empty; // the code is single-use; don't leave it on screen
            switch (result)
            {
                case GogConnectResult.Connected:
                    IsEnteringCode = false;
                    await ShowAccountAsync(CancellationToken.None);
                    SetStatus(StatusKind.Success, "GogAccount_Connected");
                    await _library.RescanCommand.ExecuteAsync(null);
                    break;
                case GogConnectResult.NotACode:
                    SetStatus(StatusKind.Error, "GogAccount_NotACode");
                    break;
                case GogConnectResult.CodeRejected:
                    SetStatus(StatusKind.Error, "GogAccount_CodeRejected");
                    break;
                default:
                    LogConnectFailed(detail);
                    SetStatus(StatusKind.Error, "GogAccount_Failed");
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            SetStatus(StatusKind.Error, "GogAccount_Failed");
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!_dialogs.Confirm(_localizer["GogAccount_DisconnectTitle"], _localizer["GogAccount_DisconnectConfirm"]))
        {
            return;
        }

        try
        {
            await _account.DisconnectAsync(CancellationToken.None);
            IsConnected = false;
            AccountText = null;
            SetStatus(StatusKind.Success, "GogAccount_Disconnected");
            await _library.RescanCommand.ExecuteAsync(null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            SetStatus(StatusKind.Error, "GogAccount_Failed");
        }
    }

    private async Task ShowAccountAsync(CancellationToken ct)
    {
        var info = await _account.GetInfoAsync(ct);
        IsConnected = info is not null;
        AccountText = info is null
            ? null
            : string.Format(CultureInfo.CurrentCulture, _localizer["GogAccount_Account"], info.UserName, info.ConnectedSince.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));
    }

    private void SetStatus(StatusKind kind, string? key)
    {
        StatusKind = kind;
        Status = key is null ? null : _localizer[key];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "GOG sign-in step failed")]
    private partial void LogFailed(Exception exception);

    // GOG's error code only: never the pasted address or tokens.
    [LoggerMessage(Level = LogLevel.Warning, Message = "GOG sign-in was refused: {Detail}")]
    private partial void LogConnectFailed(string? detail);
}
