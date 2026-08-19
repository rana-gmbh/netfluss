// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using Microsoft.Win32;
using NetFluss.Core;

namespace NetFluss.App;

/// <summary>
/// What the taskbar and the app's own windows are currently painted like.
///
/// <para>Windows composites a tray icon onto the taskbar as-is — there is no template-image
/// treatment of the kind macOS applies in the menu bar — so the meter has to know whether it
/// is drawing onto near-black or near-white to keep both rows readable. Windows exposes the
/// two independently: apps can be dark while the shell is light.</para>
/// </summary>
internal static class SystemTheme
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Windows 11's default taskbar colours, which the tray meter sits on.</summary>
    private static readonly ThemeColor LightTaskbar = ThemeColor.FromHex("F3F3F3");
    private static readonly ThemeColor DarkTaskbar = ThemeColor.FromHex("202020");

    /// <summary>True when the taskbar and Start are light. Governs the tray meter only.</summary>
    internal static bool IsShellLight() => ReadFlag("SystemUsesLightTheme");

    /// <summary>True when app windows are light. Governs the popover and Preferences.</summary>
    internal static bool IsAppLight() => ReadFlag("AppsUseLightTheme");

    /// <summary>The colour the tray icon will actually be composited over.</summary>
    internal static ThemeColor TaskbarBackground() => IsShellLight() ? LightTaskbar : DarkTaskbar;

    /// <summary>
    /// What "Automatic" resolves to for each rate row.
    ///
    /// <para>Not a single monochrome ink. macOS can afford one because the menu bar renders
    /// a template image, but here the two rows sit stacked in a 16 px box with nothing else
    /// to tell them apart — the colour *is* the label. Dropping to one ink made the meter
    /// read as two anonymous numbers. These are the accents the Phase 0 contact sheet was
    /// judged on, brightened for the dark taskbar; <c>Contrast.EnsureRatio</c> then does the
    /// per-taskbar correction.</para>
    /// </summary>
    internal static (ThemeColor Download, ThemeColor Upload) DefaultInk()
        => IsShellLight()
            ? (AppTheme.System.DownloadColor, AppTheme.System.UploadColor)
            : (ThemeColor.FromHex("4CC2FF"), ThemeColor.FromHex("6CCB5F"));

    private static bool ReadFlag(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // The value is absent on a fresh profile and on Windows 10 builds that predate
            // the setting. Dark is the Windows 11 default, so absent means dark.
            return key?.GetValue(name) is int flag && flag != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
