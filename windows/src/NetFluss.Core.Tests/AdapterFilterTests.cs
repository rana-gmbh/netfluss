// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

public class AdapterClassifierTests
{
    [Theory]
    [InlineData(AdapterClassifier.IfTypeIeee80211, "Intel(R) Wi-Fi 6E AX211 160MHz", AdapterType.WiFi)]
    [InlineData(AdapterClassifier.IfTypeEthernet, "Realtek Gaming 2.5GbE Family Controller", AdapterType.Ethernet)]
    [InlineData(AdapterClassifier.IfTypeSoftwareLoopback, "Software Loopback Interface 1", AdapterType.Other)]
    [InlineData(AdapterClassifier.IfTypeTunnel, "Teredo Tunneling Pseudo-Interface", AdapterType.Other)]
    public void ClassifyType(uint ifType, string description, AdapterType expected)
        => Assert.Equal(expected, AdapterClassifier.ClassifyType(ifType, description));

    /// <summary>
    /// Wintun and TAP-Windows present as IF_TYPE_ETHERNET_CSMACD, so type alone is not
    /// enough — without the description check they would be counted as a physical NIC and
    /// double every VPN byte against the underlying adapter.
    /// </summary>
    [Theory]
    [InlineData("Wintun Userspace Tunnel")]
    [InlineData("TAP-Windows Adapter V9")]
    [InlineData("WireGuard Tunnel")]
    public void EthernetLookalikeTunnelsAreNotEthernet(string description)
    {
        Assert.Equal(AdapterType.Other, AdapterClassifier.ClassifyType(AdapterClassifier.IfTypeEthernet, description));
        Assert.True(AdapterClassifier.IsTunnelInterface(AdapterClassifier.IfTypeEthernet, 0, description));
    }

    [Fact]
    public void PppAndTunnelTypesAreTunnels()
    {
        Assert.True(AdapterClassifier.IsTunnelInterface(AdapterClassifier.IfTypePpp, 0, "WAN Miniport (IKEv2)"));
        Assert.True(AdapterClassifier.IsTunnelInterface(AdapterClassifier.IfTypeTunnel, 0, "Teredo"));
        Assert.True(AdapterClassifier.IsTunnelInterface(AdapterClassifier.IfTypeEthernet, 3, "Some encapsulation"));
        Assert.False(AdapterClassifier.IsTunnelInterface(AdapterClassifier.IfTypeEthernet, 0, "Intel I225-V"));
    }

    /// <summary>
    /// Filter interfaces mirror the traffic of the adapter beneath them. This is the Windows
    /// equivalent of the macOS lo0/awdl exclusion that fixed issue #54.
    /// </summary>
    [Fact]
    public void LoopbackAndFilterInterfacesNeverCount()
    {
        Assert.True(AdapterClassifier.IsNonInternetInterface(AdapterClassifier.IfTypeSoftwareLoopback, false));
        Assert.True(AdapterClassifier.IsNonInternetInterface(AdapterClassifier.IfTypeEthernet, true));
        Assert.False(AdapterClassifier.IsNonInternetInterface(AdapterClassifier.IfTypeEthernet, false));
    }
}

public class AdapterTotalsFilterTests
{
    private static AdapterStatus Adapter(
        string id,
        AdapterType type = AdapterType.Ethernet,
        double rx = 0,
        double tx = 0,
        bool isUp = true,
        bool isTunnel = false,
        bool isNonInternet = false) => new()
    {
        Id = id,
        DisplayName = id,
        Description = id,
        Type = type,
        IsTunnel = isTunnel,
        IsNonInternet = isNonInternet,
        IsUp = isUp,
        RxBytes = 0,
        TxBytes = 0,
        RxRateBps = rx,
        TxRateBps = tx,
    };

    [Fact]
    public void TotalsSumEveryCountingAdapter()
    {
        var adapters = new[]
        {
            Adapter("eth", rx: 100, tx: 10),
            Adapter("wifi", AdapterType.WiFi, rx: 50, tx: 5),
        };

        var totals = AdapterTotalsFilter.Totals(adapters, onlyVisible: false, excludeTunnelAdapters: false, new AdapterVisibilityOptions());

        Assert.Equal(150, totals.RxRateBps);
        Assert.Equal(15, totals.TxRateBps);
    }

    [Fact]
    public void LoopbackIsExcludedEvenWhenTunnelsAreIncluded()
    {
        var adapters = new[]
        {
            Adapter("eth", rx: 100),
            Adapter("loopback", AdapterType.Other, rx: 900, isNonInternet: true),
        };

        var totals = AdapterTotalsFilter.Totals(adapters, onlyVisible: false, excludeTunnelAdapters: false, new AdapterVisibilityOptions());

        Assert.Equal(100, totals.RxRateBps);
    }

    [Fact]
    public void ExcludeTunnelsDropsTunnelTraffic()
    {
        var adapters = new[]
        {
            Adapter("eth", rx: 100),
            Adapter("wg0", AdapterType.Other, rx: 90, isTunnel: true),
        };

        Assert.Equal(190, AdapterTotalsFilter.Totals(adapters, false, false, new AdapterVisibilityOptions()).RxRateBps);
        Assert.Equal(100, AdapterTotalsFilter.Totals(adapters, false, true, new AdapterVisibilityOptions()).RxRateBps);
    }

