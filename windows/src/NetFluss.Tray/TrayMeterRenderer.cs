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

namespace NetFluss.Tray;

/// <summary>
/// Draws the live rates into a notification-area icon bitmap.
///
/// <para>This is NetFluss's answer to "there is no Windows menu bar". The DeskBand API
/// that NetSpeedMonitor and DU Meter used was removed in Windows 11 with no replacement,
/// so the tray icon is the only placement that is guaranteed to keep working.</para>
///
/// <para><b>Handle discipline:</b> <see cref="Bitmap.GetHicon"/> allocates a GDI icon
/// that the GC will not release. At one tick per second that is ~86k leaked handles a day
/// and a hard crash at the 10k per-process GDI limit. <see cref="RenderIconHandle"/>
/// therefore returns the raw handle and the caller must destroy the *previous* one after
/// assigning the new one — see <c>TrayIconHost</c>.</para>
/// </summary>
public sealed class TrayMeterRenderer
{
    /// <summary>Below this the glyphs stop resolving at all; clamp rather than draw mush.</summary>
    private const float MinimumFontPixelSize = 5.5f;

    private static readonly StringFormat TextFormat = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        Alignment = StringAlignment.Far,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.None,
    };

    /// <summary>
    /// Renders to a bitmap. Used directly by the preview tool; the app goes through
    /// <see cref="RenderIconHandle"/>.
    /// </summary>
    public Bitmap RenderBitmap(RateTotals totals, TrayMeterOptions options)
    {
        var size = Math.Max(8, options.Size);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // ClearType cannot composite onto a transparent surface — it would bake the
        // taskbar's assumed background into the glyph edges. Grid-fit greyscale AA is
        // the only hinting mode that stays crisp and stays transparent.
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        switch (options.Layout)
        {
            case TrayMeterLayout.TwoLine:
                DrawTwoLine(g, size, totals, options);
                break;
            case TrayMeterLayout.DownloadOnly:
                DrawSingleLine(g, size, totals.RxRateBps, options.DownloadColor, '↓', options);
                break;
            case TrayMeterLayout.UploadOnly:
                DrawSingleLine(g, size, totals.TxRateBps, options.UploadColor, '↑', options);
                break;
            case TrayMeterLayout.Icon:
                DrawGlyph(g, size, options);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Layout, "Unknown tray meter layout.");
        }

        return bitmap;
    }

    /// <summary>
    /// Renders and hands back an unmanaged HICON. The caller owns it and must pass it to
    /// <c>DestroyIcon</c> once it has been replaced.
    /// </summary>
    public nint RenderIconHandle(RateTotals totals, TrayMeterOptions options)
    {
        using var bitmap = RenderBitmap(totals, options);
        return bitmap.GetHicon();
    }

    private void DrawTwoLine(Graphics g, int size, RateTotals totals, TrayMeterOptions options)
    {
        var lineHeight = size / 2f;

        DrawRow(
            g,
            new RectangleF(0, 0, size, lineHeight),
            RateFormatter.FormatCompact(totals.RxRateBps, options.UseBits),
            options.DownloadColor,
            options.ShowArrows ? '↓' : (char?)null,
            options);

        DrawRow(
            g,
            new RectangleF(0, lineHeight, size, lineHeight),
            RateFormatter.FormatCompact(totals.TxRateBps, options.UseBits),
            options.UploadColor,
            options.ShowArrows ? '↑' : (char?)null,
            options);
    }

    private void DrawSingleLine(Graphics g, int size, double rate, ThemeColor color, char arrow, TrayMeterOptions options)
        => DrawRow(
            g,
            new RectangleF(0, 0, size, size),
            RateFormatter.FormatCompact(rate, options.UseBits),
            color,
            options.ShowArrows ? arrow : (char?)null,
            options);

    private void DrawRow(Graphics g, RectangleF bounds, string text, ThemeColor color, char? arrow, TrayMeterOptions options)
    {
        var label = arrow is { } glyph ? string.Concat(glyph, text) : text;

        using var font = FitFont(g, label, bounds, options.FontFamily);
        using var brush = new SolidBrush(ToGdi(color));

        // Right-aligned: the unit suffix stays pinned to the same column as the number
        // grows and shrinks, so the icon does not visibly jitter every tick.
        g.DrawString(label, font, brush, bounds, TextFormat);
    }

    /// <summary>
    /// Shrinks the font until the label fits the row. Necessary because "834K" and "1.2M"
    /// differ in width and a fixed size would clip the wider one at 16 px.
    /// </summary>
    private static Font FitFont(Graphics g, string text, RectangleF bounds, string fontFamily)
    {
        var pixelSize = bounds.Height;

        while (true)
        {
            var font = new Font(fontFamily, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            var measured = g.MeasureString(text, font, PointF.Empty, TextFormat);

            if (measured.Width <= bounds.Width || pixelSize <= MinimumFontPixelSize)
            {
                return font;
            }

            font.Dispose();
            pixelSize -= 0.25f;
        }
    }

    /// <summary>Static up/down chevrons for <see cref="TrayMeterLayout.Icon"/>.</summary>
    private static void DrawGlyph(Graphics g, int size, TrayMeterOptions options)
    {
        var thickness = Math.Max(1f, size / 10f);
        var inset = size * 0.18f;
        var mid = size / 2f;

        using var download = new Pen(ToGdi(options.DownloadColor), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var upload = new Pen(ToGdi(options.UploadColor), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var left = inset;
        var right = mid - (thickness / 2f);
        g.DrawLine(download, left + ((right - left) / 2f), inset, left + ((right - left) / 2f), size - inset);
        g.DrawLines(download,
        [
            new PointF(left, size - inset - ((right - left) / 2f)),
            new PointF(left + ((right - left) / 2f), size - inset),
            new PointF(right, size - inset - ((right - left) / 2f)),
        ]);

        var left2 = mid + (thickness / 2f);
        var right2 = size - inset;
        g.DrawLine(upload, left2 + ((right2 - left2) / 2f), inset, left2 + ((right2 - left2) / 2f), size - inset);
        g.DrawLines(upload,
        [
            new PointF(left2, inset + ((right2 - left2) / 2f)),
            new PointF(left2 + ((right2 - left2) / 2f), inset),
            new PointF(right2, inset + ((right2 - left2) / 2f)),
        ]);
    }

    private static Color ToGdi(ThemeColor color) => Color.FromArgb(255, color.R, color.G, color.B);
}
