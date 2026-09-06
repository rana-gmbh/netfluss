// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

/// <summary>
/// User ordering and renaming — the last two of the four adapter settings that were stored
/// but never read. Both are keyed on the interface GUID rather than the connection name, so
/// they survive Windows renaming an adapter or it moving between ports.
/// </summary>
public class AdapterOrderAndNameTests
{
    private static AdapterStatus Adapter(string id, string name, double rate) => new()
    {
        Id = id,
        DisplayName = name,
        Description = name,
        Type = AdapterType.Ethernet,
        IsTunnel = false,
        IsNonInternet = false,
        IsUp = true,
        RxBytes = 0,
        TxBytes = 0,
        RxRateBps = rate,
        TxRateBps = 0,
    };

    private static readonly AdapterStatus[] Three =
    [
        Adapter("{A}", "Ethernet", 10),
        Adapter("{B}", "Wi-Fi", 500),
        Adapter("{C}", "VPN", 100),
    ];

    [Fact]
    public void NoOrder_FallsBackToBusiestFirst()
    {
        var ordered = AdapterTotalsFilter.InUserOrder(Three, []);

        Assert.Equal(["{B}", "{C}", "{A}"], ordered.Select(a => a.Id));
    }

    [Fact]
    public void UserOrder_Wins()
    {
        var ordered = AdapterTotalsFilter.InUserOrder(Three, ["{A}", "{C}", "{B}"]);

        Assert.Equal(["{A}", "{C}", "{B}"], ordered.Select(a => a.Id));
    }

    /// <summary>
    /// A partial order is the normal case — the user drags one adapter and leaves the rest.
    /// Ranked ones lead; the remainder keep the busiest-first default rather than being
    /// dropped or pinned to the top.
    /// </summary>
    [Fact]
    public void PartialOrder_LeavesTheRestByTraffic()
    {
        var ordered = AdapterTotalsFilter.InUserOrder(Three, ["{A}"]);

        Assert.Equal(["{A}", "{B}", "{C}"], ordered.Select(a => a.Id));
    }

    /// <summary>An unplugged dock should keep its slot for when it comes back.</summary>
    [Fact]
    public void OrderMayNameAdaptersThatAreNotPresent()
    {
        var ordered = AdapterTotalsFilter.InUserOrder(Three, ["{GONE}", "{C}"]);

        Assert.Equal(["{C}", "{B}", "{A}"], ordered.Select(a => a.Id));
    }

    [Fact]
    public void OrderMatching_IgnoresCase()
    {
        var ordered = AdapterTotalsFilter.InUserOrder(Three, ["{a}"]);

        Assert.Equal("{A}", ordered[0].Id);
    }

    /// <summary>A duplicated id must not make the comparer inconsistent.</summary>
    [Fact]
    public void DuplicateIdsInOrder_AreTolerated()
    {
        var ordered = AdapterTotalsFilter.InUserOrder(Three, ["{C}", "{A}", "{C}"]);

        Assert.Equal(["{C}", "{A}", "{B}"], ordered.Select(a => a.Id));
    }

    /// <summary>
    /// Regression, found by reading the popover's own text rather than the code: the WFP and
    /// QoS filter pseudo-interfaces mirror the adapter they sit on, and were excluded from
    /// the totals but not from the list. A plain Ethernet machine showed four rows all
    /// reading the same rate.
    /// </summary>
    [Fact]
    public void MirrorInterfaces_AreNotListed()
    {
        var real = Adapter("{A}", "Ethernet", 554);
        var mirror = Adapter("{A-WFP}", "Ethernet-WFP Native MAC Layer LightWeight Filter-0000", 554)
                     with { IsNonInternet = true };

        var visible = AdapterTotalsFilter.VisibleAdapters([real, mirror], new AdapterVisibilityOptions());

        Assert.Equal(["{A}"], visible.Select(a => a.Id));
    }

    /// <summary>They must stay out even when the user asks to see disconnected adapters.</summary>
    [Fact]
    public void MirrorInterfaces_StayHiddenWithShowInactive()
    {
        var mirror = Adapter("{A-WFP}", "mirror", 0) with { IsNonInternet = true, IsUp = false };

        Assert.Empty(AdapterTotalsFilter.VisibleAdapters(
            [mirror],
            new AppSettings { ShowInactiveAdapters = true }.VisibilityOptions()));
    }

