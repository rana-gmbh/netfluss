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
    private const int MinimumFontPixelSize = 6;

    /// <summary>
    /// Smallest Segoe UI size that still hints into clean one-pixel stems. Below it a
    /// digit's vertical stroke lands between two columns and greys across both, which is
    /// what made the 16 px meter unreadable — so at that point <see cref="PixelFont"/>
    /// takes over instead.
    /// </summary>
    private const int MinimumHintableFontSize = 9;

    /// <summary>
    /// Measured with Near alignment and drawn at an explicit integer origin: letting
    /// DrawString centre the text inside a RectangleF puts the baseline on a fractional
    /// pixel, and a half-pixel baseline blurs every glyph no matter how it is hinted.
    /// </summary>
    private static readonly StringFormat TextFormat = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
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
        var bounds = new RectangleF(0, 0, size, lineHeight);

        var download = Label(totals.RxRateBps, options.ShowArrows ? '↓' : null, options);
        var upload = Label(totals.TxRateBps, options.ShowArrows ? '↑' : null, options);

        // One decision for the whole icon. Resolving each row on its own lets a wide
        // "834K" fall to the bitmap font while a narrow "41K" stays on Segoe UI, and the
        // two typefaces stacked in a 16 px box look like a rendering fault.
        var style = ResolveStyle(g, [download, upload], bounds, options);

        DrawRow(g, bounds, download, options.DownloadColor, style, options);
        DrawRow(g, bounds with { Y = lineHeight }, upload, options.UploadColor, style, options);
    }

    private void DrawSingleLine(Graphics g, int size, double rate, ThemeColor color, char arrow, TrayMeterOptions options)
    {
        var bounds = new RectangleF(0, 0, size, size);
        var label = Label(rate, options.ShowArrows ? arrow : null, options);

        DrawRow(g, bounds, label, color, ResolveStyle(g, [label], bounds, options), options);
    }

    private static string Label(double rate, char? arrow, TrayMeterOptions options)
    {
        var text = RateFormatter.FormatCompact(rate, options.UseBits);
        return arrow is { } glyph ? string.Concat(glyph, text) : text;
    }

    /// <summary>
    /// Resolves one drawing style for every row of the icon, sized to whichever label needs
    /// the most room. Fitting to the widest means a row can never be clipped by a decision
    /// made for a different row.
    /// </summary>
    private static RowStyle ResolveStyle(Graphics g, string[] labels, RectangleF bounds, TrayMeterOptions options)
    {
        // Every label has to fit, so take the smallest size any of them needs rather than
        // sizing to one and hoping. Picking the "widest" label by measuring it in Segoe and
        // then drawing in a bitmap face is what clipped "118M" to ".18M" at 16 px: the two
        // fonts do not agree on which of "118M" and "2.4M" is wider, because the bitmap
        // decimal point is one column and Segoe's is not.
        var fontSize = float.MaxValue;
        foreach (var label in labels)
        {
            using var fitted = FitFont(g, label, bounds, options.FontFamily, out _);
            fontSize = Math.Min(fontSize, fitted.Size);
        }

        return ChoosePixelFace(labels, bounds, fontSize) is var (face, scale)
            ? RowStyle.Bitmap(face, scale)
            : RowStyle.TrueType(fontSize);
    }

    /// <summary>How every row of one icon is drawn: a bitmap face at a scale, or Segoe UI at a size.</summary>
    private readonly record struct RowStyle(PixelFont? Face, int Scale, float FontSize)
    {
        internal static RowStyle Bitmap(PixelFont face, int scale) => new(face, scale, 0);

        internal static RowStyle TrueType(float fontSize) => new(null, 0, fontSize);
    }

    /// <summary>
    /// Which path a row resolved to. Exposed so the preview tool can label every cell of
    /// the contact sheet with the decision that produced it — at 16 px the difference
    /// between a hinted Segoe row and a bitmap one is a couple of pixels of stem, which is
    /// not something to be judging by eye from a magnified screenshot.
    /// </summary>
    internal readonly record struct RowPlan(bool UsesPixelFont, int GlyphHeight, int Scale, float FontSize)
    {
        public override string ToString()
            => UsesPixelFont ? $"px {GlyphHeight}px ×{Scale}" : $"Segoe {FontSize:0}px";
    }

    /// <summary>
    /// Resolves an icon's style without drawing it, given the labels its rows will carry.
    /// Same decision <see cref="RenderBitmap"/> makes.
    /// </summary>
    internal RowPlan PlanRow(string[] labels, int size, TrayMeterLayout layout, TrayMeterOptions options)
    {
        using var probe = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(probe);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var bounds = layout == TrayMeterLayout.TwoLine
            ? new RectangleF(0, 0, size, size / 2f)
            : new RectangleF(0, 0, size, size);

        var style = ResolveStyle(g, labels, bounds, options);

        return style.Face is { } face
            ? new RowPlan(true, face.GlyphHeight, style.Scale, 0)
            : new RowPlan(false, 0, 0, style.FontSize);
    }

    private void DrawRow(Graphics g, RectangleF bounds, string label, ThemeColor color, RowStyle style, TrayMeterOptions options)
    {
        var ink = ToGdi(color, options);

        if (style.Face is { } face)
        {
            DrawPixelRow(g, face, label, bounds, style.Scale, ink);
            return;
        }

        using var font = new Font(options.FontFamily, style.FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ink);

        var measured = g.MeasureString(label, font, PointF.Empty, TextFormat);

        // Right-aligned so the unit suffix holds one column while the digits change width,
        // and rounded to whole pixels so the glyphs land on the grid they were hinted for.
        var x = (float)Math.Round(bounds.Right - measured.Width);
        var y = (float)Math.Round(bounds.Top + ((bounds.Height - measured.Height) / 2f));

        g.DrawString(label, font, brush, new PointF(x, y), TextFormat);
    }

    /// <summary>
    /// Picks the bitmap face to draw with, or null to stay on Segoe UI.
    ///
    /// <para>The bitmap font buys exactly one thing — a stem that is one solid pixel where
    /// TrueType would have to smear it across two — so it is used exactly where TrueType
    /// has run out of pixels to hint with, and nowhere else. An earlier version compared
    /// the two on ink height instead, which let a doubled 3×5 face out-score Segoe at
    /// 200%: taller, and visibly cruder. Above the threshold, real letterforms win.</para>
    ///
    /// <para>Of the faces that fit, the taller wins: it is the same sharpness either way,
    /// so the only question left is how much of the row the digits fill. "Fits" means every
    /// label fits — a face chosen for one row is what the other row is drawn in too.</para>
    /// </summary>
    private static (PixelFont Face, int Scale)? ChoosePixelFace(string[] labels, RectangleF bounds, float trueTypeSize)
    {
        if (trueTypeSize >= MinimumHintableFontSize)
        {
            return null;
        }

        foreach (var face in (PixelFont[])[PixelFont.Medium, PixelFont.Small])
        {
            var scale = int.MaxValue;
            foreach (var label in labels)
            {
                // Zero for a label this face cannot draw or cannot fit, which rules the
                // whole face out rather than clipping that one row.
                scale = Math.Min(scale, face.FittingScale(label, bounds.Width, bounds.Height));
            }

            if (scale > 0 && scale != int.MaxValue)
            {
                return (face, scale);
            }
        }

        return null;
    }

    private static void DrawPixelRow(Graphics g, PixelFont face, string label, RectangleF bounds, int scale, Color ink)
    {
        var width = face.Measure(label) * scale;
        var height = face.GlyphHeight * scale;

        face.Draw(
            g,
            label,
            (int)Math.Round(bounds.Right - width),
            (int)Math.Round(bounds.Top + ((bounds.Height - height) / 2f)),
            scale,
            ink);
    }

    /// <summary>
    /// Shrinks the font until the label fits the row. Necessary because "834K" and "1.2M"
    /// differ in width and a fixed size would clip the wider one at 16 px.
    ///
    /// <para>Sizes step by whole pixels. Fractional sizes put stems between columns, and
    /// the quarter-pixel search this replaces was a large part of why the small icons
    /// looked soft even before the font ran out of room.</para>
    /// </summary>
    private static Font FitFont(Graphics g, string text, RectangleF bounds, string fontFamily, out SizeF measured)
    {
        var pixelSize = Math.Max(MinimumFontPixelSize, (int)bounds.Height);

        while (true)
        {
            var font = new Font(fontFamily, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            measured = g.MeasureString(text, font, PointF.Empty, TextFormat);

            if (measured.Width <= bounds.Width || pixelSize <= MinimumFontPixelSize)
            {
                return font;
            }

            font.Dispose();
            pixelSize -= 1;
        }
    }

    /// <summary>Static up/down chevrons for <see cref="TrayMeterLayout.Icon"/>.</summary>
    private static void DrawGlyph(Graphics g, int size, TrayMeterOptions options)
    {
        var thickness = Math.Max(1f, size / 10f);
        var inset = size * 0.18f;
        var mid = size / 2f;

        using var download = new Pen(ToGdi(options.DownloadColor, options), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var upload = new Pen(ToGdi(options.UploadColor, options), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };

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

    /// <summary>
    /// Converts to GDI, first lifting the colour to the configured contrast floor against
    /// the taskbar it will be composited over. Applied to both rows from the one place, so
    /// download and upload can never drift to different legibility the way they did at
    /// #0078D4 (4.08:1) against #2EA043 (3.04:1) on the light taskbar.
    /// </summary>
    private static Color ToGdi(ThemeColor color, TrayMeterOptions options)
    {
        var adjusted = options.MinimumContrastRatio > 0
            ? Contrast.EnsureRatio(color, options.TaskbarBackground, options.MinimumContrastRatio)
            : color;

        return Color.FromArgb(255, adjusted.R, adjusted.G, adjusted.B);
    }
}
