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

using System.Diagnostics;
using System.Runtime.InteropServices;
using NetFluss.Core;

namespace NetFluss.Native;

/// <summary>
/// Windows counterpart of the macOS <c>InterfaceSampler</c>: reads every interface's
/// 64-bit octet counters and turns successive snapshots into per-adapter rates.
///
/// Not thread-safe — drive it from a single timer, as the macOS version does.
/// </summary>
public sealed class InterfaceSampler
{
    /// <summary>
    /// Above this, <c>ReceiveLinkSpeed</c> is a placeholder rather than a real link rate.
    /// Virtual and tunnel adapters routinely report <see cref="ulong.MaxValue"/>.
    /// </summary>
    private const ulong ImplausibleLinkSpeedBps = 400_000_000_000;

    /// <summary>Guards against a corrupt table walk; a machine with this many interfaces does not exist.</summary>
    private const uint MaxPlausibleInterfaceCount = 4096;

    private readonly Dictionary<string, Snapshot> _previous = new(StringComparer.Ordinal);

    /// <summary>Wall-clock is unusable here: it jumps on sleep/resume and NTP correction.</summary>
    private long _previousTimestamp;

    public IReadOnlyList<AdapterStatus> Sample()
    {
        var rows = ReadInterfaceTable();
        var now = Stopwatch.GetTimestamp();

        var elapsedSeconds = _previousTimestamp == 0
            ? 0
            : (now - _previousTimestamp) / (double)Stopwatch.Frequency;
        _previousTimestamp = now;

        var adapters = new List<AdapterStatus>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var id = row.InterfaceGuid.ToString("B");
            seen.Add(id);

            var description = row.Description ?? string.Empty;
            var rx = row.InOctets;
            var tx = row.OutOctets;

            double rxRate = 0;
            double txRate = 0;

            if (elapsedSeconds > 0 && _previous.TryGetValue(id, out var previous))
            {
                rxRate = DeltaRate(previous.RxBytes, rx, elapsedSeconds);
                txRate = DeltaRate(previous.TxBytes, tx, elapsedSeconds);
            }

            _previous[id] = new Snapshot(rx, tx);

            adapters.Add(new AdapterStatus
            {
                Id = id,
                DisplayName = string.IsNullOrWhiteSpace(row.Alias) ? description : row.Alias,
                Description = description,
                Type = AdapterClassifier.ClassifyType(row.Type, description),
                IsTunnel = AdapterClassifier.IsTunnelInterface(row.Type, row.TunnelType, description),
                IsNonInternet = AdapterClassifier.IsNonInternetInterface(row.Type, row.IsFilterInterface),
                IsUp = (IpHelper.IfOperStatus)row.OperStatus == IpHelper.IfOperStatus.Up,
                LinkSpeedBps = NormalizeLinkSpeed(row.ReceiveLinkSpeed),
                RxBytes = rx,
                TxBytes = tx,
                RxRateBps = rxRate,
                TxRateBps = txRate,
            });
        }

        // Drop adapters that vanished, so a re-plugged NIC starts from a clean baseline
        // instead of emitting one enormous spike from a stale counter.
        if (_previous.Count != seen.Count)
        {
            foreach (var stale in _previous.Keys.Where(key => !seen.Contains(key)).ToList())
            {
                _previous.Remove(stale);
            }
        }

        return adapters;
    }

    /// <summary>Raw table read, exposed for the interop layout tests.</summary>
    internal static List<IpHelper.MIB_IF_ROW2> ReadInterfaceTable()
    {
        var status = IpHelper.GetIfTable2(out var table);
        if (status != IpHelper.NO_ERROR)
        {
            throw new InvalidOperationException($"GetIfTable2 failed with status {status}.");
        }

        if (table == nint.Zero)
        {
            return [];
        }

        try
        {
            var count = (uint)Marshal.ReadInt32(table);
            if (count > MaxPlausibleInterfaceCount)
            {
                throw new InvalidOperationException(
                    $"GetIfTable2 reported {count} interfaces, which indicates a MIB_IF_TABLE2 layout mismatch.");
            }

            var rows = new List<IpHelper.MIB_IF_ROW2>((int)count);
            var stride = Marshal.SizeOf<IpHelper.MIB_IF_ROW2>();

            for (var i = 0; i < count; i++)
            {
                var rowPtr = table + IpHelper.MIB_IF_TABLE2_ROWS_OFFSET + (i * stride);
                rows.Add(Marshal.PtrToStructure<IpHelper.MIB_IF_ROW2>(rowPtr));
            }

            return rows;
        }
        finally
        {
            IpHelper.FreeMibTable(table);
        }
    }

    private static double DeltaRate(ulong previous, ulong current, double elapsedSeconds)
    {
        // A counter that went backwards means the adapter reset (disable/enable, driver
        // reload, sleep). Report zero rather than a spike of ~2^64 bytes.
        if (current < previous)
        {
            return 0;
        }

        return (current - previous) / elapsedSeconds;
    }

    private static ulong? NormalizeLinkSpeed(ulong bitsPerSecond)
        => bitsPerSecond is 0 or >= ImplausibleLinkSpeedBps ? null : bitsPerSecond;

    private readonly record struct Snapshot(ulong RxBytes, ulong TxBytes);
}
