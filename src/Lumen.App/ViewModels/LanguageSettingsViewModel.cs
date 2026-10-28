// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.App.Localization;
using Lumen.Core.Localization;
using Lumen.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>One choice in the language list. Language names are written in their own language.</summary>
public sealed record LanguageOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The UI language (PLAN-v1 §4.3/§4.7): follow Windows, English or Spanish. Applied immediately and remembered in
/// <c>settings_local</c> (<see cref="LocalSettingKeys.Language"/>). Used by Settings and the first-run wizard.
/// </summary>
public sealed partial class LanguageSettingsViewModel : ObservableObject
{
    private readonly LocalizationService _localizer;
    private readonly ILocalSettings _settings;
    private readonly ILogger<LanguageSettingsViewModel> _logger;
    private readonly CultureInfo _osCulture = LocalizationService.SystemUiCulture;
    private bool _loading;

    [ObservableProperty]
    private LanguageOption? _selected;

    public LanguageSettingsViewModel(LocalizationService localizer, ILocalSettings settings, ILogger<LanguageSettingsViewModel> logger)
    {
        _localizer = localizer;
        _settings = settings;
        _logger = logger;
        Options = BuildOptions();
        _localizer.CultureChanged += (_, _) => RefreshLabels();
    }

    public IReadOnlyList<LanguageOption> Options { get; private set; }

    /// <summary>Reads the saved choice and applies it. Call before the main window is shown.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        string? saved = null;
        try
        {
            saved = await _settings.GetAsync(LocalSettingKeys.Language, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
        }

        var value = saved is not null && LanguageResolver.IsSupported(saved) ? saved.ToLowerInvariant() : LanguageResolver.SystemPreference;
        _loading = true;
        Selected = Options.First(o => o.Value == value);
        _loading = false;
        _localizer.Culture = LanguageResolver.Resolve(value, _osCulture);
    }

    partial void OnSelectedChanged(LanguageOption? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        _localizer.Culture = LanguageResolver.Resolve(value.Value, _osCulture);
        _ = SaveAsync(value.Value);
    }

    private async Task SaveAsync(string value)
    {
        try
        {
            await _settings.SetAsync(LocalSettingKeys.Language, value, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex); // the language still applies for this session
        }
    }

    // "Windows language" is translated; "English" and "Español" are always written in their own language.
    private List<LanguageOption> BuildOptions() =>
    [
        new(LanguageResolver.SystemPreference, _localizer["Language_System"]),
        new("es", "Español"),
        new("en", "English"),
    ];

    private void RefreshLabels()
    {
        var current = Selected?.Value;
        _loading = true;
        Options = BuildOptions();
        OnPropertyChanged(nameof(Options));
        Selected = Options.FirstOrDefault(o => o.Value == current) ?? Options[0];
        _loading = false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the saved language; following Windows")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the language choice")]
    private partial void LogSaveFailed(Exception exception);
}
