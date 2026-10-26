// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
using Lumen.Providers.SteamGridDb;

namespace Lumen.App.ViewModels;

/// <summary>
/// Settings → Artwork: SteamGridDB key (DPAPI, never shown back; tested before saving) and clearing the cache.
/// </summary>
public sealed partial class ArtworkSettingsViewModel(
    ISecretStore secrets,
    SteamGridDbClient steamGridDb,
    ArtworkService artwork,
    IArtworkLookupCache lookups,
    LibraryViewModel library,
    ILauncher launcher,
    ILocalizer localizer) : ObservableObject
{
    public const string ApiKeyPage = "https://www.steamgriddb.com/profile/preferences/api";

    [ObservableProperty]
    private bool _isKeySaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private StatusKind _statusKind;

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public async Task InitializeAsync(CancellationToken ct) =>
        IsKeySaved = await secrets.GetAsync(SecretNames.SteamGridDbApiKey, ct) is not null;

    [RelayCommand]
    private async Task SaveAndTestAsync(string? apiKeyInput)
    {
        var key = apiKeyInput?.Trim();
        if (string.IsNullOrEmpty(key) || !ApiKeyPattern().IsMatch(key))
        {
            SetStatus(StatusKind.Error, "Settings_Sgdb_InvalidKeyFormat");
            return;
        }

        SetStatus(StatusKind.Working, "Settings_Sgdb_Testing");
        var test = await steamGridDb.SearchAsync(key, "Portal", CancellationToken.None);
        switch (test.Error)
        {
            case SgdbError.None or SgdbError.NotFound:
                await secrets.SetAsync(SecretNames.SteamGridDbApiKey, key, CancellationToken.None);
                IsKeySaved = true;

                // Items that had no art may have some now: forget misses and failed lookups, then reload covers.
                artwork.ClearMisses();
                await lookups.ClearAsync(CancellationToken.None);
                library.ReloadArtwork();
                SetStatus(StatusKind.Success, "Settings_Sgdb_Connected");
                break;
            case SgdbError.InvalidKey:
                SetStatus(StatusKind.Error, "Settings_Sgdb_InvalidKey");
                break;
            default:
                SetStatus(StatusKind.Error, "Settings_Sgdb_Unreachable");
                break;
        }
    }

    [RelayCommand]
    private async Task RemoveKeyAsync()
    {
        await secrets.RemoveAsync(SecretNames.SteamGridDbApiKey, CancellationToken.None);
        IsKeySaved = false;
        SetStatus(StatusKind.Success, "Settings_Sgdb_Removed");
    }

    /// <summary>Deletes downloaded artwork (custom covers are kept) and downloads it again as tiles appear.</summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        artwork.ClearCache();
        await lookups.ClearAsync(CancellationToken.None);
        library.ReloadArtwork();
        SetStatus(StatusKind.Success, "Settings_Artwork_CacheCleared");
    }

    [RelayCommand]
    private Task OpenApiKeyPageAsync() =>
        launcher.StartAsync(new LaunchSpec(LaunchKind.Uri, ApiKeyPage), CancellationToken.None);

    private void SetStatus(StatusKind kind, string key)
    {
        StatusKind = kind;
        Status = localizer[key];
    }

    // Loose shape check only (letters/digits, sensible length); the real check is the test call to SteamGridDB.
    [GeneratedRegex("^[A-Za-z0-9]{16,128}$")]
    private static partial Regex ApiKeyPattern();
}
