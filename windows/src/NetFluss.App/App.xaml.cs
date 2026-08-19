// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Windows;
using NetFluss.Core;
using NetFluss.Tray;

namespace NetFluss.App;

/// <summary>
/// Entry point. Mirrors the macOS <c>AppDelegate</c> → <c>AppState</c> →
/// <c>NetworkMonitor</c> + <c>StatusBarController</c> wiring order.
///
/// The class is not called <c>App</c> on purpose: a type named <c>App</c> inside a
/// namespace ending in <c>.App</c> resolves ambiguously in generated XAML partials.
/// </summary>
public partial class NetFlussApplication : Application
{
    /// <summary>
    /// Clicking the tray icon while the popover is open deactivates it first, so by the
    /// time the click arrives the popover has already hidden itself and the toggle would
    /// immediately reopen it. Ignoring a toggle that lands right after a hide is the
    /// standard fix; NSPopover handles this for us on macOS.
    /// </summary>
    private static readonly TimeSpan ReopenSuppressionWindow = TimeSpan.FromMilliseconds(250);

    private NetworkMonitorService? _monitor;
    private TrayIconHost? _tray;
    private PopoverWindow? _popover;
    private DateTime _popoverHiddenAt = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _monitor = new NetworkMonitorService(TimeSpan.FromSeconds(1));

        _tray = new TrayIconHost(_monitor, new TrayMeterOptions
        {
            Size = Dpi.TrayIconSize(),
            Layout = TrayMeterLayout.TwoLine,
            DownloadColor = AppTheme.System.DownloadColor,
            UploadColor = AppTheme.System.UploadColor,
        });

        _tray.LeftClicked += (_, _) => TogglePopover();
        _tray.QuitRequested += (_, _) => Shutdown();
        _tray.PreferencesRequested += (_, _) => MessageBox.Show(
            "Preferences arrive in Phase 1 of the Windows port.",
            "NetFluss",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        _monitor.Start();
    }

    private void TogglePopover()
    {
        if (_monitor is null)
        {
            return;
        }

        if (_popover is { IsVisible: true })
        {
            _popover.Hide();
            return;
        }

        if (DateTime.UtcNow - _popoverHiddenAt < ReopenSuppressionWindow)
        {
            return;
        }

        if (_popover is null)
        {
            _popover = new PopoverWindow(_monitor);
            _popover.Hidden += (_, _) => _popoverHiddenAt = DateTime.UtcNow;
        }

        _popover.ShowNearTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _monitor?.Dispose();
        base.OnExit(e);
    }
}
