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

    /// <summary>BSD-equivalent interface ids in user order, mirroring macOS "adapterOrder".</summary>
    public List<string> AdapterOrder { get; set; } = [];

    /// <summary>Interface id → user label, mirroring macOS "adapterCustomNames".</summary>
    public Dictionary<string, string> AdapterCustomNames { get; set; } = [];

    /// <summary>Interface ids the user has hidden from the popover.</summary>
    public List<string> HiddenAdapters { get; set; } = [];

    /// <summary>Resolved download colour, or <paramref name="fallback"/> for "system".</summary>
    public ThemeColor ResolveDownloadColor(ThemeColor fallback)
        => AccentPalette.Resolve(DownloadAccent, DownloadCustomHex, fallback) ?? fallback;

    public ThemeColor ResolveUploadColor(ThemeColor fallback)
        => AccentPalette.Resolve(UploadAccent, UploadCustomHex, fallback) ?? fallback;

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
