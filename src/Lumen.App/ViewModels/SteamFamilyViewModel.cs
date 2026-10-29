// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Localization;
using Lumen.Core.Settings;
using Lumen.Providers.Steam;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>A family member as shown on the card.</summary>
public sealed partial class FamilyMemberOption(string steamId, string name, bool isMember) : ObservableObject
{
    public string SteamId { get; } = steamId;

    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isMember = isMember;

    public override string ToString() => Name;
}

/// <summary>
/// Settings → Steam family, without any sign-in: the user picks family members (from their friend list, or by
/// profile link), and Lumen shows those members' public games with the user's own Web API key. The card says plainly
/// what is read and what isn't.
/// </summary>
public sealed partial class SteamFamilyViewModel : ObservableObject
{
    private readonly SteamFamilyMembers _family;
    private readonly SteamWebApi _api;
    private readonly ISecretStore _secrets;
    private readonly ILocalSettings _settings;
    private readonly LibraryViewModel _library;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SteamFamilyViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsKey))]
    private bool _hasKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPicking))]
    private bool _isPicking;

    [ObservableProperty]
    private string _profileInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private StatusKind _statusKind;

    public SteamFamilyViewModel(
        SteamFamilyMembers family, SteamWebApi api, ISecretStore secrets, ILocalSettings settings,
        LibraryViewModel library, ILocalizer localizer, ILogger<SteamFamilyViewModel> logger)
    {
        _family = family;
        _api = api;
        _secrets = secrets;
        _settings = settings;
        _library = library;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>The chosen members.</summary>
    public ObservableCollection<FamilyMemberOption> Members { get; } = [];

    /// <summary>Friends to choose from (while picking).</summary>
    public ObservableCollection<FamilyMemberOption> Friends { get; } = [];

    public bool NeedsKey => !HasKey;

    public bool IsNotPicking => !IsPicking;

    public bool HasMembers => Members.Count > 0;

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            var (key, own) = await KeyAndIdAsync(ct);
            HasKey = key is not null && own is not null;
            var ids = await _family.GetIdsAsync(ct);
            await ShowMembersAsync(key, ids, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
        }
    }

    [RelayCommand]
    private async Task PickFromFriendsAsync()
    {
        Status = null;
        var (key, own) = await KeyAndIdAsync(CancellationToken.None);
        if (key is null || own is null)
        {
            HasKey = false;
            return;
        }

        SetStatus(StatusKind.Working, "SteamFamily_LoadingFriends");
        var friends = await _api.GetFriendIdsAsync(key, own, CancellationToken.None);
        if (friends.Error != SteamApiError.None)
        {
            SetStatus(StatusKind.Warning, friends.Error == SteamApiError.PrivateProfile ? "SteamFamily_FriendsPrivate" : "SteamFamily_Unreachable");
            return;
        }

        var named = new List<SteamProfile>();
        foreach (var chunk in friends.Profiles.Select(p => p.SteamId).Chunk(100))
        {
            var profiles = await _api.GetProfilesAsync(key, chunk, CancellationToken.None);
            named.AddRange(profiles.Profiles);
        }

        var memberIds = Members.Select(m => m.SteamId).ToHashSet(StringComparer.Ordinal);
        Friends.Clear();
        foreach (var friend in named.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Friends.Add(new FamilyMemberOption(friend.SteamId, friend.Name, memberIds.Contains(friend.SteamId)));
        }

        Status = null;
        IsPicking = true;
        if (Friends.Count == 0)
        {
            SetStatus(StatusKind.None, "SteamFamily_NoFriends");
        }
    }

    [RelayCommand]
    private async Task SavePickAsync()
    {
        var chosen = Friends.Where(f => f.IsMember).Select(f => f.SteamId).ToList();
        if (chosen.Count > SteamFamilyMembers.MaxMembers)
        {
            Status = string.Format(CultureInfo.CurrentCulture, _localizer["SteamFamily_TooMany"], SteamFamilyMembers.MaxMembers);
            StatusKind = StatusKind.Warning;
            return;
        }

        IsPicking = false;
        await SaveAsync(chosen);
    }

    [RelayCommand]
    private void CancelPick()
    {
        IsPicking = false;
        Status = null;
    }

    /// <summary>Adds a member by profile link (<c>/profiles/7656…</c> or <c>/id/name</c>) or SteamID64.</summary>
    [RelayCommand]
    private async Task AddByProfileAsync()
    {
        var (key, _) = await KeyAndIdAsync(CancellationToken.None);
        if (key is null)
        {
            HasKey = false;
            return;
        }

        var id = SteamWebApi.ExtractSteamId64(ProfileInput);
        if (id is null && SteamWebApi.ExtractVanityName(ProfileInput) is { } vanity)
        {
            (_, id) = await _api.ResolveVanityAsync(key, vanity, CancellationToken.None);
        }

        if (id is null)
        {
            SetStatus(StatusKind.Warning, "SteamFamily_ProfileNotFound");
            return;
        }

        if (Members.Count >= SteamFamilyMembers.MaxMembers && Members.All(m => m.SteamId != id))
        {
            Status = string.Format(CultureInfo.CurrentCulture, _localizer["SteamFamily_TooMany"], SteamFamilyMembers.MaxMembers);
            StatusKind = StatusKind.Warning;
            return;
        }

        ProfileInput = string.Empty;
        await SaveAsync(Members.Select(m => m.SteamId).Append(id).ToList());
    }

    [RelayCommand]
    private async Task RemoveAsync(FamilyMemberOption? member)
    {
        if (member is not null)
        {
            await SaveAsync(Members.Where(m => m.SteamId != member.SteamId).Select(m => m.SteamId).ToList());
        }
    }

    private async Task SaveAsync(IReadOnlyList<string> ids)
    {
        try
        {
            await _family.SetIdsAsync(ids, CancellationToken.None);
            var (key, _) = await KeyAndIdAsync(CancellationToken.None);
            await ShowMembersAsync(key, await _family.GetIdsAsync(CancellationToken.None), CancellationToken.None);
            SetStatus(StatusKind.Success, "SteamFamily_Saved");
            await _library.RescanCommand.ExecuteAsync(null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex);
            SetStatus(StatusKind.Error, "SteamFamily_Unreachable");
        }
    }

    private async Task ShowMembersAsync(string? key, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (key is not null && ids.Count > 0)
        {
            foreach (var profile in (await _api.GetProfilesAsync(key, ids.ToList(), ct)).Profiles)
            {
                names[profile.SteamId] = profile.Name;
            }
        }

        Members.Clear();
        foreach (var id in ids)
        {
            Members.Add(new FamilyMemberOption(id, names.GetValueOrDefault(id, id), isMember: true));
        }

        OnPropertyChanged(nameof(HasMembers));
    }

    private async Task<(string? Key, string? OwnId)> KeyAndIdAsync(CancellationToken ct)
    {
        var key = await _secrets.GetAsync(SecretNames.SteamWebApiKey, ct);
        var own = await _settings.GetAsync(LocalSettingKeys.SteamId64, ct);
        return (string.IsNullOrWhiteSpace(key) ? null : key, SteamWebApi.IsValidSteamId64(own) ? own : null);
    }

    private void SetStatus(StatusKind kind, string key)
    {
        StatusKind = kind;
        Status = _localizer[key];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the Steam family members")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the Steam family members")]
    private partial void LogSaveFailed(Exception exception);
}
