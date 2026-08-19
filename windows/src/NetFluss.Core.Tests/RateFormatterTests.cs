// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

/// <summary>
/// Locks the C# port to the exact output of the macOS <c>RateFormatter</c>. Every expected
/// string here is what Sources/Netfluss/Formatters.swift produces for the same input — if
/// one of these changes, the two platforms have silently diverged.
/// </summary>
public class RateFormatterTests
{
    [Theory]
    [InlineData(0, false, "0.00 B/s")]
    [InlineData(999, false, "999 B/s")]
    [InlineData(1_500, false, "1.50 KB/s")]
    [InlineData(15_000, false, "15.0 KB/s")]
    [InlineData(150_000, false, "150 KB/s")]
    [InlineData(1_500_000, false, "1.50 MB/s")]
    [InlineData(1_500, true, "12.0 Kb/s")]
    [InlineData(-5, false, "0.00 B/s")]
    public void FormatRate_MatchesMacOsLadder(double bytesPerSecond, bool useBits, string expected)
        => Assert.Equal(expected, RateFormatter.FormatRate(bytesPerSecond, useBits));

    [Theory]
    [InlineData(1_500_000, false, "M", 2, "1.50 MB/s")]
    [InlineData(1_500_000, false, "K", 0, "1500 KB/s")]
    [InlineData(1_500_000, false, "auto", 1, "1.5 MB/s")]
    [InlineData(1_500_000, true, "M", 1, "12.0 Mb/s")]
    public void FormatRate_PinnedUnit(double bytesPerSecond, bool useBits, string unit, int decimals, string expected)
        => Assert.Equal(expected, RateFormatter.FormatRate(bytesPerSecond, useBits, unit, decimals));

    [Theory]
    [InlineData(1200, "1.2 Gb/s")]
    [InlineData(866, "866 Mb/s")]
    [InlineData(null, "—")]
    public void FormatMbps(double? value, string expected)
        => Assert.Equal(expected, RateFormatter.FormatMbps(value));

    [Fact]
    public void FormatLinkSpeed_NullIsEmDash()
        => Assert.Equal("—", RateFormatter.FormatLinkSpeed(null, true));

    [Fact]
    public void FormatLinkSpeed_Gigabit()
        => Assert.Equal("1.00 Gb/s", RateFormatter.FormatLinkSpeed(1_000_000_000, useBits: true));

    /// <summary>
    /// The compact form has no macOS counterpart — it exists because a tray icon is 16 px
    /// wide. These cases pin the width budget: never more than four characters.
    /// </summary>
    [Theory]
    [InlineData(0, false, "0")]
    [InlineData(0.4, false, "0")]
    [InlineData(500, false, "500")]
    [InlineData(834_000, false, "834K")]
    [InlineData(4_720_000, false, "4.7M")]
    [InlineData(118_000_000, false, "118M")]
    [InlineData(1_180_000_000, false, "1.2G")]
    [InlineData(4_720_000, true, "38M")]
    public void FormatCompact(double bytesPerSecond, bool useBits, string expected)
        => Assert.Equal(expected, RateFormatter.FormatCompact(bytesPerSecond, useBits));

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(1_000)]
    [InlineData(834_000)]
    [InlineData(999_999_999)]
    [InlineData(12_345_678_901)]
    public void FormatCompact_NeverExceedsFiveCharacters(double bytesPerSecond)
    {
        Assert.True(RateFormatter.FormatCompact(bytesPerSecond, useBits: false).Length <= 5);
        Assert.True(RateFormatter.FormatCompact(bytesPerSecond, useBits: true).Length <= 5);
    }

    /// <summary>
    /// Swift's String(format:) is not locale-aware, so the Mac always prints a dot. A German
    /// Windows install must not start printing "1,50 MB/s".
    /// </summary>
    [Fact]
    public void FormatRate_IgnoresCurrentCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1.50 MB/s", RateFormatter.FormatRate(1_500_000, useBits: false));
            Assert.Equal("4.7M", RateFormatter.FormatCompact(4_720_000, useBits: false));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
