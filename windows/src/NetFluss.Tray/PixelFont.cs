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

using System.Drawing;
using System.Drawing.Drawing2D;

namespace NetFluss.Tray;

/// <summary>
/// A hand-drawn bitmap font for the notification-area meter.
///
/// <para><b>Why not just use Segoe UI.</b> The macOS menu bar gives the meter a strip
/// roughly 22 px tall and as wide as it likes, so "1.25 MB/s" renders at a comfortable
/// 11 px. A Windows tray icon is a 16 px *square* at 100% DPI. Fitting "834K" — four
/// glyphs — across 16 px forces Segoe UI down to about 6 px per row, below the size at
/// which TrueType hinting can hold a stem at one clean pixel. That is the grey mush the
/// Phase 0 contact sheet shows at 100%.</para>
///
/// <para><b>What this does instead.</b> Glyphs are painted as solid axis-aligned
/// rectangles with anti-aliasing off, so every stem is exactly one pixel and nothing is
/// ever resampled. Scaling is integer-only — a 2× glyph is perfectly square pixels —
/// because a fractional factor would reintroduce the blur this exists to avoid.</para>
///
/// <para>Two faces, because one grid cannot serve every icon size. <see cref="Small"/> is
/// as narrow as digits can get and is what makes four glyphs fit 16 px at all;
/// <see cref="Medium"/> has better-proportioned shapes and wins wherever the row is tall
/// enough and the text still fits. <c>TrayMeterRenderer</c> picks whichever yields the
/// most ink height, and defers to Segoe UI once there are enough pixels to hint with.</para>
/// </summary>
internal sealed class PixelFont
{
    /// <summary>Blank columns between glyphs, in unscaled pixels.</summary>
    internal const int Tracking = 1;

    private readonly Dictionary<char, string[]> _glyphs;

    private PixelFont(int glyphHeight, Dictionary<char, string[]> glyphs)
    {
        GlyphHeight = glyphHeight;
        _glyphs = glyphs;
    }

    /// <summary>Cell height of every glyph in this face. Widths vary per glyph.</summary>
    internal int GlyphHeight { get; }

    /// <summary>
    /// 3×5. The decimal point is one column wide, which is exactly what buys "4.7M" room
    /// to sit in the same box as "118M" without either needing a smaller scale.
    /// </summary>
    internal static readonly PixelFont Small = new(5, new Dictionary<char, string[]>
    {
        ['0'] = ["###", "#.#", "#.#", "#.#", "###"],
        ['1'] = [".#.", "##.", ".#.", ".#.", "###"],
        ['2'] = ["###", "..#", "###", "#..", "###"],
        ['3'] = ["###", "..#", "###", "..#", "###"],
        ['4'] = ["#.#", "#.#", "###", "..#", "..#"],
        ['5'] = ["###", "#..", "###", "..#", "###"],
        ['6'] = ["###", "#..", "###", "#.#", "###"],
        ['7'] = ["###", "..#", ".#.", ".#.", ".#."],
        ['8'] = ["###", "#.#", "###", "#.#", "###"],
        ['9'] = ["###", "#.#", "###", "..#", "###"],
        ['.'] = [".", ".", ".", ".", "#"],
        ['B'] = ["##.", "#.#", "##.", "#.#", "##."],
        ['b'] = ["#..", "#..", "##.", "#.#", "##."],
        ['K'] = ["#.#", "##.", "#..", "##.", "#.#"],
        ['M'] = ["#.#", "###", "###", "#.#", "#.#"],
        ['G'] = ["###", "#..", "#.#", "#.#", "###"],
        ['T'] = ["###", ".#.", ".#.", ".#.", ".#."],
        ['↓'] = [".#.", ".#.", ".#.", "###", ".#."],
        ['↑'] = [".#.", "###", ".#.", ".#.", ".#."],
    });

