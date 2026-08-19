// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Runtime.InteropServices;
using NetFluss.Core;
using Xunit;

namespace NetFluss.Native.Tests;

/// <summary>
/// These tests exist because <c>MIB_IF_ROW2</c> is hand-marshalled. A wrong field offset or
/// stride does not throw — it silently produces a believable first row and garbage
/// afterwards, which would show up as phantom adapters and nonsense rates months later.
///
/// The strategy is a plausibility walk: every row the kernel reports must have a non-empty
/// alias, a defined operational status and a non-zero interface type. If the stride is off
/// by even one byte, row 2 onward fails all three.
/// </summary>
public class InterfaceTableTests
{
    [Fact]
    public void RowStrideIsPlausible()
    {
        var size = Marshal.SizeOf<IpHelper.MIB_IF_ROW2>();

        // Two 257-WCHAR strings alone account for 1028 bytes; the counters add ~200 more.
        Assert.InRange(size, 1300, 1400);

        // The struct ends in a run of ULONG64 counters, so its size must be 8-aligned.
        Assert.Equal(0, size % 8);
    }

    [Fact]
    public void EveryRowIsPlausible()
    {
        var rows = InterfaceSampler.ReadInterfaceTable();

        // Every Windows machine has at least a loopback interface.
        Assert.NotEmpty(rows);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            Assert.False(
                string.IsNullOrWhiteSpace(row.Alias),
                $"Row {i} of {rows.Count} has an empty alias — MIB_IF_ROW2 stride or MIB_IF_TABLE2 row offset is wrong.");

            Assert.InRange(row.OperStatus, (uint)IpHelper.IfOperStatus.Up, (uint)IpHelper.IfOperStatus.LowerLayerDown);

            Assert.True(row.Type != 0, $"Row {i} reported interface type 0, which is not a valid IANA ifType.");

            Assert.True(row.InterfaceIndex != 0, $"Row {i} reported interface index 0.");
        }
    }

    /// <summary>Windows always exposes "Software Loopback Interface 1"; a missing one means bad parsing.</summary>
    [Fact]
    public void LoopbackIsPresentAndClassifiedAsNonInternet()
    {
        var rows = InterfaceSampler.ReadInterfaceTable();
        var loopback = rows.Where(row => row.Type == AdapterClassifier.IfTypeSoftwareLoopback).ToList();

        Assert.NotEmpty(loopback);
        Assert.All(loopback, row => Assert.True(AdapterClassifier.IsNonInternetInterface(row.Type, row.IsFilterInterface)));
    }

    [Fact]
    public void GuidsAreDistinctAcrossRows()
    {
        var rows = InterfaceSampler.ReadInterfaceTable();
        var guids = rows.Select(row => row.InterfaceGuid).ToList();

        // Duplicate GUIDs are the classic symptom of reading the same memory repeatedly
        // because the stride was computed as zero or the table offset was wrong.
        Assert.Equal(guids.Count, guids.Distinct().Count());
    }
}

public class InterfaceSamplerTests
{
    [Fact]
    public void FirstSampleReportsZeroRates()
    {
        var sampler = new InterfaceSampler();
        var adapters = sampler.Sample();

        Assert.NotEmpty(adapters);

        // No previous snapshot exists, so there is nothing to differentiate — the macOS app
        // shows "Gathering data…" for exactly this reason.
        Assert.All(adapters, adapter =>
        {
            Assert.Equal(0, adapter.RxRateBps);
            Assert.Equal(0, adapter.TxRateBps);
        });
    }

    [Fact]
    public async Task SecondSampleProducesNonNegativeRates()
    {
        var sampler = new InterfaceSampler();
        sampler.Sample();
        await Task.Delay(250);
        var adapters = sampler.Sample();

        Assert.NotEmpty(adapters);
        Assert.All(adapters, adapter =>
        {
            Assert.True(adapter.RxRateBps >= 0, $"{adapter.DisplayName} reported a negative download rate.");
            Assert.True(adapter.TxRateBps >= 0, $"{adapter.DisplayName} reported a negative upload rate.");

            // A counter-wrap bug would surface here as an absurd spike. 400 Gb/s is well
            // above any real NIC and well below the 2^64 nonsense a wrap would produce.
            Assert.True(adapter.RxRateBps < 50e9, $"{adapter.DisplayName} reported an impossible download rate.");
        });
    }

    [Fact]
    public void AdaptersCarryStableGuidIdentity()
    {
        var sampler = new InterfaceSampler();
        var first = sampler.Sample().Select(adapter => adapter.Id).OrderBy(id => id).ToList();
        var second = sampler.Sample().Select(adapter => adapter.Id).OrderBy(id => id).ToList();

        Assert.Equal(first, second);
        Assert.All(first, id => Assert.True(Guid.TryParse(id, out _), $"'{id}' is not a GUID."));
    }

    [Fact]
    public void LinkSpeedPlaceholdersAreNormalizedAway()
    {
        var adapters = new InterfaceSampler().Sample();

        // Virtual adapters report ulong.MaxValue or 0; neither is a link rate worth showing.
        Assert.All(adapters, adapter =>
            Assert.True(adapter.LinkSpeedBps is null or (> 0 and < 400_000_000_000)));
    }
}
