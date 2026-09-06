// Copyright (C) 2026 Rana GmbH
//
// This file is part of NetFluss.
//
// NetFluss is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// NetFluss is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with NetFluss. If not, see <https://www.gnu.org/licenses/>.

using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace NetFluss.Core;

/// <summary>Where the meter puts its numbers. Mirrors the macOS "Menu bar style" choice.</summary>
public enum MeterStyle
{
    TwoLine,
    DownloadOnly,
    UploadOnly,
    Icon,
}

/// <summary>
/// Which surface carries the live meter.
///
/// <para>macOS has one answer — a variable-width <c>NSStatusItem</c> — and Windows has
/// none that is both roomy and guaranteed. The notification area is permanent but capped
/// at a 16–32 px square; the taskbar overlay has the room but sits on undocumented
/// geometry. So the choice is the user's, and the app degrades rather than disappears.</para>
/// </summary>
public enum MeterSurface
{
    /// <summary>
    /// A window positioned over the taskbar beside the tray. Room for the full
    /// "↓ 4.72 MB/s ↑ 834 KB/s" line, and the closest thing to the macOS menu bar.
    /// Falls back to <see cref="Tray"/> on its own if the taskbar cannot be located.
    /// </summary>
    TaskbarOverlay,

    /// <summary>The notification-area icon. Cramped, and it never breaks.</summary>
    Tray,
}

/// <summary>How the roomier surfaces lay the rates out. Ports the macOS menu bar styles.</summary>
public enum ReadoutStyle
{
    /// <summary>One line: "↓ 4.72 MB/s   ↑ 834 KB/s". The macOS <c>unified</c> style.</summary>
    Unified,

    /// <summary>Two half-height lines, upload above download. The macOS <c>rates</c> stack.</summary>
    Stacked,

    /// <summary>Combined throughput only, for the narrowest placements.</summary>
    Total,
}

