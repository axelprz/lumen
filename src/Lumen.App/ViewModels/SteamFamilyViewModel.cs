// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Localization;
using Lumen.Providers.Steam;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Lumen.App.ViewModels;

public enum SteamFamilyState
{
    Disconnected,

    /// <summary>A QR is on screen, waiting for the phone.</summary>
    Connecting,
    Connected,
}

/// <summary>
/// Settings → Steam family library (PLAN-v1 §4.8). Sign-in by scanning a QR with the Steam mobile app (no password
/// in Lumen); afterwards the card shows the account, since when, and exactly what Lumen can do. Disconnect revokes
/// at Steam and deletes the token here, and says so when Steam couldn't confirm.
/// </summary>
public sealed partial class SteamFamilyViewModel : ObservableObject
{
    private readonly SteamAccount _account;
    private readonly LibraryViewModel _library;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SteamFamilyViewModel> _logger;
    private CancellationTokenSource? _signIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected), nameof(IsConnecting), nameof(IsConnected))]
    private SteamFamilyState _state;

    [ObservableProperty]
    private ImageSource? _qrImage;

    [ObservableProperty]
    private string? _accountText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private StatusKind _statusKind;

    public SteamFamilyViewModel(SteamAccount account, LibraryViewModel library, IDialogService dialogs, ILocalizer localizer, ILogger<SteamFamilyViewModel> logger)
    {
        _account = account;
        _library = library;
        _dialogs = dialogs;
        _localizer = localizer;
        _logger = logger;
        var ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _account.Changed += (_, _) => ui.Post(_ => ShowReconnectIfNeeded(), null);
    }

    /// <summary>Steam rejected the sign-in (scan): say so on the card and offer to connect again.</summary>
    private void ShowReconnectIfNeeded()
    {
        OnPropertyChanged(nameof(ShowsReconnect));
        if (_account.NeedsReconnect && State == SteamFamilyState.Connected)
        {
            SetStatus(StatusKind.Warning, "SteamFamily_NeedsReconnect");
        }
    }

    public bool ShowsReconnect => _account.NeedsReconnect;

    public bool IsDisconnected => State == SteamFamilyState.Disconnected;

    public bool IsConnecting => State == SteamFamilyState.Connecting;

    public bool IsConnected => State == SteamFamilyState.Connected;

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

    /// <summary>Shows a QR and waits (polling) until the phone approves, the code expires, or the user cancels.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        _signIn?.Cancel();
        _signIn = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = _signIn.Token;
        SetStatus(StatusKind.None, null);
        try
        {
            var begin = await _account.StartQrAsync(ct);
            if (begin.Value is not { } session)
            {
                SetStatus(StatusKind.Error, "SteamFamily_Unreachable");
                return;
            }

            QrImage = RenderQr(session.ChallengeUrl);
            State = SteamFamilyState.Connecting;
            SetStatus(StatusKind.Working, "SteamFamily_Waiting");

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(session.Interval, ct);
                var poll = await _account.PollAsync(session, ct);
                if (poll.IsApproved)
                {
                    await OnConnectedAsync(ct);
                    return;
                }

                switch (poll.Error)
                {
                    case SteamAccountError.None when poll.NewChallengeUrl is { } url:
                        QrImage = RenderQr(url); // Steam rotates the code; the old one stops working
                        break;
                    case SteamAccountError.None or SteamAccountError.Network or SteamAccountError.RateLimited:
                        break; // keep waiting
                    case SteamAccountError.SessionExpired:
                        EndConnecting(StatusKind.Warning, "SteamFamily_Expired");
                        return;
                    default:
                        LogPollFailed(poll.Error, poll.Detail);
                        EndConnecting(StatusKind.Error, "SteamFamily_Failed");
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (State == SteamFamilyState.Connecting)
            {
                EndConnecting(StatusKind.Warning, "SteamFamily_Expired");
            }
        }
        catch (Exception ex)
        {
            LogFailed(ex);
            EndConnecting(StatusKind.Error, "SteamFamily_Failed");
        }
    }

    [RelayCommand]
    private void CancelConnect()
    {
        _signIn?.Cancel();
        EndConnecting(StatusKind.None, null);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!_dialogs.Confirm(_localizer["SteamFamily_DisconnectTitle"], _localizer["SteamFamily_DisconnectConfirm"]))
        {
            return;
        }

        try
        {
            var revoked = await _account.DisconnectAsync(CancellationToken.None);
            State = SteamFamilyState.Disconnected;
            AccountText = null;
            SetStatus(revoked ? StatusKind.Success : StatusKind.Warning, revoked ? "SteamFamily_Disconnected" : "SteamFamily_DisconnectedLocally");
            await _library.RescanCommand.ExecuteAsync(null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            SetStatus(StatusKind.Error, "SteamFamily_Failed");
        }
    }

    private async Task OnConnectedAsync(CancellationToken ct)
    {
        QrImage = null;
        await ShowAccountAsync(ct);

        // Say right away what the sign-in gives: shared games, or that the account has no family.
        var family = await _account.GetSharedGamesAsync(ct);
        switch (family.Status)
        {
            case SteamFamilyStatus.Ok:
                SetStatus(StatusKind.Success, "SteamFamily_Connected", family.Games.Count);
                break;
            case SteamFamilyStatus.NotInFamily:
                SetStatus(StatusKind.Warning, "SteamFamily_NoFamily");
                break;
            default:
                SetStatus(StatusKind.Warning, "SteamFamily_ConnectedNoList");
                break;
        }

        await _library.RescanCommand.ExecuteAsync(null);
    }

    private async Task ShowAccountAsync(CancellationToken ct)
    {
        var info = await _account.GetInfoAsync(ct);
        State = info is null ? SteamFamilyState.Disconnected : SteamFamilyState.Connected;
        AccountText = info is null
            ? null
            : string.Format(CultureInfo.CurrentCulture, _localizer["SteamFamily_Account"], info.AccountName, info.ConnectedSince.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));
    }

    private void EndConnecting(StatusKind kind, string? key)
    {
        QrImage = null;
        if (State == SteamFamilyState.Connecting)
        {
            State = SteamFamilyState.Disconnected;
        }

        SetStatus(kind, key);
    }

    private void SetStatus(StatusKind kind, string? key, params object[] args)
    {
        StatusKind = kind;
        Status = key is null ? null : args.Length == 0 ? _localizer[key] : string.Format(CultureInfo.CurrentCulture, _localizer[key], args);
    }

    /// <summary>The challenge URL as a QR image (black on white, with a quiet zone so phones read it on a dark page).</summary>
    private static BitmapImage RenderQr(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam family sign-in step failed")]
    private partial void LogFailed(Exception exception);

    // Error kind and status code only: never URLs or tokens.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam family sign-in was refused: {Error} {Detail}")]
    private partial void LogPollFailed(SteamAccountError error, string? detail);
}
