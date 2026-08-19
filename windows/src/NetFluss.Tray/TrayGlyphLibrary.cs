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

/// <summary>One entry in the tray glyph picker.</summary>
public sealed record TrayGlyphOption(string Id, string Label);

/// <summary>
/// The glyphs offered for <see cref="TrayMeterLayout.Icon"/>, porting the macOS
/// <c>MenuBarIconLibrary</c> choice list.
///
/// <para><b>Drawn, not shipped as image files.</b> The macOS side uses SF Symbols, whose
/// licence does not permit them off Apple platforms, and the obvious substitute — a set of
/// PNGs or a multi-resolution .ico — would have to guess at sizes. The tray asks for a new
/// bitmap at 16, 20, 24 or 32 px depending on the display, plus whatever a future scaling
/// factor invents, and a raster asset is crisp at exactly the sizes it was authored for and
/// soft everywhere else.</para>
///
/// <para>These are drawn as geometry against the requested box instead, so every size is
/// authored. Stroke widths are snapped to whole pixels for the same reason
/// <see cref="PixelFont"/> exists: a 1.5 px stroke is two grey pixels, not one dark one.</para>
/// </summary>
public static class TrayGlyphLibrary
{
    /// <summary>Matches the macOS list, minus the SF Symbols that cannot cross over.</summary>
    public static readonly IReadOnlyList<TrayGlyphOption> Options =
    [
        new("netfluss", "NetFluss"),
        new("arrows", "Arrows"),
        new("network", "Network"),
        new("wifi", "Wi-Fi"),
        new("antenna", "Antenna"),
    ];

    public static bool IsSupported(string id) => Options.Any(option => option.Id == id);

    /// <summary>Falls back to the house glyph rather than drawing nothing for a stale id.</summary>
    public static string Normalize(string id) => IsSupported(id) ? id : "netfluss";

    internal static void Draw(Graphics g, string id, int size, Color download, Color upload)
    {
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            switch (Normalize(id))
            {
                case "arrows":
                    DrawArrows(g, size, download, upload);
                    break;
                case "network":
                    DrawNetwork(g, size, download);
                    break;
                case "wifi":
                    DrawWifi(g, size, download);
                    break;
                case "antenna":
                    DrawAntenna(g, size, download, upload);
                    break;
                default:
                    DrawNetfluss(g, size, download, upload);
                    break;
            }
        }
        finally
        {
            g.SmoothingMode = previous;
        }
    }

    /// <summary>Whole-pixel stroke, never thinner than one pixel.</summary>
    private static float Stroke(int size) => Math.Max(1f, (float)Math.Round(size / 11f));

    /// <summary>
    /// The house glyph: two chevrons pointing apart, the shape the meter itself draws with.
    /// Reads as throughput at 16 px, where anything more literal turns to porridge.
    /// </summary>
    private static void DrawNetfluss(Graphics g, int size, Color download, Color upload)
    {
        var thickness = Stroke(size);
        var inset = size * 0.16f;
        var mid = size / 2f;

        using var down = new Pen(download, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var up = new Pen(upload, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        var leftCentre = size * 0.29f;
        var rightCentre = size * 0.71f;
        var arm = size * 0.15f;

        // Down on the left, up on the right, each a stem plus a head.
        g.DrawLine(down, leftCentre, inset, leftCentre, size - inset);
        g.DrawLines(down,
        [
            new PointF(leftCentre - arm, size - inset - arm),
            new PointF(leftCentre, size - inset),
            new PointF(leftCentre + arm, size - inset - arm),
        ]);

        g.DrawLine(up, rightCentre, size - inset, rightCentre, inset);
        g.DrawLines(up,
        [
            new PointF(rightCentre - arm, inset + arm),
            new PointF(rightCentre, inset),
            new PointF(rightCentre + arm, inset + arm),
        ]);

        _ = mid;
    }

    /// <summary>
    /// Solid triangles, the macOS <c>arrow.up.arrow.down</c> equivalent.
    ///
    /// <para>Deliberately not the same drawing as <see cref="DrawNetfluss"/> with a different
    /// name. Filled shapes hold their weight at 16 px where a stroked outline starts to thin
    /// out, so this is the choice for anyone who finds the house glyph too faint — which is
    /// the only reason to offer a second arrow icon at all.</para>
    /// </summary>
    private static void DrawArrows(Graphics g, int size, Color download, Color upload)
    {
        var inset = size * 0.14f;
        var span = size - (inset * 2);
        var half = span / 2.4f;

        using var down = new SolidBrush(download);
        using var up = new SolidBrush(upload);

        var leftCentre = size * 0.29f;
        var rightCentre = size * 0.71f;

        g.FillPolygon(down,
        [
            new PointF(leftCentre - (half / 2), inset),
            new PointF(leftCentre + (half / 2), inset),
            new PointF(leftCentre, size - inset),
        ]);

        g.FillPolygon(up,
        [
            new PointF(rightCentre - (half / 2), size - inset),
            new PointF(rightCentre + (half / 2), size - inset),
            new PointF(rightCentre, inset),
        ]);
    }

    /// <summary>A globe-ish ring with a meridian: the macOS <c>network</c> symbol.</summary>
    private static void DrawNetwork(Graphics g, int size, Color ink)
    {
        var thickness = Stroke(size);
        var inset = thickness + (size * 0.08f);
        var box = new RectangleF(inset, inset, size - (inset * 2), size - (inset * 2));

        using var pen = new Pen(ink, thickness);
        g.DrawEllipse(pen, box);
        g.DrawLine(pen, box.Left, box.Top + (box.Height / 2), box.Right, box.Top + (box.Height / 2));
        g.DrawEllipse(pen, box.Left + (box.Width * 0.28f), box.Top, box.Width * 0.44f, box.Height);
    }

    /// <summary>Three arcs and a dot.</summary>
    private static void DrawWifi(Graphics g, int size, Color ink)
    {
        var thickness = Stroke(size);
        using var pen = new Pen(ink, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var centreX = size / 2f;
        var baseY = size * 0.78f;

        for (var ring = 0; ring < 3; ring++)
        {
            var radius = size * (0.18f + (ring * 0.16f));
            g.DrawArc(pen, centreX - radius, baseY - radius, radius * 2, radius * 2, 215, 110);
        }

        var dot = Math.Max(1.5f, size * 0.09f);
        using var brush = new SolidBrush(ink);
        g.FillEllipse(brush, centreX - (dot / 2), baseY - (dot / 2), dot, dot);
    }

    /// <summary>A mast with waves either side.</summary>
    private static void DrawAntenna(Graphics g, int size, Color download, Color upload)
    {
        var thickness = Stroke(size);
        using var mast = new Pen(download, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var wave = new Pen(upload, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var centreX = size / 2f;
        g.DrawLine(mast, centreX, size * 0.32f, centreX, size * 0.86f);

        for (var ring = 1; ring <= 2; ring++)
        {
            var radius = size * (0.14f + (ring * 0.11f));
            var top = (size * 0.36f) - radius;
            g.DrawArc(wave, centreX - radius, top, radius * 2, radius * 2, 200, 140);
        }
    }
}
