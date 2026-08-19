// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

/// <summary>
/// Pins the WCAG maths and the Phase 0 finding that motivated it: on the light taskbar the
/// upload green scored 3.04:1 while the download blue scored 4.08:1, so the two rows of the
/// meter did not read as equally important.
/// </summary>
public class ContrastTests
{
    private static readonly ThemeColor LightTaskbar = ThemeColor.FromHex("F3F3F3");
    private static readonly ThemeColor DarkTaskbar = ThemeColor.FromHex("202020");

    [Fact]
    public void Ratio_BlackOnWhite_IsTheMaximum()
        => Assert.Equal(21.0, Contrast.Ratio(ThemeColor.FromHex("000000"), ThemeColor.FromHex("FFFFFF")), 1);

    [Fact]
    public void Ratio_IdenticalColours_IsOne()
        => Assert.Equal(1.0, Contrast.Ratio(LightTaskbar, LightTaskbar), 3);

    [Fact]
    public void Ratio_IsSymmetric()
        => Assert.Equal(
            Contrast.Ratio(AppTheme.System.UploadColor, LightTaskbar),
            Contrast.Ratio(LightTaskbar, AppTheme.System.UploadColor),
            6);

    /// <summary>The measurements that started this, kept as documentation with teeth.</summary>
    [Fact]
    public void TheOriginalPhase0Gap_IsReproduced()
    {
        Assert.Equal(4.08, Contrast.Ratio(AppTheme.System.DownloadColor, LightTaskbar), 2);
        Assert.Equal(3.04, Contrast.Ratio(AppTheme.System.UploadColor, LightTaskbar), 2);
    }

    [Theory]
    [InlineData("F3F3F3")]
    [InlineData("202020")]
    public void EnsureRatio_LiftsBothAccentsOverTheFloor(string taskbarHex)
    {
        var taskbar = ThemeColor.FromHex(taskbarHex);

        foreach (var accent in new[] { AppTheme.System.DownloadColor, AppTheme.System.UploadColor })
        {
            var corrected = Contrast.EnsureRatio(accent, taskbar, Contrast.MinimumReadableRatio);

            Assert.True(
                Contrast.Ratio(corrected, taskbar) >= Contrast.MinimumReadableRatio,
                $"#{accent.ToHex()} on #{taskbarHex} came back at " +
                $"{Contrast.Ratio(corrected, taskbar):N2}:1 as #{corrected.ToHex()}");
        }
    }

    /// <summary>
    /// After correction the two rows must be within a stone's throw of each other, which is
    /// the actual complaint — not the absolute numbers but that they disagreed.
    /// </summary>
    [Fact]
    public void EnsureRatio_BalancesTheTwoRows()
    {
        var download = Contrast.EnsureRatio(AppTheme.System.DownloadColor, LightTaskbar, Contrast.MinimumReadableRatio);
        var upload = Contrast.EnsureRatio(AppTheme.System.UploadColor, LightTaskbar, Contrast.MinimumReadableRatio);

        var gap = Math.Abs(Contrast.Ratio(download, LightTaskbar) - Contrast.Ratio(upload, LightTaskbar));

        Assert.True(gap < 1.0, $"the rows still differ by {gap:N2} contrast points");
    }

    [Fact]
    public void EnsureRatio_LeavesACompliantColourAlone()
    {
        var white = ThemeColor.FromHex("FFFFFF");
        Assert.Equal(white, Contrast.EnsureRatio(white, DarkTaskbar, Contrast.MinimumReadableRatio));
    }

    /// <summary>On a dark taskbar the correction must brighten, not darken.</summary>
    [Fact]
    public void EnsureRatio_MovesAwayFromTheBackground()
    {
        var navy = ThemeColor.FromHex("102040");

        var onDark = Contrast.EnsureRatio(navy, DarkTaskbar, Contrast.MinimumReadableRatio);
        var onLight = Contrast.EnsureRatio(ThemeColor.FromHex("CCCCAA"), LightTaskbar, Contrast.MinimumReadableRatio);

        Assert.True(Contrast.RelativeLuminance(onDark) > Contrast.RelativeLuminance(navy));
        Assert.True(Contrast.RelativeLuminance(onLight) < Contrast.RelativeLuminance(ThemeColor.FromHex("CCCCAA")));
    }

    [Fact]
    public void IsLight_SplitsTheTwoTaskbars()
    {
        Assert.True(Contrast.IsLight(LightTaskbar));
        Assert.False(Contrast.IsLight(DarkTaskbar));
    }
}
