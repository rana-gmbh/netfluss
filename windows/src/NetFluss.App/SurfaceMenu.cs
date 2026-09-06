// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Windows.Controls;

namespace NetFluss.App;

/// <summary>
/// The command menu, identical on every surface the meter can appear on.
///
/// <para><b>Why this is centralised.</b> Whichever surface a user has chosen is, for them,
/// the whole application — it is the only part of NetFluss on screen. If the taskbar overlay
/// carries the meter and only the tray icon has a menu, then hiding the tray icon locks the
/// user out of their own preferences and, worse, out of quitting: Task Manager becomes the
/// only way to stop the app. Building one menu in one place is what stops a surface from
/// being added later without one.</para>
///
/// <para>Kept deliberately short, matching the macOS status-item menu. Anything longer
/// belongs in Preferences.</para>
/// </summary>
internal static class SurfaceMenu
{
    /// <summary>
    /// A fresh menu each call. WPF menus carry placement state tied to the element that
    /// opened them, so sharing one instance across the overlay and the widget would have
    /// the second one open in the first one's position.
    /// </summary>
    internal static ContextMenu Build(Action showPreferences, Action showSpeedTest, Action quit)
    {
        var menu = new ContextMenu();

        var speedTest = new MenuItem { Header = "Speed Test…" };
        speedTest.Click += (_, _) => showSpeedTest();

        var preferences = new MenuItem { Header = "Preferences…" };
        preferences.Click += (_, _) => showPreferences();

        var exit = new MenuItem { Header = "Quit NetFluss" };
        exit.Click += (_, _) => quit();

        menu.Items.Add(speedTest);
        menu.Items.Add(preferences);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        return menu;
    }
}
