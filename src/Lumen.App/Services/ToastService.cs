// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.App.ViewModels;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Error,
}

/// <summary>One short notice in the window's bottom-right corner. The text is written on read, so it follows the language.</summary>
public sealed class ToastViewModel : ObservableObject
{
    private readonly Func<string> _text;

    public ToastViewModel(Func<string> text, ToastKind kind)
    {
        _text = text;
        Kind = kind;
    }

    public ToastKind Kind { get; }

    public string Text => _text();

    /// <summary>Segoe Fluent glyph: check for success, warning for errors, info otherwise.</summary>
    public string Glyph => Kind switch
    {
        ToastKind.Success => "",
        ToastKind.Error => "",
        _ => "",
    };

    internal void Refresh() => OnPropertyChanged(nameof(Text));
}

/// <summary>
/// Short notices shown over the main window (M10 template): a game starting, a routine run from a shortcut finishing.
/// At most three at a time, each gone after a few seconds. Calls from background threads are posted to the UI thread.
/// </summary>
public sealed class ToastService
{
    /// <summary>How long a notice stays.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(3.5);

    private const int MaxVisible = 3;

    private readonly Dispatcher _dispatcher;
    private readonly ILocalizer _loc;

    public ToastService(ILocalizer loc, LaunchService launcher, RoutinesViewModel routines)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(routines);
        _loc = loc;
        _dispatcher = System.Windows.Application.Current.Dispatcher;
        _loc.CultureChanged += (_, _) =>
        {
            foreach (var toast in Items)
            {
                toast.Refresh();
            }
        };

        launcher.Launched += (_, e) => OnLaunched(e);
        routines.FinishedOutsidePage += (_, e) => Show(() => string.Format(CultureInfo.CurrentCulture, _loc["Toast_RoutineDone"], e.Name, e.Result), ToastKind.Success);
    }

    public ObservableCollection<ToastViewModel> Items { get; } = [];

    public void Show(Func<string> text, ToastKind kind)
    {
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(() => Show(text, kind));
            return;
        }

        var toast = new ToastViewModel(text, kind);
        Items.Add(toast);
        while (Items.Count > MaxVisible)
        {
            Items.RemoveAt(0);
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Items.Remove(toast);
        };
        timer.Start();
    }

    private void OnLaunched(LaunchEventArgs e)
    {
        var title = e.Entry.Title;
        if (e.Outcome == LaunchOutcome.Failed)
        {
            Show(() => string.Format(CultureInfo.CurrentCulture, _loc["Library_LaunchFailed"], title), ToastKind.Error);
        }
        else if (e.Outcome == LaunchOutcome.Started)
        {
            var key = e.Installing ? "Toast_Installing" : "Toast_Launching";
            Show(() => string.Format(CultureInfo.CurrentCulture, _loc[key], title), ToastKind.Info);
        }
    }
}
