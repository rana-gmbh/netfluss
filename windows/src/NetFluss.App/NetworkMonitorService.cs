// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using NetFluss.Core;
using NetFluss.Native;

namespace NetFluss.App;

/// <summary>
/// Windows counterpart of the macOS <c>NetworkMonitor</c>: one timer on the UI dispatcher
/// drives one <see cref="InterfaceSampler"/> pass and republishes adapters and totals.
///
/// Deliberately a single timer, as on macOS. The energy lesson from the Mac side applies
/// verbatim — anything expensive (per-process ETW aggregation, reverse DNS) must be gated
/// on a window actually being open, never bolted onto this tick.
/// </summary>
public sealed class NetworkMonitorService : INotifyPropertyChanged, IDisposable
{
    private readonly InterfaceSampler _sampler = new();
    private readonly DispatcherTimer _timer;

    private RateTotals _totals = RateTotals.Zero;
    private AdapterVisibilityOptions _visibility = new();
    private bool _excludeTunnelAdapters;
    private bool _totalsFromVisibleOnly;

    public NetworkMonitorService(TimeSpan interval)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        _timer.Tick += (_, _) => Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The adapters that pass the visibility filter, in traffic order.</summary>
    public ObservableCollection<AdapterStatus> Adapters { get; } = [];

    /// <summary>
    /// Every adapter the machine reports, filter or no filter.
    ///
    /// <para>Preferences lists from this rather than from <see cref="Adapters"/>: hiding an
    /// adapter removes it from the filtered set, so a checklist built on that would drop the
    /// row the moment it was unticked and leave no way to ever tick it back.</para>
    /// </summary>
    public ObservableCollection<AdapterStatus> AllAdapters { get; } = [];

    public RateTotals Totals
    {
        get => _totals;
        private set
        {
            if (_totals == value)
            {
                return;
            }

            _totals = value;
            OnPropertyChanged();
        }
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool ExcludeTunnelAdapters
    {
        get => _excludeTunnelAdapters;
        set => _excludeTunnelAdapters = value;
    }

    public bool TotalsFromVisibleAdaptersOnly
    {
        get => _totalsFromVisibleOnly;
        set => _totalsFromVisibleOnly = value;
    }

    /// <summary>User labels by interface GUID; empty means use the Windows connection name.</summary>
    public IReadOnlyDictionary<string, string> AdapterNames { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>User ordering by interface GUID; empty means busiest first.</summary>
    public IReadOnlyList<string> AdapterOrder { get; set; } = [];

    public AdapterVisibilityOptions Visibility
    {
        get => _visibility;
        set => _visibility = value;
    }

    public void Start()
    {
        // Prime the counters so the first visible tick already has a delta to work from.
        Refresh();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Refresh()
    {
        var sampled = _sampler.Sample();

        Totals = AdapterTotalsFilter.Totals(
            sampled,
            _totalsFromVisibleOnly,
            _excludeTunnelAdapters,
            _visibility);

        // Custom names are applied to the user-facing list only. AllAdapters below stays as
        // Windows reports it, because the rename field in Preferences has to be able to show
        // what an adapter is called *without* a custom name — otherwise committing an
        // untouched field would pin the current label and quietly stop the adapter from ever
        // following its Windows name again.
        var visible = AdapterTotalsFilter.InUserOrder(
            AdapterTotalsFilter.WithCustomNames(
                AdapterTotalsFilter.VisibleAdapters(sampled, _visibility),
                AdapterNames),
            AdapterOrder);

        // Rebuild in place: replacing the collection would drop the popover's bindings.
        Adapters.Clear();
        foreach (var adapter in visible)
        {
            Adapters.Add(adapter);
        }

        // Loopback and the WFP/QoS filter pseudo-interfaces are excluded even here: they
        // mirror the adapter they sit on, so offering them in a checklist would be offering
        // the user four copies of their Ethernet card to choose between.
        //
        // Ranked adapters first in the user's order, then the rest alphabetically —
        // deliberately *not* by traffic like the popover. This list is the one being
        // rearranged, and rows that resort themselves every second would slide out from
        // under the pointer mid-drag.
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < AdapterOrder.Count; i++)
        {
            rank.TryAdd(AdapterOrder[i], i);
        }

        AllAdapters.Clear();
        foreach (var adapter in sampled
                     .Where(adapter => !adapter.IsNonInternet)
                     .OrderBy(adapter => rank.TryGetValue(adapter.Id, out var position) ? position : int.MaxValue)
                     .ThenBy(adapter => adapter.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            AllAdapters.Add(adapter);
        }

        OnPropertyChanged(nameof(AllAdapters));
    }

    public void Dispose() => _timer.Stop();

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
