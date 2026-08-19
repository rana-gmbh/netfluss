// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Runtime.InteropServices;

namespace NetFluss.App;

/// <summary>
/// Notification-area icons are sized in physical pixels against the *system* DPI — the
/// taskbar's — not the DPI of whichever monitor a window happens to sit on. Rendering a
/// fixed 16 px bitmap makes the meter a blurred smear on every 150% and 200% display,
/// which is the single most common complaint about Windows tray meters.
/// </summary>
internal static partial class Dpi
{
    private const int SM_CXSMICON = 49;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    /// <summary>Edge length in physical pixels for a notification-area icon at the current system DPI.</summary>
    internal static int TrayIconSize()
    {
        var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());

        // A zero here would produce a 0x0 bitmap and an ArgumentException deep in GDI+.
        return size > 0 ? size : 16;
    }
}
