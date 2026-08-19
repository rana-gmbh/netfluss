// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Drawing;
using NetFluss.Core;
using NetFluss.Tray;
using Xunit;

namespace NetFluss.Tray.Tests;

/// <summary>
/// Asserts on the rendered pixels, because "is the 16 px meter legible" is not a question
/// a screenshot answers reliably and it is the one thing this component exists to get right.
/// </summary>
public class TrayMeterRendererTests
{
    /// <summary>Tray icon edge lengths at 100%, 125%, 150% and 200% display scaling.</summary>
    public static TheoryData<int> IconSizes() => new() { 16, 20, 24, 32 };

    private static TrayMeterOptions Options(int size, TrayMeterLayout layout = TrayMeterLayout.TwoLine) => new()
    {
        Size = size,
        Layout = layout,
        DownloadColor = ThemeColor.FromHex("4CC2FF"),
        UploadColor = ThemeColor.FromHex("6CCB5F"),
        TaskbarBackground = ThemeColor.FromHex("202020"),
    };

    [Theory]
    [MemberData(nameof(IconSizes))]
    public void Renders_AtTheRequestedSize(int size)
    {
        using var bitmap = new TrayMeterRenderer().RenderBitmap(new RateTotals(834_000, 41_000), Options(size));

        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
    }

    [Theory]
    [MemberData(nameof(IconSizes))]
    public void Renders_SomethingVisible(int size)
    {
        using var bitmap = new TrayMeterRenderer().RenderBitmap(new RateTotals(834_000, 41_000), Options(size));

        Assert.True(InkPixels(bitmap) > 0, $"the {size}px meter drew nothing at all");
    }

    /// <summary>
    /// The core promise of <see cref="PixelFont"/>, stated in the only way that cannot
    /// flatter itself: where the bitmap path is used, every pixel it draws is fully opaque.
    /// A single partially-transparent pixel means something got anti-aliased or landed off
    /// the grid, which is the blur this whole mechanism exists to remove.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    public void BitmapFontRows_AreFullyOpaque(int size)
    {
        var renderer = new TrayMeterRenderer();
        var options = Options(size);
        var totals = new RateTotals(834_000, 41_000);

        // Guard the premise: if the renderer ever stops choosing the bitmap path here, this
        // test would pass while asserting nothing.
        var plan = renderer.PlanRow(
            [
                RateFormatter.FormatCompact(totals.RxRateBps, options.UseBits),
                RateFormatter.FormatCompact(totals.TxRateBps, options.UseBits),
            ],
            size,
            options.Layout,
            options);

        Assert.True(plan.UsesPixelFont, $"expected the bitmap font at {size}px, got {plan}");

        using var bitmap = renderer.RenderBitmap(totals, options);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var alpha = bitmap.GetPixel(x, y).A;
                Assert.True(
                    alpha is 0 or 255,
                    $"{size}px meter has a {alpha}/255 alpha pixel at ({x},{y}) — that is a soft edge");
            }
        }
    }

    /// <summary>
    /// Segoe UI should still be doing the work where it has the pixels for it — 150% and
    /// 200% displays were never the problem, and a bitmap face scaled up looks cruder than
    /// properly hinted type.
    /// </summary>
    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    public void LargeIcons_StayOnTrueType(int size)
    {
        var options = Options(size);
        var plan = new TrayMeterRenderer().PlanRow(["834K", "41K"], size, options.Layout, options);

        Assert.False(plan.UsesPixelFont, $"expected Segoe UI at {size}px, got {plan}");
    }

    /// <summary>
    /// Every layout must draw at every size. A layout that renders empty would show as a
    /// blank tray icon, which reads as "the app has crashed".
    /// </summary>
    [Theory]
    [MemberData(nameof(IconSizes))]
    public void EveryLayout_DrawsInk(int size)
    {
        var renderer = new TrayMeterRenderer();

        foreach (var layout in Enum.GetValues<TrayMeterLayout>())
        {
            using var bitmap = renderer.RenderBitmap(new RateTotals(4_720_000, 96_000), Options(size, layout));
            Assert.True(InkPixels(bitmap) > 0, $"{layout} drew nothing at {size}px");
        }
    }

    /// <summary>Idle is the state the icon spends most of its life in; "0/0" must still show.</summary>
    [Theory]
    [MemberData(nameof(IconSizes))]
    public void IdleMeter_StillDrawsZeroes(int size)
    {
        using var bitmap = new TrayMeterRenderer().RenderBitmap(RateTotals.Zero, Options(size));

        Assert.True(InkPixels(bitmap) > 0, $"the idle {size}px meter is blank");
    }

    /// <summary>
    /// Both rows must clear the contrast floor against the taskbar they are drawn on. This
    /// is the Phase 0 finding — green at 3.04:1 beside blue at 4.08:1 on the light taskbar —
    /// turned into something that fails the build instead of being noticed in a screenshot.
    /// </summary>
    [Theory]
    [InlineData("F3F3F3")]
    [InlineData("202020")]
    public void BothRows_ClearTheContrastFloor(string taskbarHex)
    {
        var taskbar = ThemeColor.FromHex(taskbarHex);
        var options = Options(32) with
        {
            DownloadColor = AppTheme.System.DownloadColor,
            UploadColor = AppTheme.System.UploadColor,
            TaskbarBackground = taskbar,
        };

        using var bitmap = new TrayMeterRenderer().RenderBitmap(new RateTotals(834_000, 41_000), options);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 255)
                {
                    // Anti-aliased edges blend toward the background by design.
                    continue;
                }

                var ratio = Contrast.Ratio(new ThemeColor(pixel.R, pixel.G, pixel.B), taskbar);
                Assert.True(
                    ratio >= Contrast.MinimumReadableRatio - 0.01,
                    $"pixel at ({x},{y}) is {ratio:N2}:1 against #{taskbarHex}");
            }
        }
    }

    /// <summary>
    /// Regression: the style is resolved once per icon, so it has to fit *both* rows.
    /// Sizing it to whichever label Segoe measured widest clipped "118M" to ".18M" at
    /// 16 px, because Segoe and the bitmap face disagree about whether "118M" or "2.4M" is
    /// the wider string — the bitmap decimal point is one column and Segoe's is not.
    /// </summary>
    [Theory]
    [MemberData(nameof(IconSizes))]
    public void EveryRow_FitsTheIcon(int size)
    {
        var renderer = new TrayMeterRenderer();
        var options = Options(size);

        // Deliberately includes pairs whose widths rank differently in the two fonts.
        (double Rx, double Tx)[] samples =
        [
            (0, 0),
            (834_000, 41_000),
            (4_720_000, 96_000),
            (118_000_000, 2_400_000),
            (2_400_000, 118_000_000),
            (999_000_000, 999_000_000),
        ];

        foreach (var (rx, tx) in samples)
        {
            string[] labels =
            [
                RateFormatter.FormatCompact(rx, options.UseBits),
                RateFormatter.FormatCompact(tx, options.UseBits),
            ];

            var plan = renderer.PlanRow(labels, size, options.Layout, options);
            if (!plan.UsesPixelFont)
            {
                continue;
            }

            var face = plan.GlyphHeight == PixelFont.Small.GlyphHeight ? PixelFont.Small : PixelFont.Medium;

            foreach (var label in labels)
            {
                Assert.True(
                    face.Measure(label) * plan.Scale <= size,
                    $"'{label}' needs {face.Measure(label) * plan.Scale}px of a {size}px icon " +
                    $"under the plan chosen for [{string.Join(", ", labels)}] ({plan})");
            }
        }
    }

    private static int InkPixels(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
