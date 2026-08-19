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
using System.Drawing.Imaging;
using System.Drawing.Text;
using NetFluss.Core;
using NetFluss.Tray;

namespace NetFluss.TrayPreview;

/// <summary>
/// Renders every tray layout at every DPI scale onto both a light and a dark taskbar
/// swatch, at 1× and at 6× magnification, and writes a single PNG.
///
/// Phase 0 of the Windows port exists to answer one question — is a 16 px tray icon
/// legible enough to be the default, or does the taskbar-overlay window have to be
/// first-class? This produces the evidence.
/// </summary>
internal static class Program
{
    /// <summary>Tray icon edge lengths at 100%, 125%, 150% and 200% display scaling.</summary>
    private static readonly int[] IconSizes = [16, 20, 24, 32];

    private static readonly (string Label, double Rx, double Tx)[] Samples =
    [
        ("idle", 0, 0),
        ("browsing", 834_000, 41_200),
        ("streaming", 4_720_000, 96_000),
        ("gigabit", 118_000_000, 2_400_000),
    ];

    private static readonly Color LightTaskbar = Color.FromArgb(243, 243, 243);
    private static readonly Color DarkTaskbar = Color.FromArgb(32, 32, 32);

    private const int Magnification = 6;
    private const int Padding = 14;
    private const int HeaderHeight = 26;
    private const int RowLabelWidth = 150;

    private static int Main(string[] args)
    {
        var outputPath = args.Length > 0
            ? args[0]
            : Path.Combine(Environment.CurrentDirectory, "tray-contact-sheet.png");

        var renderer = new TrayMeterRenderer();
        var rows = BuildRows();

        var cellWidth = (IconSizes.Max() * Magnification) + Padding;
        var cellHeight = (IconSizes.Max() * Magnification) + Padding;
        var width = RowLabelWidth + (rows.Columns.Count * cellWidth) + Padding;
        var height = HeaderHeight + (rows.Rows.Count * cellHeight) + Padding;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(24, 24, 27));
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using var labelFont = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var headerFont = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelBrush = new SolidBrush(Color.FromArgb(200, 200, 205));

        for (var c = 0; c < rows.Columns.Count; c++)
        {
            var x = RowLabelWidth + (c * cellWidth);
            g.DrawString(rows.Columns[c], headerFont, labelBrush, x, 6);
        }

        for (var r = 0; r < rows.Rows.Count; r++)
        {
            var row = rows.Rows[r];
            var y = HeaderHeight + (r * cellHeight);
            g.DrawString(row.Label, labelFont, labelBrush, 6, y + (cellHeight / 2f) - 6);

            for (var c = 0; c < rows.Columns.Count; c++)
            {
                var size = IconSizes[c];
                var x = RowLabelWidth + (c * cellWidth);

                var options = row.Options with { Size = size };
                using var icon = renderer.RenderBitmap(new RateTotals(row.Rx, row.Tx), options);

                var scaled = size * Magnification;
                var cellX = x + ((cellWidth - Padding - scaled) / 2f);
                var cellY = y + ((cellHeight - Padding - scaled) / 2f);

                // Taskbar swatch behind the icon: the meter must stay legible on both.
                using var background = new SolidBrush(row.Dark ? DarkTaskbar : LightTaskbar);
                g.FillRectangle(background, cellX, cellY, scaled, scaled);
                g.DrawImage(icon, cellX, cellY, scaled, scaled);

                // 1:1 inset so the actual on-screen appearance is visible next to the blow-up.
                using var background1X = new SolidBrush(row.Dark ? DarkTaskbar : LightTaskbar);
                g.FillRectangle(background1X, cellX, cellY + scaled + 2, size, size);
                g.DrawImage(icon, cellX, cellY + scaled + 2, size, size);

                // Which path the renderer took, so the sheet says what it did instead of
                // leaving it to be guessed from a 6× blow-up.
                if (row.Options.Layout != TrayMeterLayout.Icon)
                {
                    string[] labels = options.Layout == TrayMeterLayout.TwoLine
                        ? [Label(row.Rx, '↓', options), Label(row.Tx, '↑', options)]
                        : [Label(row.Rx, '↓', options)];

                    var plan = renderer.PlanRow(labels, size, options.Layout, options);
                    g.DrawString(plan.ToString(), labelFont, labelBrush, cellX + size + 6, cellY + scaled + 2);
                }
            }
        }

        sheet.Save(outputPath, ImageFormat.Png);
        Console.WriteLine($"Wrote {outputPath} ({width}x{height})");
        return 0;
    }

    private static string Label(double rate, char arrow, TrayMeterOptions options)
    {
        var text = RateFormatter.FormatCompact(rate, options.UseBits);
        return options.ShowArrows ? string.Concat(arrow, text) : text;
    }

    private static (List<string> Columns, List<Row> Rows) BuildRows()
    {
        var columns = IconSizes.Select(size => $"{size}px  ({size * 100 / 16}%)").ToList();
        var rows = new List<Row>();

        foreach (var dark in new[] { true, false })
        {
            foreach (var (label, rx, tx) in Samples)
            {
                rows.Add(new Row(
                    $"TwoLine · {label} · {(dark ? "dark" : "light")}",
                    BaseOptions(dark) with { Layout = TrayMeterLayout.TwoLine },
                    rx,
                    tx,
                    dark));
            }
        }

        rows.Add(new Row("TwoLine · arrows · dark", BaseOptions(true) with { Layout = TrayMeterLayout.TwoLine, ShowArrows = true }, 4_720_000, 96_000, true));
        rows.Add(new Row("DownloadOnly · dark", BaseOptions(true) with { Layout = TrayMeterLayout.DownloadOnly }, 4_720_000, 96_000, true));
        rows.Add(new Row("TwoLine · bits · dark", BaseOptions(true) with { Layout = TrayMeterLayout.TwoLine, UseBits = true }, 4_720_000, 96_000, true));
        // One row per glyph: these are drawn geometry rather than raster assets, so the only
        // way to know they hold up at 16 px is to render them at 16 px and look.
        foreach (var glyph in TrayGlyphLibrary.Options)
        {
            rows.Add(new Row(
                $"Icon · {glyph.Label} · dark",
                BaseOptions(true) with { Layout = TrayMeterLayout.Icon, IconGlyph = glyph.Id },
                0,
                0,
                true));
        }

        return (columns, rows);
    }

    private static TrayMeterOptions BaseOptions(bool dark) => new()
    {
        Size = 16,
        // On a dark taskbar the macOS "system" colour resolves to near-white; the accent
        // colours below are the Windows equivalents of .systemBlue / .systemGreen.
        DownloadColor = dark ? ThemeColor.FromHex("4cc2ff") : AppTheme.System.DownloadColor,
        UploadColor = dark ? ThemeColor.FromHex("6ccb5f") : AppTheme.System.UploadColor,

        // Same swatch the cell is painted on, so the sheet shows the contrast correction
        // the app will actually apply rather than the raw configured colour.
        TaskbarBackground = dark
            ? ThemeColor.FromHex("202020")
            : ThemeColor.FromHex("F3F3F3"),
    };

    private sealed record Row(string Label, TrayMeterOptions Options, double Rx, double Tx, bool Dark);
}
