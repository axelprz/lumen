// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
using Lumen.Providers.Steam;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

public enum StatusKind
{
    None,
    Working,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Settings → Steam: Web API key (DPAPI-encrypted, never shown back) and SteamID64. Saving tests the pair
/// against Steam first; a key Steam rejects is never stored.
/// </summary>
public sealed partial class SteamSettingsViewModel : ObservableObject
{
    public const string ApiKeyPage = "https://steamcommunity.com/dev/apikey";

    private readonly ISecretStore _secrets;
    private readonly ILocalSettings _settings;
    private readonly SteamProvider _steam;
    private readonly LibraryViewModel _library;
    private readonly ILauncher _launcher;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SteamSettingsViewModel> _logger;

    [ObservableProperty]
    private string _steamIdInput = string.Empty;

    [ObservableProperty]
    private bool _isKeySaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private StatusKind _statusKind;

    public SteamSettingsViewModel(
        ISecretStore secrets,
        ILocalSettings settings,
        SteamProvider steam,
        LibraryViewModel library,
        ILauncher launcher,
        ILocalizer localizer,
        ILogger<SteamSettingsViewModel> logger)
    {
        _secrets = secrets;
        _settings = settings;
        _steam = steam;
        _library = library;
        _launcher = launcher;
        _localizer = localizer;
        _logger = logger;
    }

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public async Task InitializeAsync(CancellationToken ct)
    {
        IsKeySaved = await _secrets.GetAsync(SecretNames.SteamWebApiKey, ct) is not null;
        SteamIdInput = await _settings.GetAsync(LocalSettingKeys.SteamId64, ct) ?? string.Empty;
    }

    /// <param name="apiKeyInput">What the user typed in the password box; empty keeps the saved key.</param>
    [RelayCommand]
    private async Task SaveAndTestAsync(string? apiKeyInput)
    {
        var steamId = SteamWebApi.ExtractSteamId64(SteamIdInput);
        if (steamId is null)
        {
            SetStatus(StatusKind.Error, "Settings_Steam_InvalidSteamId");
            return;
        }

        var typedKey = apiKeyInput?.Trim();
        if (!string.IsNullOrEmpty(typedKey) && !ApiKeyPattern().IsMatch(typedKey))
        {
            SetStatus(StatusKind.Error, "Settings_Steam_InvalidKeyFormat");
            return;
        }

        var key = string.IsNullOrEmpty(typedKey)
            ? await _secrets.GetAsync(SecretNames.SteamWebApiKey, CancellationToken.None)
            : typedKey;
        if (key is null)
        {
            SetStatus(StatusKind.Error, "Settings_Steam_MissingKey");
            return;
        }

        SetStatus(StatusKind.Working, "Settings_Steam_Testing");
        var result = await _steam.TestConnectionAsync(key, steamId, CancellationToken.None);
        switch (result.Error)
        {
            case SteamApiError.None or SteamApiError.PrivateProfile:
                await _secrets.SetAsync(SecretNames.SteamWebApiKey, key, CancellationToken.None);
                await _settings.SetAsync(LocalSettingKeys.SteamId64, steamId, CancellationToken.None);
                IsKeySaved = true;
                SteamIdInput = steamId;
                if (result.Error == SteamApiError.None)
                {
                    SetStatus(StatusKind.Success, "Settings_Steam_Connected", result.Games.Count);
                }
                else
                {
                    SetStatus(StatusKind.Warning, "Settings_Steam_Private");
                }

                LogSaved(result.Games.Count);
                await _library.RescanCommand.ExecuteAsync(null);
                break;
            case SteamApiError.InvalidKey:
                SetStatus(StatusKind.Error, "Settings_Steam_InvalidKey");
                break;
            default:
                LogTestFailed(result.Error, result.Detail);
                SetStatus(StatusKind.Error, "Settings_Steam_Unreachable");
                break;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        await _secrets.RemoveAsync(SecretNames.SteamWebApiKey, CancellationToken.None);
        await _settings.RemoveAsync(LocalSettingKeys.SteamId64, CancellationToken.None);
        IsKeySaved = false;
        SteamIdInput = string.Empty;
        SetStatus(StatusKind.Success, "Settings_Steam_Removed");
        await _library.RescanCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private Task OpenApiKeyPageAsync() =>
        _launcher.StartAsync(new LaunchSpec(LaunchKind.Uri, ApiKeyPage), CancellationToken.None);

    private void SetStatus(StatusKind kind, string key, params object[] args)
    {
        StatusKind = kind;
        Status = args.Length == 0 ? _localizer[key] : string.Format(CultureInfo.CurrentCulture, _localizer[key], args);
    }

    // Steam Web API keys are 32 hexadecimal characters.
    [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
    private static partial Regex ApiKeyPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam account details saved; {Count} owned games reported")]
    private partial void LogSaved(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam connection test failed: {Error} {Detail}")]
    private partial void LogTestFailed(SteamApiError error, string? detail);
}
