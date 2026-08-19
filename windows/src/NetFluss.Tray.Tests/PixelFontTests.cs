// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Tray;
using Xunit;

namespace NetFluss.Tray.Tests;

/// <summary>
/// The glyph tables are hand-typed ASCII art, which is exactly the kind of data where a
/// single stray character produces a glyph that is one column short and shifts everything
/// after it — visible only as a slightly wrong-looking digit in a 16 px icon. These assert
/// the shape of the tables rather than trusting them.
/// </summary>
public class PixelFontTests
{
    public static TheoryData<string> Faces() => new() { "Small", "Medium" };

    private static PixelFont Face(string name) => name == "Small" ? PixelFont.Small : PixelFont.Medium;

    /// <summary>Every rate label the meter can produce is made of these.</summary>
    private const string RequiredCharacters = "0123456789.BbKMGT";

    [Theory]
    [MemberData(nameof(Faces))]
    public void EveryGlyph_HasExactlyGlyphHeightRows(string faceName)
    {
        var face = Face(faceName);

        foreach (var c in RequiredCharacters + "↓↑")
        {
            var rows = face.Rows(c);
            Assert.True(rows is not null, $"'{c}' is missing from the {faceName} face");
            Assert.Equal(face.GlyphHeight, rows!.Length);
        }
    }

    [Theory]
    [MemberData(nameof(Faces))]
    public void EveryGlyph_IsRectangular(string faceName)
    {
        var face = Face(faceName);

        foreach (var c in RequiredCharacters + "↓↑")
        {
            var rows = face.Rows(c)!;
            var width = rows[0].Length;

            for (var i = 0; i < rows.Length; i++)
            {
                Assert.True(
                    rows[i].Length == width,
                    $"{faceName} '{c}' row {i} is {rows[i].Length} wide, expected {width}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Faces))]
    public void EveryGlyph_UsesOnlyInkAndBlank(string faceName)
    {
        var face = Face(faceName);

        foreach (var c in RequiredCharacters + "↓↑")
        {
            foreach (var row in face.Rows(c)!)
            {
                Assert.All(row.ToCharArray(), pixel => Assert.True(
                    pixel is '#' or '.',
                    $"{faceName} '{c}' contains '{pixel}', which is neither ink nor blank"));
            }
        }
    }

    /// <summary>A blank glyph would silently render as a gap in the middle of a number.</summary>
    [Theory]
    [MemberData(nameof(Faces))]
    public void EveryGlyph_HasInk(string faceName)
    {
        var face = Face(faceName);

        foreach (var c in RequiredCharacters + "↓↑")
        {
            Assert.Contains(face.Rows(c)!, row => row.Contains('#'));
        }
    }

    /// <summary>
    /// Digits must be visually distinct: two that share a bitmap would show the wrong
    /// number rather than a wrong-looking one, which is far worse and far easier to miss.
    /// </summary>
    [Theory]
    [MemberData(nameof(Faces))]
    public void EveryDigit_IsDistinct(string faceName)
    {
        var face = Face(faceName);
        var seen = new Dictionary<string, char>(StringComparer.Ordinal);

        foreach (var c in "0123456789")
        {
            var key = string.Join('/', face.Rows(c)!);
            Assert.False(
                seen.TryGetValue(key, out var other),
                $"{faceName} draws '{c}' and '{other}' identically");

            seen[key] = c;
        }
    }

    /// <summary>
    /// The whole point of integer scaling: text can never be laid out wider than the box it
    /// was fitted to, so the right-aligned origin can never go negative and clip the left.
    /// </summary>
    [Theory]
    [MemberData(nameof(Faces))]
    public void FittingScale_NeverOverflowsTheBox(string faceName)
    {
        var face = Face(faceName);
        string[] labels = ["0", "41K", "834K", "4.7M", "118M", "768K", "2.4M", "↓4.7M"];

        foreach (var label in labels)
        {
            foreach (var size in new[] { 16, 20, 24, 32, 40 })
            {
                foreach (var height in new[] { size / 2f, size })
                {
                    var scale = face.FittingScale(label, size, height);
                    if (scale == 0)
                    {
                        continue;
                    }

                    Assert.True(
                        face.Measure(label) * scale <= size,
                        $"{faceName} '{label}' at {size}px scale {scale} is wider than the icon");

                    Assert.True(
                        face.GlyphHeight * scale <= height,
                        $"{faceName} '{label}' at {size}px scale {scale} is taller than its row");
                }
            }
        }
    }

    /// <summary>"834K" fitting a 16 px icon at all is the reason the 3×5 face exists.</summary>
    [Fact]
    public void SmallFace_FitsFourGlyphsIn16Pixels()
    {
        Assert.Equal(15, PixelFont.Small.Measure("834K"));
        Assert.Equal(1, PixelFont.Small.FittingScale("834K", 16, 8));
    }

    [Fact]
    public void MediumFace_IsTooWideForFourGlyphsIn16Pixels()
        => Assert.Equal(0, PixelFont.Medium.FittingScale("834K", 16, 8));

    [Fact]
    public void CanRender_RejectsCharactersOutsideTheTables()
    {
        Assert.True(PixelFont.Small.CanRender("118M"));
        Assert.False(PixelFont.Small.CanRender("118 MB/s"));
    }
}
