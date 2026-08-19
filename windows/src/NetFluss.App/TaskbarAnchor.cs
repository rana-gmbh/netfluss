// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Runtime.InteropServices;

namespace NetFluss.App;

/// <summary>Which screen edge the taskbar is docked to.</summary>
internal enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>Where the overlay should sit, in physical pixels, plus the DPI it must draw at.</summary>
internal readonly record struct TaskbarPlacement(
    int Left,
    int Top,
    int Width,
    int Height,
    TaskbarEdge Edge,
    uint Dpi)
{
    internal bool IsHorizontal => Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
}

/// <summary>
/// Locates the taskbar and works out where a meter can sit on it.
///
/// <para><b>This is the undocumented part of the port, and it is undocumented on purpose.</b>
/// Microsoft removed the DeskBand API in Windows 11 and shipped no replacement, so the only
/// way to put a real rate readout on the taskbar is to find the shell's own windows and
/// place a window over them. <c>Shell_TrayWnd</c> and <c>TrayNotifyWnd</c> are class names
/// the shell has used since Windows 95 and which nothing promises to keep.</para>
///
/// <para>So every lookup here is failure-tolerant and returns null rather than throwing or
/// guessing. A null placement is not an error state — it is the signal for the app to fall
/// back to the notification-area meter, which cannot break.</para>
/// </summary>
internal static partial class TaskbarAnchor
{
    /// <summary>Gap to leave before the notification area, in device-independent units.</summary>
    private const int GapFromTrayDip = 8;

    private const int MinimumWidthDip = 90;
    private const int MaximumWidthDip = 260;

    /// <summary>
    /// Computes where the overlay belongs, or null when the taskbar cannot be found or has
    /// no usable room — a fullscreen game, an auto-hidden taskbar mid-slide, or a future
    /// Windows that renames these windows.
    /// </summary>
    /// <param name="desiredWidthDip">
    /// Wanted width in device-independent units. Scaled here by the taskbar's own DPI:
    /// window geometry is physical pixels while WPF lays its content out in DIPs, and
    /// conflating the two gives a window half the width it needs at 200% scaling, which
    /// silently clips the left-hand rate off the readout.
    /// </param>
    internal static TaskbarPlacement? Locate(int desiredWidthDip)
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || !GetWindowRect(taskbar, out var taskbarRect))
        {
            return null;
        }

        var taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        if (taskbarWidth <= 0 || taskbarHeight <= 0)
        {
            return null;
        }

        var edge = EdgeOf(taskbarRect);
        var dpi = DpiOf(taskbar);

        // The notification area is the landmark: the meter goes immediately before it, which
        // is where every Windows meter has lived and where users look for one.
        var tray = FindWindowEx(taskbar, nint.Zero, "TrayNotifyWnd", null);
        var trayRect = default(Rect);
        var haveTray = tray != nint.Zero && GetWindowRect(tray, out trayRect);

        var scale = dpi / 96.0;
        var width = (int)Math.Round(Math.Clamp(desiredWidthDip, MinimumWidthDip, MaximumWidthDip) * scale);
        var gap = (int)Math.Round(GapFromTrayDip * scale);

        if (edge is TaskbarEdge.Top or TaskbarEdge.Bottom)
        {
            // Right-aligned against the tray, or against the taskbar's right edge if the
            // tray could not be found.
            var rightBoundary = haveTray ? trayRect.Left - gap : taskbarRect.Right - gap;
            var left = rightBoundary - width;

            if (left < taskbarRect.Left)
            {
                // Not enough room — a very narrow screen, or a tray full of icons.
                return null;
            }

            return new TaskbarPlacement(left, taskbarRect.Top, width, taskbarHeight, edge, dpi);
        }

        // A vertical taskbar is only a Windows 10 arrangement, but it is still a supported
        // one there and a meter jammed off the edge is worse than no meter.
        var stackHeight = Math.Min(taskbarHeight, (int)Math.Round(40 * scale));
        var bottomBoundary = haveTray ? trayRect.Top - gap : taskbarRect.Bottom - gap;
        var top = bottomBoundary - stackHeight;

        return top < taskbarRect.Top
            ? null
            : new TaskbarPlacement(taskbarRect.Left, top, taskbarWidth, stackHeight, edge, dpi);
    }

    /// <summary>
    /// True while the shell is showing something fullscreen. The overlay is topmost, so
    /// without this check it would paint a rate readout over a game or a presentation.
    /// </summary>
    internal static bool IsFullScreenAppActive()
    {
        try
        {
            var state = 0;
            return SHQueryUserNotificationState(ref state) == 0
                   && state is QunsBusy or QunsRunningD3dFullScreen or QunsPresentationMode;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Message the shell broadcasts after Explorer restarts. Every window rebuilt, every
    /// handle stale — an overlay that does not listen for this is left anchored to a taskbar
    /// that no longer exists, which is the classic way these meters "stop working".
    /// </summary>
    internal static uint TaskbarCreatedMessage() => RegisterWindowMessage("TaskbarCreated");

    private static TaskbarEdge EdgeOf(Rect taskbar)
    {
        // Derived from the taskbar's own rect rather than ABM_GETTASKBARPOS: that call needs
        // an APPBARDATA the shell recognises, and reading the geometry works the same on
        // every version including the Windows 11 centred layout.
        var width = taskbar.Right - taskbar.Left;
        var height = taskbar.Bottom - taskbar.Top;

        if (width >= height)
        {
            return taskbar.Top <= 0 ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        return taskbar.Left <= 0 ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    private static uint DpiOf(nint window)
    {
        try
        {
            var dpi = GetDpiForWindow(window);
            return dpi > 0 ? dpi : 96;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;
        }
    }

    private const int QunsBusy = 2;
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string? className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string message);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(ref int state);
}
