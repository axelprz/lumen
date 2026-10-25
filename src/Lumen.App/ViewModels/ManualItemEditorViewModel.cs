// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>Add/edit dialog for a manual item. Validation is <see cref="ManualItemValidator"/>, shared with sync later.</summary>
public sealed partial class ManualItemEditorViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly Guid _id;

    [ObservableProperty]
    private bool _isGame = true;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArgumentsEnabled))]
    private string _target = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public ManualItemEditorViewModel(IDialogService dialogs, ILocalizer localizer, ManualItem? existing = null, string? prefillTarget = null)
    {
        _dialogs = dialogs;
        _localizer = localizer;
        _id = existing?.Id ?? Guid.NewGuid();
        IsNew = existing is null;
        if (existing is not null)
        {
            IsGame = existing.Kind == ItemKind.Game;
            Title = existing.Title;
            Target = existing.Path ?? existing.Url ?? string.Empty;
            Arguments = existing.Arguments ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(prefillTarget))
        {
            Target = prefillTarget.Trim();
            Title = ManualItemValidator.SuggestTitle(Target);
        }
    }

    /// <summary>Raised when the dialog should close; true = saved.</summary>
    public event EventHandler<bool>? CloseRequested;

    public bool IsNew { get; }

    public string WindowTitle => _localizer[IsNew ? "Manual_AddTitle" : "Manual_EditTitle"];

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Arguments only apply to .exe targets.</summary>
    public bool ArgumentsEnabled => Target.Trim().Trim('"').EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>The saved item, set when <see cref="CloseRequested"/> fires with true.</summary>
    public ManualItem? Result { get; private set; }

    [RelayCommand]
    private void Browse()
    {
        var file = _dialogs.PickFile(_localizer["Manual_FileFilter"]);
        if (file is null)
        {
            return;
        }

        Target = file;
        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = ManualItemValidator.SuggestTitle(file);
        }
    }

    [RelayCommand]
    private void Save()
    {
        var arguments = ArgumentsEnabled ? Arguments : null;
        var error = ManualItemValidator.Validate(Title, Target, arguments);
        if (error != ManualItemError.None)
        {
            Error = _localizer["Manual_Error_" + error];
            return;
        }

        Result = ManualItemValidator.Create(_id, IsGame ? ItemKind.Game : ItemKind.App, Title, Target, arguments);
        CloseRequested?.Invoke(this, true);
    }
}
