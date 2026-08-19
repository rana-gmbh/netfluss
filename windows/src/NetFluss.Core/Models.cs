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

namespace NetFluss.Core;

public enum AdapterType
{
    WiFi,
    Ethernet,
    Other,
}

/// <summary>
/// One network interface at a point in time. Port of the macOS <c>AdapterStatus</c>.
///
/// <para><b>Platform difference:</b> macOS identifies interfaces by BSD name ("en0") and
/// classifies them by name prefix. Windows has no equivalent stable short name, so
/// <see cref="Id"/> is the interface GUID — stable across renames and reconnects — and
/// classification comes from the NDIS metadata in MIB_IF_ROW2 rather than from the name.</para>
/// </summary>
public sealed record AdapterStatus
{
    /// <summary>Interface GUID, e.g. "{3A5B1C...}". Stable identity for settings keys.</summary>
    public required string Id { get; init; }

    /// <summary>Connection alias shown in Settings, e.g. "Wi-Fi" or "Ethernet 2".</summary>
    public required string DisplayName { get; init; }

    /// <summary>NDIS description, e.g. "Intel(R) Wi-Fi 6E AX211 160MHz".</summary>
    public required string Description { get; init; }

    public required AdapterType Type { get; init; }

    public required bool IsTunnel { get; init; }

    /// <summary>
    /// Interfaces that never carry real internet uplink traffic and must never be counted
    /// in totals: loopback and NDIS/WFP filter pseudo-interfaces, which mirror the traffic
    /// of the adapter they sit on and would double every number.
    /// </summary>
    public required bool IsNonInternet { get; init; }

    public required bool IsUp { get; init; }

    public ulong? LinkSpeedBps { get; init; }

    public string? WifiSsid { get; init; }

    public string? WifiMode { get; init; }

    public double? WifiTxRateMbps { get; init; }

    public required ulong RxBytes { get; init; }

    public required ulong TxBytes { get; init; }

    public double RxRateBps { get; init; }

    public double TxRateBps { get; init; }

    public bool HasTraffic => RxRateBps > 0 || TxRateBps > 0;
}

public readonly record struct RateTotals(double RxRateBps, double TxRateBps)
{
    public static RateTotals Zero => new(0, 0);
}

/// <summary>
/// Decides which interfaces count toward usage totals. Single source of truth shared by
/// the live header totals and the historical statistics, so the two always agree — the
/// same contract as the macOS <c>AdapterClassifier</c>.
/// </summary>
public static class AdapterClassifier
{
    // IANA ifType values as reported in MIB_IF_ROW2.Type.
    public const uint IfTypeOther = 1;
    public const uint IfTypeEthernet = 6;
    public const uint IfTypePpp = 23;
    public const uint IfTypeSoftwareLoopback = 24;
    public const uint IfTypeIeee80211 = 71;
    public const uint IfTypeTunnel = 131;
    public const uint IfTypeIeee1394 = 144;

    /// <summary>
    /// Virtual tunnel adapters that present themselves as plain Ethernet and therefore
    /// cannot be detected from <c>Type</c> alone. Matched case-insensitively against the
    /// NDIS description. Heuristic by necessity — Windows offers nothing authoritative here.
    /// </summary>
    private static readonly string[] TunnelDescriptionMarkers =
    [
        "wintun",
        "tap-windows",
        "tap adapter",
        "wireguard",
        "openvpn",
        "tunnel",
        "vpn",
        "wan miniport (ikev2)",
        "wan miniport (l2tp)",
        "wan miniport (pptp)",
        "wan miniport (sstp)",
    ];

    public static AdapterType ClassifyType(uint ifType, string description)
    {
        if (ifType == IfTypeIeee80211)
        {
            return AdapterType.WiFi;
        }

        // Only treat Ethernet as Ethernet when it is not a disguised tunnel adapter.
        if ((ifType == IfTypeEthernet || ifType == IfTypeIeee1394) && !MatchesTunnelDescription(description))
        {
            return AdapterType.Ethernet;
        }

        return AdapterType.Other;
    }

    public static bool IsTunnelInterface(uint ifType, uint tunnelType, string description)
    {
        if (ifType is IfTypeTunnel or IfTypePpp)
        {
            return true;
        }

        // TUNNEL_TYPE_NONE == 0; anything else is a configured tunnel encapsulation.
        if (tunnelType != 0)
        {
            return true;
        }

        return MatchesTunnelDescription(description);
    }

    public static bool IsNonInternetInterface(uint ifType, bool isFilterInterface)
        => ifType == IfTypeSoftwareLoopback || isFilterInterface;

    /// <summary>
    /// Whether an interface's bytes count toward usage totals, regardless of whether it is
    /// shown in the adapter list.
    /// </summary>
    public static bool CountsTowardTotals(AdapterStatus adapter, bool excludeTunnels)
    {
        if (adapter.IsNonInternet)
        {
            return false;
        }

        return !excludeTunnels || !adapter.IsTunnel;
    }

    private static bool MatchesTunnelDescription(string description)
    {
        foreach (var marker in TunnelDescriptionMarkers)
        {
            if (description.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Port of the macOS <c>AdapterTotalsFilter</c> — visibility rules and header totals.</summary>
public static class AdapterTotalsFilter
{
    public static IReadOnlyList<AdapterStatus> VisibleAdapters(
        IReadOnlyList<AdapterStatus> adapters,
        AdapterVisibilityOptions options)
    {
        var visible = new List<AdapterStatus>(adapters.Count);
        foreach (var adapter in adapters)
        {
            if (IsVisible(adapter, options))
            {
                visible.Add(adapter);
            }
        }

        return visible;
    }

    public static RateTotals Totals(
        IReadOnlyList<AdapterStatus> adapters,
        bool onlyVisible,
        bool excludeTunnelAdapters,
        AdapterVisibilityOptions options)
    {
        double rx = 0;
        double tx = 0;

        foreach (var adapter in adapters)
        {
            if (onlyVisible && !IsVisible(adapter, options))
            {
                continue;
            }

            if (!AdapterClassifier.CountsTowardTotals(adapter, excludeTunnelAdapters))
            {
                continue;
            }

            rx += adapter.RxRateBps;
            tx += adapter.TxRateBps;
        }

        return new RateTotals(rx, tx);
    }

    public static bool IsVisible(AdapterStatus adapter, AdapterVisibilityOptions options)
    {
        if (!options.ShowOtherAdapters && adapter.Type == AdapterType.Other)
        {
            return false;
        }

        if (options.Hidden.Contains(adapter.Id))
        {
            return false;
        }

        var idle = !adapter.HasTraffic;
        if (options.GraceEnabled && idle)
        {
            return options.GraceDeadlines.ContainsKey(adapter.Id);
        }

        if (!options.ShowInactive && idle && !adapter.IsUp)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// The macOS filter takes six loose parameters; bundling them keeps the call sites honest
/// and makes it impossible to transpose two booleans by accident.
/// </summary>
public sealed record AdapterVisibilityOptions
{
    public bool ShowOtherAdapters { get; init; } = true;

    public bool ShowInactive { get; init; }

    public bool GraceEnabled { get; init; }

    public IReadOnlySet<string> Hidden { get; init; } = new HashSet<string>();

    public IReadOnlyDictionary<string, DateTimeOffset> GraceDeadlines { get; init; }
        = new Dictionary<string, DateTimeOffset>();
}
