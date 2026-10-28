// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Resources;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Localization;

namespace Lumen.App.Localization;

/// <summary>
/// Reads <c>Resources/Strings.resx</c> (English) and <c>Strings.es.resx</c> (Spanish).
/// XAML binds to the indexer through <see cref="LocExtension"/>, so changing <see cref="Culture"/>
/// updates every visible string without restarting.
/// </summary>
public sealed class LocalizationService : ObservableObject, ILocalizer
{
    private static readonly ResourceManager _resources =
        new("Lumen.App.Resources.Strings", typeof(LocalizationService).Assembly);

    /// <summary>
    /// Windows' display language, captured before Lumen changes the thread's UI culture, so "follow Windows" keeps
    /// meaning Windows after the user tried another language.
    /// </summary>
    public static CultureInfo SystemUiCulture { get; } = CultureInfo.CurrentUICulture;

    private CultureInfo _culture = CultureInfo.GetCultureInfo(LanguageResolver.FallbackLanguage);

    private LocalizationService()
    {
    }

    /// <summary>Single instance shared by XAML markup extensions and the DI container.</summary>
    public static LocalizationService Instance { get; } = new();

    public event EventHandler? CultureChanged;

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Equals(_culture, value))
            {
                return;
            }

            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            OnPropertyChanged();
            OnPropertyChanged(Binding.IndexerName);
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string this[string key] =>
        _resources.GetString(key, _culture) is { Length: > 0 } value ? value : $"[{key}]";
}
