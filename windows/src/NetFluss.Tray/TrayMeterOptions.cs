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

using NetFluss.Core;

namespace NetFluss.Tray;

/// <summary>
/// How the notification-area icon presents the current rates.
///
/// Windows has no menu-bar text area and no DeskBand API since Windows 11, so unlike
/// macOS — where the four styles differ mainly in decoration — the constraint here is
/// raw pixels: a tray icon is 16 px at 100% DPI.
/// </summary>
public enum TrayMeterLayout
{
    /// <summary>Upload over download, stacked. The default; closest to the macOS menu bar.</summary>
    TwoLine,

    /// <summary>One line, download only — legible at 100% DPI where TwoLine is cramped.</summary>
    DownloadOnly,

    /// <summary>One line, upload only.</summary>
    UploadOnly,

    /// <summary>Static glyph, no numbers. Equivalent to the macOS "Icon" menu bar style.</summary>
    Icon,
}

public sealed record TrayMeterOptions
{
    /// <summary>Icon edge length in physical pixels — always DPI-scaled, never a hardcoded 16.</summary>
    public required int Size { get; init; }

    public TrayMeterLayout Layout { get; init; } = TrayMeterLayout.TwoLine;

    public required ThemeColor DownloadColor { get; init; }

    public required ThemeColor UploadColor { get; init; }

    public bool UseBits { get; init; }

    /// <summary>
    /// Segoe UI Semibold holds up better than Bold below ~9 px, where Bold's stems merge.
    /// </summary>
    public string FontFamily { get; init; } = "Segoe UI Semibold";

    /// <summary>
    /// Arrow glyphs cost 3–4 px of a 16 px box. Off by default at small sizes; the colour
    /// already distinguishes the two rows.
    /// </summary>
    public bool ShowArrows { get; init; }

    /// <summary>
    /// What the icon will be composited over. Windows hands the tray no template-image
    /// treatment, so the meter has to know whether it is drawing onto the light or dark
    /// taskbar to keep both rows equally readable. Defaults to the Windows 11 dark taskbar.
    /// </summary>
    public ThemeColor TaskbarBackground { get; init; } = ThemeColor.FromHex("202020");

    /// <summary>
    /// Contrast floor applied to both rate colours against <see cref="TaskbarBackground"/>.
    /// Set to 0 to draw the configured colours untouched.
    /// </summary>
    public double MinimumContrastRatio { get; init; } = Contrast.MinimumReadableRatio;

    /// <summary>Glyph drawn by <see cref="TrayMeterLayout.Icon"/>; see <c>TrayGlyphLibrary</c>.</summary>
    public string IconGlyph { get; init; } = "netfluss";
}
