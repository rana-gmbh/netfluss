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

namespace NetFluss.Core;

/// <summary>
/// WCAG relative luminance and contrast, used to keep the tray meter legible on whichever
/// taskbar it lands on.
///
/// <para><b>Why this exists.</b> macOS renders the menu bar meter in a template colour the
/// system inverts for you, so a single pair of colours works on any wallpaper. Windows does
/// no such thing — the tray icon is composited as-is over a taskbar that is near-black or
/// near-white depending on a user setting. The Phase 0 contact sheet caught the
/// consequence: NetFluss green (#2EA043) scores 3.04:1 on a light taskbar while the blue
/// (#0078D4) scores 4.08:1, so the upload row reads visibly weaker than the download row —
/// the two rows disagree about how important they are, which is not a decision the colour
/// should be making.</para>
///
/// <para>Both are darkened here until they clear the same bar, so the pair stays balanced
/// on either taskbar and the accent the user picked is still recognisably that accent.</para>
/// </summary>
public static class Contrast
{
    /// <summary>
    /// WCAG AA for body text. The meter's glyphs are small and thin, so the 3:1
    /// large-text allowance is the wrong one to borrow.
    /// </summary>
    public const double MinimumReadableRatio = 4.5;

    /// <summary>Relative luminance per WCAG 2.x, in 0…1.</summary>
    public static double RelativeLuminance(ThemeColor color)
        => (0.2126 * Linearize(color.R))
         + (0.7152 * Linearize(color.G))
         + (0.0722 * Linearize(color.B));

    /// <summary>Contrast ratio between two colours, from 1:1 (identical) to 21:1.</summary>
    public static double Ratio(ThemeColor a, ThemeColor b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (lighter, darker) = la >= lb ? (la, lb) : (lb, la);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>True when <paramref name="background"/> is the lighter of the two extremes.</summary>
    public static bool IsLight(ThemeColor background) => RelativeLuminance(background) > 0.5;

    /// <summary>
    /// Darkens (on a light background) or lightens (on a dark one) <paramref name="color"/>
    /// until it reaches <paramref name="targetRatio"/> against <paramref name="background"/>,
    /// returning it unchanged when it already clears the bar.
    ///
    /// <para>The walk is in 2% steps toward black or white and stops at whichever comes
    /// first, the target or the extreme. Stepping rather than solving keeps the hue: a
    /// green that has been darkened 20% is still the same green, where jumping straight to
    /// a computed luminance would land on an arbitrary colour the user never chose.</para>
    /// </summary>
    public static ThemeColor EnsureRatio(ThemeColor color, ThemeColor background, double targetRatio)
    {
        if (Ratio(color, background) >= targetRatio)
        {
            return color;
        }

        var towardWhite = !IsLight(background);
        var best = color;

        for (var step = 1; step <= 50; step++)
        {
            var t = step / 50.0;
            var candidate = towardWhite ? Lighten(color, t) : Darken(color, t);
            best = candidate;

            if (Ratio(candidate, background) >= targetRatio)
            {
                return candidate;
            }
        }

        // Ran to black or white without clearing the bar — that is the most contrast this
        // hue can produce here, and it is still better than where we started.
        return best;
    }

    private static ThemeColor Darken(ThemeColor color, double amount)
        => new(
            (byte)Math.Round(color.R * (1 - amount)),
            (byte)Math.Round(color.G * (1 - amount)),
            (byte)Math.Round(color.B * (1 - amount)));

    private static ThemeColor Lighten(ThemeColor color, double amount)
        => new(
            (byte)Math.Round(color.R + ((255 - color.R) * amount)),
            (byte)Math.Round(color.G + ((255 - color.G) * amount)),
            (byte)Math.Round(color.B + ((255 - color.B) * amount)));

    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