    [Fact]
    public void HiddenAdaptersDropOutOfVisibleTotals()
    {
        var adapters = new[] { Adapter("eth", rx: 100), Adapter("eth2", rx: 40) };
        var options = new AdapterVisibilityOptions { Hidden = new HashSet<string> { "eth2" } };

        Assert.Equal(100, AdapterTotalsFilter.Totals(adapters, onlyVisible: true, false, options).RxRateBps);
        Assert.Equal(140, AdapterTotalsFilter.Totals(adapters, onlyVisible: false, false, options).RxRateBps);
    }

    [Fact]
    public void IdleDownAdaptersHideUnlessShowInactive()
    {
        var adapter = Adapter("eth", isUp: false);

        Assert.False(AdapterTotalsFilter.IsVisible(adapter, new AdapterVisibilityOptions { ShowInactive = false }));
        Assert.True(AdapterTotalsFilter.IsVisible(adapter, new AdapterVisibilityOptions { ShowInactive = true }));
    }

    [Fact]
    public void GracePeriodKeepsRecentlyActiveAdaptersVisible()
    {
        var adapter = Adapter("eth", isUp: true);
        var withGrace = new AdapterVisibilityOptions
        {
            GraceEnabled = true,
            GraceDeadlines = new Dictionary<string, DateTimeOffset> { ["eth"] = DateTimeOffset.UtcNow.AddSeconds(5) },
        };
        var withoutGrace = new AdapterVisibilityOptions { GraceEnabled = true };

        Assert.True(AdapterTotalsFilter.IsVisible(adapter, withGrace));
        Assert.False(AdapterTotalsFilter.IsVisible(adapter, withoutGrace));
    }

    [Fact]
    public void OtherAdaptersHideWhenShowOtherIsOff()
    {
        var adapter = Adapter("wg0", AdapterType.Other, rx: 10, isTunnel: true);

        Assert.False(AdapterTotalsFilter.IsVisible(adapter, new AdapterVisibilityOptions { ShowOtherAdapters = false }));
        Assert.True(AdapterTotalsFilter.IsVisible(adapter, new AdapterVisibilityOptions { ShowOtherAdapters = true }));
    }
}

public class ThemeColorTests
{
    [Fact]
    public void ParsesSixDigitHex()
    {
        var color = ThemeColor.FromHex("8be9fd");
        Assert.Equal(0x8B, color.R);
        Assert.Equal(0xE9, color.G);
        Assert.Equal(0xFD, color.B);
    }

    [Fact]
    public void TolerantOfLeadingHash() => Assert.Equal(ThemeColor.FromHex("8be9fd"), ThemeColor.FromHex("#8be9fd"));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("8be9fdaa")]
    [InlineData("zzzzzz")]
    public void RejectsMalformed(string hex) => Assert.False(ThemeColor.TryFromHex(hex, out _));

    [Fact]
    public void RoundTripsThroughHex() => Assert.Equal("8BE9FD", ThemeColor.FromHex("8be9fd").ToHex());

    [Fact]
    public void PresetsMatchMacOsPalette()
    {
        Assert.Equal("8BE9FD", AppTheme.Dracula.DownloadColor.ToHex());
        Assert.Equal("50FA7B", AppTheme.Dracula.UploadColor.ToHex());
        Assert.Equal("88C0D0", AppTheme.Nord.DownloadColor.ToHex());
        Assert.Equal("268BD2", AppTheme.Solarized.DownloadColor.ToHex());
        Assert.Equal(AppTheme.System, AppTheme.Named("nope"));
    }

    [Fact]
    public void SystemAccentResolvesToNullSoTheAppLayerCanFollowTheTaskbar()
        => Assert.Null(AccentPalette.Resolve("system", string.Empty, AppTheme.System.DownloadColor));
}

public class LocalizationTests
{
    [Theory]
    [InlineData(AppLanguage.German, "Preferences", "Einstellungen")]
    [InlineData(AppLanguage.English, "Preferences", "Preferences")]
    public void ResolvesTranslations(AppLanguage language, string key, string expected)
    {
        Localization.Use(language);
        try
        {
            Assert.Equal(expected, Localization.L(key));
        }
        finally
        {
            Localization.Use(AppLanguage.System);
        }
    }

    /// <summary>Mirrors NSLocalizedString: an unknown key renders as itself, never as blank.</summary>
    [Fact]
    public void UnknownKeyFallsBackToTheKey()
        => Assert.Equal("No such string", Localization.L("No such string"));

    /// <summary>
    /// The Cocoa "%@" specifiers are rewritten to "{0}" by the generator; if that ever
    /// regresses, formatting silently produces the raw "%@" in the UI.
    /// </summary>
    [Fact]
    public void CompositeFormattingWorks()
    {
        Localization.Use(AppLanguage.English);
        try
        {
            Assert.Equal("Collecting since yesterday", Localization.L("Collecting since {0}", "yesterday"));
        }
        finally
        {
            Localization.Use(AppLanguage.System);
        }
    }

    [Fact]
    public void AllFourCataloguesAreEmbedded()
    {
        foreach (var language in new[]
                 {
                     AppLanguage.English, AppLanguage.German,
                     AppLanguage.SimplifiedChinese, AppLanguage.TraditionalChinese,
                 })
        {
            Localization.Use(language);
            try
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(Localization.L("Preferences")),
                    $"{language} resolved 'Preferences' to nothing — its satellite assembly is missing.");
            }
            finally
            {
                Localization.Use(AppLanguage.System);
            }
        }
    }
}