    [Fact]
    public void CustomNames_ReplaceTheDisplayName()
    {
        var renamed = AdapterTotalsFilter.WithCustomNames(
            Three,
            new Dictionary<string, string> { ["{B}"] = "Office Wi-Fi" });

        Assert.Equal("Office Wi-Fi", renamed.Single(a => a.Id == "{B}").DisplayName);
        Assert.Equal("Ethernet", renamed.Single(a => a.Id == "{A}").DisplayName);
    }

    /// <summary>A blank label is not a name; it must not blank out the row.</summary>
    [Fact]
    public void BlankCustomName_IsIgnored()
    {
        var renamed = AdapterTotalsFilter.WithCustomNames(
            Three,
            new Dictionary<string, string> { ["{A}"] = "   " });

        Assert.Equal("Ethernet", renamed.Single(a => a.Id == "{A}").DisplayName);
    }

    [Fact]
    public void Renaming_IsStoredAndCleared()
    {
        var settings = new AppSettings();

        settings.SetAdapterName("{A}", "  Office  ");
        Assert.Equal("Office", settings.AdapterDisplayName("{A}", "Ethernet"));

        settings.SetAdapterName("{A}", "");
        Assert.Equal("Ethernet", settings.AdapterDisplayName("{A}", "Ethernet"));
        Assert.Empty(settings.AdapterCustomNames);
    }

    /// <summary>
    /// A Dictionary is compared by reference, so an in-place edit would raise nothing and
    /// the rename would apply in memory but never reach disk.
    /// </summary>
    [Fact]
    public void Renaming_RaisesChangeNotification()
    {
        var settings = new AppSettings();
        var raised = 0;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.AdapterCustomNames))
            {
                raised++;
            }
        };

        settings.SetAdapterName("{A}", "Office");

        Assert.True(raised > 0, "renaming raised no change, so nothing would persist it");
    }

    [Fact]
    public void MoveAdapter_RecordsTheWholeSequence()
    {
        var settings = new AppSettings();

        settings.MoveAdapter(["{A}", "{B}", "{C}"], "{C}", 0);

        Assert.Equal(["{C}", "{A}", "{B}"], settings.AdapterOrder);
    }

    [Fact]
    public void MoveAdapter_ToTheEnd()
    {
        var settings = new AppSettings();

        settings.MoveAdapter(["{A}", "{B}", "{C}"], "{A}", 2);

        Assert.Equal(["{B}", "{C}", "{A}"], settings.AdapterOrder);
    }

    [Fact]
    public void MoveAdapter_ClampsAnOutOfRangeIndex()
    {
        var settings = new AppSettings();

        settings.MoveAdapter(["{A}", "{B}"], "{A}", 99);

        Assert.Equal(["{B}", "{A}"], settings.AdapterOrder);
    }

    [Fact]
    public void ResetOrder_ReturnsToBusiestFirst()
    {
        var settings = new AppSettings();
        settings.MoveAdapter(["{A}", "{B}", "{C}"], "{C}", 0);

        settings.ResetAdapterOrder();

        Assert.Empty(settings.AdapterOrder);
        Assert.Equal(["{B}", "{C}", "{A}"], AdapterTotalsFilter.InUserOrder(Three, settings.AdapterOrder).Select(a => a.Id));
    }

    [Fact]
    public void OrderAndNames_SurviveAReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netfluss-order-{Guid.NewGuid():N}.json");

        try
        {
            var store = new SettingsStore(path);
            store.Batch(settings =>
            {
                settings.MoveAdapter(["{A}", "{B}"], "{B}", 0);
                settings.SetAdapterName("{A}", "Office");
            });

            var reloaded = new SettingsStore(path).Settings;

            Assert.Equal(["{B}", "{A}"], reloaded.AdapterOrder);
            Assert.Equal("Office", reloaded.AdapterDisplayName("{A}", "Ethernet"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
