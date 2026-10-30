// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Providers.Steam;

namespace Lumen.App.ViewModels;

/// <summary>
/// The bottom of the sidebar (M10 template): the Steam account read from Steam's own files, and a small CPU/RAM
/// monitor read locally. The monitor only ticks while the window is on screen (<see cref="SetActive"/>).
/// </summary>
public sealed partial class SidebarFooterViewModel : ObservableObject
{
    /// <summary>How often the monitor reads the system.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private const double BytesPerGb = 1024d * 1024 * 1024;

    private readonly SteamProvider _steam;
    private readonly ISystemUsage _usage;
    private readonly ILocalizer _loc;
    private readonly DispatcherTimer _timer;
    private CpuTimes? _lastCpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfile))]
    private SteamProfile? _profile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuText))]
    [NotifyPropertyChangedFor(nameof(HasCpu))]
    private double? _cpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamText))]
    [NotifyPropertyChangedFor(nameof(RamFraction))]
    [NotifyPropertyChangedFor(nameof(HasRam))]
    private MemoryUsage? _ram;

    public SidebarFooterViewModel(SteamProvider steam, ISystemUsage usage, LibraryService library, ILocalizer loc)
    {
        ArgumentNullException.ThrowIfNull(library);
        _steam = steam;
        _usage = usage;
        _loc = loc;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += (_, _) => Sample();
        var ui = SynchronizationContext.Current;

        // A scan may follow a new sign-in or a changed SteamID: read the profile again (posted to the UI thread).
        library.FullScanCompleted += (_, _) =>
        {
            if (ui is null)
            {
                return;
            }

            ui.Post(_ => _ = RefreshProfileAsync(), null);
        };
        _loc.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(RamText));
        };
    }

    public bool HasProfile => Profile is not null;

    public bool HasCpu => Cpu is not null;

    public bool HasRam => Ram is not null;

    /// <summary>Busy share, 0 to 1 (bar length).</summary>
    public double CpuFraction => Cpu ?? 0;

    public string CpuText => Cpu is { } cpu ? string.Format(_loc.Culture, _loc["Footer_CpuValue"], Math.Round(cpu * 100)) : string.Empty;

    public double RamFraction => Ram?.UsedFraction ?? 0;

    // Numbers follow Lumen's language (not Windows' regional format), so switching to English shows "7.2".
    public string RamText => Ram is { } ram
        ? string.Format(_loc.Culture, _loc["Footer_RamValue"], ram.UsedBytes / BytesPerGb, Math.Round(ram.TotalBytes / BytesPerGb))
        : string.Empty;

    public Task InitializeAsync() => RefreshProfileAsync();

    /// <summary>Starts or stops the monitor (the window shown vs hidden or minimized).</summary>
    public void SetActive(bool active)
    {
        if (active == _timer.IsEnabled)
        {
            return;
        }

        if (active)
        {
            _lastCpu = null; // the first reading after a pause only sets the baseline
            Sample();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    partial void OnCpuChanged(double? value) => OnPropertyChanged(nameof(CpuFraction));

    private void Sample()
    {
        var now = _usage.ReadCpuTimes();
        if (now is { } current && _lastCpu is { } previous && CpuLoad.Between(previous, current) is { } load)
        {
            Cpu = load;
        }

        _lastCpu = now;
        Ram = _usage.ReadMemory();
    }

    private async Task RefreshProfileAsync() => Profile = await _steam.ReadProfileAsync(CancellationToken.None).ConfigureAwait(true);
}
