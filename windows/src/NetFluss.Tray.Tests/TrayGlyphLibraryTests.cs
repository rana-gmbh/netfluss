// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Drawing;
using NetFluss.Core;
using NetFluss.Tray;
using Xunit;

namespace NetFluss.Tray.Tests;

/// <summary>
/// The glyphs are drawn geometry rather than raster assets, so they have to hold up at every
/// size the shell asks for rather than at the sizes someone happened to author.
/// </summary>
public class TrayGlyphLibraryTests
{
    public static TheoryData<string> AllGlyphs()
    {
        var data = new TheoryData<string>();
        foreach (var option in TrayGlyphLibrary.Options)
        {
            data.Add(option.Id);
        }

        return data;
    }

    private static TrayMeterOptions Options(int size, string glyph) => new()
    {
        Size = size,
        Layout = TrayMeterLayout.Icon,
        IconGlyph = glyph,
        DownloadColor = ThemeColor.FromHex("4CC2FF"),
        UploadColor = ThemeColor.FromHex("6CCB5F"),
        TaskbarBackground = ThemeColor.FromHex("202020"),
    };

    [Theory]
    [MemberData(nameof(AllGlyphs))]
    public void EveryGlyph_DrawsInkAtEverySize(string glyph)
    {
        var renderer = new TrayMeterRenderer();

        foreach (var size in new[] { 16, 20, 24, 32, 48 })
        {
            using var bitmap = renderer.RenderBitmap(RateTotals.Zero, Options(size, glyph));

            var ink = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).A > 0)
                    {
                        ink++;
                    }
                }
            }

            Assert.True(ink > 0, $"'{glyph}' drew nothing at {size}px");

            // A glyph covering nearly the whole box is a bug, not a design: it means a stroke
            // width or radius scaled wrong and filled the icon.
            Assert.True(
                ink < bitmap.Width * bitmap.Height * 0.9,
                $"'{glyph}' filled {ink} of {bitmap.Width * bitmap.Height} pixels at {size}px");
        }
    }

    /// <summary>
    /// Two entries that draw the same thing are not a choice. "NetFluss" and "Arrows" were
    /// literally the same method for a while, which is exactly the kind of thing that reads
    /// as fine in a picker and does nothing when selected.
    /// </summary>
    [Fact]
    public void EveryGlyph_LooksDifferentFromTheOthers()
    {
        var renderer = new TrayMeterRenderer();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var option in TrayGlyphLibrary.Options)
        {
            using var bitmap = renderer.RenderBitmap(RateTotals.Zero, Options(32, option.Id));

            var signature = new System.Text.StringBuilder();
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    signature.Append(bitmap.GetPixel(x, y).A > 128 ? '#' : '.');
                }
            }

            var key = signature.ToString();
            Assert.False(
                seen.TryGetValue(key, out var other),
                $"'{option.Id}' and '{other}' render identically");

            seen[key] = option.Id;
        }
    }

    [Fact]
    public void Normalize_FallsBackForAnUnknownId()
    {
        Assert.Equal("netfluss", TrayGlyphLibrary.Normalize("sf.symbol.that.cannot.ship"));
        Assert.Equal("wifi", TrayGlyphLibrary.Normalize("wifi"));
    }

    [Fact]
    public void Options_AreUniqueAndLabelled()
    {
        Assert.Equal(
            TrayGlyphLibrary.Options.Count,
            TrayGlyphLibrary.Options.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count());

        Assert.All(TrayGlyphLibrary.Options, option => Assert.False(string.IsNullOrWhiteSpace(option.Label)));
    }
}
