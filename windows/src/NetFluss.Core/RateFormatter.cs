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

using System.Globalization;

namespace NetFluss.Core;

/// <summary>
/// Port of the macOS <c>RateFormatter</c> (Sources/Netfluss/Formatters.swift).
///
/// Swift's <c>String(format:)</c> is not locale-aware, so the macOS app always
/// renders "1.25 MB/s" with a dot. We format with <see cref="CultureInfo.InvariantCulture"/>
/// to keep the two platforms byte-identical; changing this would silently make
/// German builds disagree with their Mac counterparts.
/// </summary>
public static class RateFormatter
{
    private static readonly string[] BitUnits = ["b/s", "Kb/s", "Mb/s", "Gb/s", "Tb/s"];
    private static readonly string[] ByteUnits = ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];

    /// <summary>Compact suffixes for the notification-area meter, where every pixel counts.</summary>
    private static readonly string[] CompactBitUnits = ["b", "K", "M", "G", "T"];
    private static readonly string[] CompactByteUnits = ["B", "K", "M", "G", "T"];

    public static string FormatRate(double bytesPerSecond, bool useBits)
    {
        var value = Math.Max(0, bytesPerSecond);
        return useBits
            ? Format(value * 8.0, BitUnits)
            : Format(value, ByteUnits);
    }

    public static string FormatLinkSpeed(ulong? bitsPerSecond, bool useBits)
    {
        if (bitsPerSecond is not { } bps)
        {
            return "—";
        }

        return useBits
            ? Format(bps, BitUnits)
            : Format(bps / 8.0, ByteUnits);
    }

    public static string FormatMbps(double? value)
    {
        if (value is not { } mbps)
        {
            return "—";
        }

        return mbps >= 1000
            ? string.Format(CultureInfo.InvariantCulture, "{0:F1} Gb/s", mbps / 1000.0)
            : string.Format(CultureInfo.InvariantCulture, "{0:F0} Mb/s", mbps);
    }

    /// <summary>
    /// Rate with a pinned unit scale and fixed decimal places.
    /// </summary>
    /// <param name="pinnedUnit">"auto", "K", "M" or "G".</param>
    /// <param name="decimals">0–3 decimal places.</param>
    public static string FormatRate(double bytesPerSecond, bool useBits, string pinnedUnit, int decimals)
    {
        var value = Math.Max(0, bytesPerSecond);
        var baseValue = useBits ? value * 8.0 : value;
        var units = useBits ? BitUnits : ByteUnits;

        if (pinnedUnit == "auto")
        {
            return Format(baseValue, units, decimals);
        }

        var scaleIndex = pinnedUnit switch
        {
            "K" => 1,
            "M" => 2,
            "G" => 3,
            _ => 0,
        };

        var divisor = Math.Pow(1000.0, scaleIndex);
        var adjusted = baseValue / divisor;
        var unit = scaleIndex < units.Length ? units[scaleIndex] : units[^1];
        return string.Concat(adjusted.ToString("F" + decimals, CultureInfo.InvariantCulture), " ", unit);
    }

    /// <summary>
    /// Three-or-four character rate for the notification-area icon, e.g. "1.2M", "834K", "0".
    /// Windows has no menu-bar text area, so the tray bitmap is only ~16–32 px wide and
    /// cannot fit the full "1.25 MB/s" label the macOS menu bar uses.
    /// </summary>
    public static string FormatCompact(double bytesPerSecond, bool useBits)
    {
        var value = Math.Max(0, bytesPerSecond);
        var baseValue = useBits ? value * 8.0 : value;
        var units = useBits ? CompactBitUnits : CompactByteUnits;

        var adjusted = baseValue;
        var unitIndex = 0;
        while (adjusted >= 1000.0 && unitIndex < units.Length - 1)
        {
            adjusted /= 1000.0;
            unitIndex++;
        }

        // Below the first scale step there is no useful fraction to show, and "0" reads
        // far better than "0.00 B" in a 16 px box.
        if (unitIndex == 0)
        {
            return adjusted < 1
                ? "0"
                : adjusted.ToString("F0", CultureInfo.InvariantCulture);
        }

        var format = adjusted < 10 ? "F1" : "F0";
        return string.Concat(adjusted.ToString(format, CultureInfo.InvariantCulture), units[unitIndex]);
    }

    private static string Format(double value, string[] units, int decimals)
    {
        var (adjusted, unit) = Scale(value, units);
        return string.Concat(adjusted.ToString("F" + decimals, CultureInfo.InvariantCulture), " ", unit);
    }

    private static string Format(double value, string[] units)
    {
        var (adjusted, unit) = Scale(value, units);

        // Matches the macOS significant-digit ladder: 2 decimals under 10, 1 under 100, 0 above.
        var format = adjusted switch
        {
            < 10 => "F2",
            < 100 => "F1",
            _ => "F0",
        };

        return string.Concat(adjusted.ToString(format, CultureInfo.InvariantCulture), " ", unit);
    }

    private static (double Value, string Unit) Scale(double value, string[] units)
    {
        var adjusted = value;
        var unitIndex = 0;
        while (adjusted >= 1000.0 && unitIndex < units.Length - 1)
        {
            adjusted /= 1000.0;
            unitIndex++;
        }

        return (adjusted, units[unitIndex]);
    }
}