    /// <summary>
    /// 4×7. Closed counters and a proper diagonal on the 4, which the 3×5 face cannot
    /// afford. Used at 125% and wherever else the row is tall enough to take it.
    /// </summary>
    internal static readonly PixelFont Medium = new(7, new Dictionary<char, string[]>
    {
        ['0'] = [".##.", "#..#", "#..#", "#..#", "#..#", "#..#", ".##."],
        ['1'] = ["..#.", ".##.", "..#.", "..#.", "..#.", "..#.", ".###"],
        ['2'] = [".##.", "#..#", "...#", "..#.", ".#..", "#...", "####"],
        ['3'] = ["###.", "...#", "...#", ".##.", "...#", "...#", "###."],
        ['4'] = ["..#.", ".##.", "#.#.", "#.#.", "####", "..#.", "..#."],
        ['5'] = ["####", "#...", "###.", "...#", "...#", "#..#", ".##."],
        ['6'] = [".##.", "#...", "#...", "###.", "#..#", "#..#", ".##."],
        ['7'] = ["####", "...#", "...#", "..#.", "..#.", ".#..", ".#.."],
        ['8'] = [".##.", "#..#", "#..#", ".##.", "#..#", "#..#", ".##."],
        ['9'] = [".##.", "#..#", "#..#", ".###", "...#", "...#", ".##."],
        ['.'] = [".", ".", ".", ".", ".", ".", "#"],
        ['B'] = ["###.", "#..#", "#..#", "###.", "#..#", "#..#", "###."],
        ['b'] = ["#...", "#...", "#...", "###.", "#..#", "#..#", "###."],
        ['K'] = ["#..#", "#.#.", "##..", "#...", "##..", "#.#.", "#..#"],
        ['M'] = ["#..#", "####", "####", "#..#", "#..#", "#..#", "#..#"],
        ['G'] = [".##.", "#..#", "#...", "#.##", "#..#", "#..#", ".##."],
        ['T'] = ["####", ".#..", ".#..", ".#..", ".#..", ".#..", ".#.."],
        ['↓'] = [".#.", ".#.", ".#.", ".#.", "###", ".#.", "..."],
        ['↑'] = ["...", ".#.", "###", ".#.", ".#.", ".#.", ".#."],
    });

    /// <summary>The rows of one glyph, or null when this face cannot draw it.</summary>
    internal string[]? Rows(char c) => _glyphs.TryGetValue(c, out var glyph) ? glyph : null;

    /// <summary>True when every character of <paramref name="text"/> can be drawn.</summary>
    internal bool CanRender(string text)
    {
        foreach (var c in text)
        {
            if (!_glyphs.ContainsKey(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Width of <paramref name="text"/> in unscaled pixels, or 0 when empty.</summary>
    internal int Measure(string text)
    {
        var width = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!_glyphs.TryGetValue(text[i], out var glyph))
            {
                continue;
            }

            width += glyph[0].Length;
            if (i < text.Length - 1)
            {
                width += Tracking;
            }
        }

        return width;
    }

    /// <summary>
    /// Largest integer scale at which <paramref name="text"/> fits the given box, or 0 when
    /// it does not fit even at 1×.
    /// </summary>
    internal int FittingScale(string text, float boxWidth, float boxHeight)
    {
        if (!CanRender(text))
        {
            return 0;
        }

        var width = Measure(text);
        if (width <= 0)
        {
            return 0;
        }

        var byWidth = (int)(boxWidth / width);
        var byHeight = (int)(boxHeight / GlyphHeight);
        return Math.Max(0, Math.Min(byWidth, byHeight));
    }

    /// <summary>
    /// Paints <paramref name="text"/> with its top-left at (<paramref name="originX"/>,
    /// <paramref name="originY"/>), which must already be whole pixels — a half-pixel
    /// origin would smear every stem across two columns and undo the point of this class.
    /// </summary>
    internal void Draw(Graphics g, string text, int originX, int originY, int scale, Color color)
    {
        if (scale <= 0)
        {
            return;
        }

        var previousSmoothing = g.SmoothingMode;
        var previousOffset = g.PixelOffsetMode;

        // Solid rectangles on exact integer bounds: no anti-aliasing to soften an edge and
        // no half-pixel offset to shift one.
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;

        using var brush = new SolidBrush(color);

        try
        {
            var x = originX;
            foreach (var c in text)
            {
                if (!_glyphs.TryGetValue(c, out var glyph))
                {
                    continue;
                }

                for (var row = 0; row < glyph.Length; row++)
                {
                    var line = glyph[row];
                    var column = 0;

                    while (column < line.Length)
                    {
                        if (line[column] != '#')
                        {
                            column++;
                            continue;
                        }

                        // Coalesce a horizontal run into one rectangle. Abutting fills can
                        // leave a hairline seam at some scales; one rectangle cannot.
                        var start = column;
                        while (column < line.Length && line[column] == '#')
                        {
                            column++;
                        }

                        g.FillRectangle(
                            brush,
                            x + (start * scale),
                            originY + (row * scale),
                            (column - start) * scale,
                            scale);
                    }
                }

                x += (glyph[0].Length + Tracking) * scale;
            }
        }
        finally
        {
            g.SmoothingMode = previousSmoothing;
            g.PixelOffsetMode = previousOffset;
        }
    }
}