/// <summary>
/// Everything Preferences can change, and the single source of truth for it.
///
/// <para><b>Why a JSON file and not the registry.</b> The macOS app keeps ordered lists in
/// <c>UserDefaults</c> — adapter order, hidden adapters, custom DNS presets. The registry
/// has no ordered-collection story worth using, so a JSON document under
/// <c>%LOCALAPPDATA%</c> is both the closer analogue and the thing a user can back up,
/// diff, or delete to reset. It also keeps a portable no-install build possible, which the
/// port plan treats as a requirement.</para>
///
/// <para>Defaults here are the registration list: they must match what the macOS
/// <c>AppState</c> registers, because a fresh install on either platform should behave the
/// same way.</para>
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private double _refreshIntervalSeconds = 1;
    private bool _useBits;
    private MeterStyle _meterStyle = MeterStyle.TwoLine;
    private bool _showArrows;
    private bool _enforceContrast = true;
    private string _downloadAccent = "system";
    private string _uploadAccent = "system";
    private string _downloadCustomHex = "0078D4";
    private string _uploadCustomHex = "2EA043";
    private string _themeId = "system";
    private AppLanguage _language = AppLanguage.System;
    private bool _launchAtLogin;
    private bool _excludeTunnelAdapters;
    private bool _totalsFromVisibleAdaptersOnly;
    private MeterSurface _meterSurface = MeterSurface.TaskbarOverlay;
    private ReadoutStyle _readoutStyle = ReadoutStyle.Unified;
    private double _readoutFontSize = 11;
    private bool _showFloatingWidget;
    private double? _floatingWidgetLeft;
    private double? _floatingWidgetTop;
    private string _trayIconGlyph = "netfluss";
    private bool _hideTrayIcon;
    private bool _showInactiveAdapters;
    private bool _showOtherAdapters = true;
    private double _popoverWidth = 320;
    private double _popoverHeight = 460;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Tick interval. The macOS app offers 1–5 s and so does this; anything faster buys no
    /// visible precision and costs battery on a machine that is otherwise idle.
    /// </summary>
    public double RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set => Set(ref _refreshIntervalSeconds, Math.Clamp(value, 1, 5));
    }

    /// <summary>Bits per second rather than bytes. Off by default, as on macOS.</summary>
    public bool UseBits
    {
        get => _useBits;
        set => Set(ref _useBits, value);
    }

    public MeterStyle MeterStyle
    {
        get => _meterStyle;
        set => Set(ref _meterStyle, value);
    }

    /// <summary>
    /// Off by default: at 16–20 px the arrow eats width the digits need, and the row colour
    /// already carries the direction. See the Phase 0 verdict in windows/README.md.
    /// </summary>
    public bool ShowArrows
    {
        get => _showArrows;
        set => Set(ref _showArrows, value);
    }

    /// <summary>
    /// Lift both rate colours to WCAG AA against the taskbar they are drawn on. On by
    /// default — a user picking a colour is asking for that colour, not for an unreadable
    /// tray icon, and the correction preserves the hue.
    /// </summary>
    public bool EnforceContrast
    {
        get => _enforceContrast;
        set => Set(ref _enforceContrast, value);
    }

    /// <summary>Named entry in <see cref="AccentPalette"/>, "system", or "custom".</summary>
    public string DownloadAccent
    {
        get => _downloadAccent;
        set => Set(ref _downloadAccent, value);
    }

    public string UploadAccent
    {
        get => _uploadAccent;
        set => Set(ref _uploadAccent, value);
    }

    public string DownloadCustomHex
    {
        get => _downloadCustomHex;
        set => Set(ref _downloadCustomHex, value);
    }

    public string UploadCustomHex
    {
        get => _uploadCustomHex;
        set => Set(ref _uploadCustomHex, value);
    }

    /// <summary>Id from <see cref="AppTheme.All"/>.</summary>
    public string ThemeId
    {
        get => _themeId;
        set => Set(ref _themeId, value);
    }

    public AppLanguage Language
    {
        get => _language;
        set => Set(ref _language, value);
    }

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set => Set(ref _launchAtLogin, value);
    }

    public bool ExcludeTunnelAdapters
    {
        get => _excludeTunnelAdapters;
        set => Set(ref _excludeTunnelAdapters, value);
    }

    public bool TotalsFromVisibleAdaptersOnly
    {
        get => _totalsFromVisibleAdaptersOnly;
        set => Set(ref _totalsFromVisibleAdaptersOnly, value);
    }

    /// <summary>
    /// Which surface shows the meter. Defaults to the overlay because it is the only one
    /// with room for a real rate line; <c>App</c> falls back to the tray by itself if the
    /// taskbar cannot be anchored to, so this preference never leaves the user with nothing.
    /// </summary>
    public MeterSurface MeterSurface
    {
        get => _meterSurface;
        set => Set(ref _meterSurface, value);
    }

    /// <summary>Layout used by the overlay and the floating widget, not by the tray.</summary>
    public ReadoutStyle ReadoutStyle
    {
        get => _readoutStyle;
        set => Set(ref _readoutStyle, value);
    }

    /// <summary>Point size for the roomy surfaces. macOS clamps to 8–16 and so does this.</summary>
    public double ReadoutFontSize
    {
        get => _readoutFontSize;
        set => Set(ref _readoutFontSize, Math.Clamp(value, 8, 16));
    }

    /// <summary>
    /// The always-on-top desktop panel. Independent of <see cref="MeterSurface"/> — it is an
    /// addition, not an alternative, and the macOS "Pin" feature it ports behaves the same way.
    /// </summary>
    public bool ShowFloatingWidget
    {
        get => _showFloatingWidget;
        set => Set(ref _showFloatingWidget, value);
    }

    /// <summary>
    /// Last position of the floating widget, or null until it has been placed once.
    ///
    /// <para>Nullable rather than NaN: System.Text.Json refuses to write NaN at all, so the
    /// sentinel turned every settings save into an ArgumentException that Save could not
    /// catch. Absence is what is actually being modelled here anyway.</para>
    /// </summary>
    public double? FloatingWidgetLeft
    {
        get => _floatingWidgetLeft;
        set => Set(ref _floatingWidgetLeft, value);
    }

    public double? FloatingWidgetTop
    {
        get => _floatingWidgetTop;
        set => Set(ref _floatingWidgetTop, value);
    }

    /// <summary>
    /// Hide the notification-area icon while another surface is carrying the meter.
    ///
    /// <para><b>Off by default, and that is a deliberate safety choice.</b> The tray icon is
    /// where Windows users look for a background app's menu, and for a while this was
    /// hidden automatically whenever the taskbar overlay anchored — which left a user who
    /// had also turned off the floating widget with no discoverable way into Preferences and
    /// no way to quit at all, short of Task Manager.</para>
    ///
    /// <para>Turning it on is fine, and the overlay's own right-click menu still works, but
    /// it is a choice the user makes knowingly rather than a side effect of picking a
    /// placement. While it is on and the overlay is carrying the numbers, the tray icon
    /// drops to a static glyph so the rates are not shown twice.</para>
    /// </summary>
    public bool HideTrayIcon
    {
        get => _hideTrayIcon;
        set => Set(ref _hideTrayIcon, value);
    }

    /// <summary>Id from the tray glyph library, used by <see cref="MeterStyle.Icon"/>.</summary>
    public string TrayIconGlyph
    {
        get => _trayIconGlyph;
        set => Set(ref _trayIconGlyph, value);
    }

    /// <summary>BSD-equivalent interface ids in user order, mirroring macOS "adapterOrder".</summary>
    public List<string> AdapterOrder { get; set; } = [];

    /// <summary>Interface id → user label, mirroring macOS "adapterCustomNames".</summary>
    public Dictionary<string, string> AdapterCustomNames { get; set; } = [];

    /// <summary>Interface ids the user has hidden from the popover.</summary>
    public List<string> HiddenAdapters { get; set; } = [];

    /// <summary>Resolved download colour, or <paramref name="fallback"/> for "system".</summary>
    /// <summary>
    /// Show adapters that are down. Off by default: a laptop has a dozen disconnected
    /// virtual and tunnel interfaces and listing them all buries the one that matters.
    /// </summary>
    public bool ShowInactiveAdapters
    {
        get => _showInactiveAdapters;
        set => Set(ref _showInactiveAdapters, value);
    }

    /// <summary>Show interfaces NDIS could not classify. On by default, as on macOS.</summary>
    public bool ShowOtherAdapters
    {
        get => _showOtherAdapters;
        set => Set(ref _showOtherAdapters, value);
    }

    /// <summary>Remembered popover size. The user resizes it; it stays resized.</summary>
    public double PopoverWidth
    {
        get => _popoverWidth;
        set => Set(ref _popoverWidth, Math.Clamp(value, 280, 900));
    }

    public double PopoverHeight
    {
        get => _popoverHeight;
        set => Set(ref _popoverHeight, Math.Clamp(value, 220, 1200));
    }

    /// <summary>
    /// The visibility rules assembled from the individual preferences.
    ///
    /// <para>Built here rather than at the call site so the popover, the totals and the
    /// Preferences list cannot end up disagreeing about which adapters count — the whole
    /// reason <see cref="AdapterVisibilityOptions"/> bundles them.</para>
    /// </summary>
    public AdapterVisibilityOptions VisibilityOptions() => new()
    {
        Hidden = HiddenAdapters.ToHashSet(StringComparer.OrdinalIgnoreCase),
        ShowOtherAdapters = ShowOtherAdapters,
        ShowInactive = ShowInactiveAdapters,
    };

    /// <summary>Whether <paramref name="adapterId"/> is currently hidden by the user.</summary>
    public bool IsAdapterHidden(string adapterId)
        => HiddenAdapters.Contains(adapterId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Shows or hides one adapter. Mutates the list in place and republishes it, because
    /// <see cref="Set{T}"/> compares by reference for a List and would not fire otherwise —
    /// which would leave the setting saved but unapplied until something else changed.
    /// </summary>
    public void SetAdapterHidden(string adapterId, bool hidden)
    {
        var already = IsAdapterHidden(adapterId);
        if (already == hidden)
        {
            return;
        }

        var updated = HiddenAdapters
            .Where(id => !string.Equals(id, adapterId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (hidden)
        {
            updated.Add(adapterId);
        }

        HiddenAdapters = updated;
        OnPropertyChanged(nameof(HiddenAdapters));
    }

    /// <summary>
    /// Renames one adapter, or clears the label back to the Windows connection name when
    /// given blank. Stored per interface GUID, so it survives the adapter being renamed in
    /// Windows or moving between ports.
    /// </summary>
    public void SetAdapterName(string adapterId, string? name)
    {
        var trimmed = name?.Trim();
        var updated = new Dictionary<string, string>(AdapterCustomNames, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(trimmed))
        {
            if (!updated.Remove(adapterId))
            {
                return;
            }
        }
        else
        {
            if (updated.TryGetValue(adapterId, out var existing) && existing == trimmed)
            {
                return;
            }

            updated[adapterId] = trimmed;
        }

        // Replaced rather than mutated, for the same reason SetAdapterHidden replaces its
        // list: Set<T> compares a Dictionary by reference and an in-place edit would never
        // raise a change, leaving the rename applied in memory and absent from disk.
        AdapterCustomNames = updated;
        OnPropertyChanged(nameof(AdapterCustomNames));
    }

    /// <summary>The user's label for an adapter, or <paramref name="fallback"/> if unnamed.</summary>
    public string AdapterDisplayName(string adapterId, string fallback)
        => AdapterCustomNames.TryGetValue(adapterId, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom
            : fallback;

    /// <summary>
    /// Moves one adapter to a new position, recording the full order as it stands.
    ///
    /// <para>The whole visible sequence is stored rather than just the moved id, because a
    /// position is only meaningful relative to its neighbours — saving "the VPN is third"
    /// says nothing once the list it was third in has changed.</para>
    /// </summary>
    public void MoveAdapter(IReadOnlyList<string> currentOrder, string adapterId, int newIndex)
    {
        var reordered = currentOrder
            .Where(id => !string.Equals(id, adapterId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        reordered.Insert(Math.Clamp(newIndex, 0, reordered.Count), adapterId);

        if (reordered.SequenceEqual(AdapterOrder, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        AdapterOrder = reordered;
        OnPropertyChanged(nameof(AdapterOrder));
    }

    /// <summary>Forgets the user's ordering, returning every surface to busiest-first.</summary>
    public void ResetAdapterOrder()
    {
        if (AdapterOrder.Count == 0)
        {
            return;
        }

        AdapterOrder = [];
        OnPropertyChanged(nameof(AdapterOrder));
    }

    /// <summary>The selected theme, or <see cref="AppTheme.System"/> for an unknown id.</summary>
    public AppTheme Theme => AppTheme.Named(ThemeId);

    public ThemeColor ResolveDownloadColor(ThemeColor fallback)
        => AccentPalette.Resolve(DownloadAccent, DownloadCustomHex, fallback) ?? fallback;

    public ThemeColor ResolveUploadColor(ThemeColor fallback)
        => AccentPalette.Resolve(UploadAccent, UploadCustomHex, fallback) ?? fallback;

    /// <summary>
    /// The rate colours every surface should draw with, given what Windows' own light or dark
    /// shell would use.
    ///
    /// <para><b>Precedence, which is the whole point of this method.</b> A theme supplies the
    /// base pair; an accent set to anything other than "Automatic" overrides it for that row.
    /// So picking Dracula recolours both rows, and a user who has separately pinned upload to
    /// orange keeps their orange. Matching macOS, where the theme sets the palette and the
    /// per-element colour pickers win over it.</para>
    ///
    /// <para>This exists because the two halves used to be wired independently: the theme
    /// picker wrote <see cref="ThemeId"/> and nothing ever read it, so choosing Dracula
    /// changed a line in the settings file and nothing else.</para>
    /// </summary>
    public (ThemeColor Download, ThemeColor Upload) ResolveRateColors(
        ThemeColor systemDownload,
        ThemeColor systemUpload)
    {
        var theme = Theme;

        var baseDownload = theme.IsExplicit ? theme.DownloadColor : systemDownload;
        var baseUpload = theme.IsExplicit ? theme.UploadColor : systemUpload;

        return (ResolveDownloadColor(baseDownload), ResolveUploadColor(baseUpload));
    }

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
