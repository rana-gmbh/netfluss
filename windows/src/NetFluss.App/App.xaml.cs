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

    private SettingsStore? _store;
    private NetworkMonitorService? _monitor;
    private TrayIconHost? _tray;
    private PopoverWindow? _popover;
    private PreferencesWindow? _preferences;
    private TaskbarOverlayWindow? _overlay;
    private FloatingWidgetWindow? _widget;
    private DateTime _popoverHiddenAt = DateTime.MinValue;

    /// <summary>
    /// Set when the overlay was asked for but could not anchor to the taskbar, so the tray
    /// meter is standing in for it. Preferences reads this to explain itself rather than
    /// leaving the user staring at a setting that appears to do nothing.
    /// </summary>
    internal static bool OverlayFellBackToTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _store = new SettingsStore(SettingsStore.DefaultPath);
        NetFluss.Core.Localization.Use(_store.Settings.Language);

        _monitor = new NetworkMonitorService(TimeSpan.FromSeconds(_store.Settings.RefreshIntervalSeconds));
        _tray = new TrayIconHost(_monitor, BuildMeterOptions());

        _tray.LeftClicked += (_, _) => TogglePopover();
        _tray.QuitRequested += (_, _) => Shutdown();
        _tray.PreferencesRequested += (_, _) => ShowPreferences();

        // Preferences writes, then everything re-reads. One direction, so there is no way
        // for the tray and the settings file to disagree about what is configured.
        _store.Changed += (_, _) => ApplySettings();

        // Surfaces repaint on the same tick that drives the tray meter.
        _monitor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NetworkMonitorService.Totals))
            {
                PushTotals();
            }
        };

        ApplySettings();
        _monitor.Start();
    }

    private void PushTotals()
    {
        if (_store is null || _monitor is null)
        {
            return;
        }

        _overlay?.Update(_monitor.Totals, _store.Settings.UseBits);
        _widget?.Update(_monitor.Totals, _store.Settings.UseBits);
    }

    private TrayMeterOptions BuildMeterOptions()
    {
        var settings = _store!.Settings;
        var (downloadInk, uploadInk) = SystemTheme.DefaultInk();

        return new TrayMeterOptions
        {
            Size = Dpi.TrayIconSize(),
            Layout = PreferencesWindow.ToLayout(settings.MeterStyle),
            DownloadColor = settings.ResolveDownloadColor(downloadInk),
            UploadColor = settings.ResolveUploadColor(uploadInk),
            UseBits = settings.UseBits,
            ShowArrows = settings.ShowArrows,
            TaskbarBackground = SystemTheme.TaskbarBackground(),
            MinimumContrastRatio = settings.EnforceContrast ? Contrast.MinimumReadableRatio : 0,
            IconGlyph = settings.TrayIconGlyph,
        };
    }

    private void ApplySettings()
    {
        if (_store is null || _monitor is null || _tray is null)
        {
            return;
        }

        var settings = _store.Settings;

        _monitor.Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
        _monitor.ExcludeTunnelAdapters = settings.ExcludeTunnelAdapters;
        _monitor.TotalsFromVisibleAdaptersOnly = settings.TotalsFromVisibleAdaptersOnly;

        var (downloadInk, uploadInk) = SystemTheme.DefaultInk();
        var download = settings.ResolveDownloadColor(downloadInk);
        var upload = settings.ResolveUploadColor(uploadInk);

        ApplyOverlay(settings, download, upload);
        ApplyWidget(settings, download, upload);

        _tray.Options = BuildMeterOptions();
        _tray.Redraw();

        // The tray icon is hidden while the overlay is carrying the meter, but only while it
        // is genuinely anchored — otherwise the fallback would have nothing to fall back to.
        _tray.IsVisible = settings.MeterSurface == MeterSurface.Tray
                          || _overlay is not { IsAnchored: true };

        PushTotals();
    }

    private void ApplyOverlay(AppSettings settings, ThemeColor download, ThemeColor upload)
    {
        if (settings.MeterSurface != MeterSurface.TaskbarOverlay)
        {
            _overlay?.Stop();
            _overlay?.Close();
            _overlay = null;
            OverlayFellBackToTray = false;
            return;
        }

        if (_overlay is null)
        {
            _overlay = new TaskbarOverlayWindow(_monitor!);
            _overlay.Clicked += (_, _) => TogglePopover();
            _overlay.ContextMenuRequested += (_, _) => ShowPreferences();

            // The overlay reports rather than decides. Losing the anchor brings the tray
            // meter back immediately, so there is never a moment with no meter at all.
            _overlay.AnchorLost += (_, _) =>
            {
                OverlayFellBackToTray = true;
                if (_tray is not null)
                {
                    _tray.IsVisible = true;
                }
            };

            _overlay.Start();
        }

        _overlay.ApplySettings(settings, download, upload);
        OverlayFellBackToTray = !_overlay.IsAnchored;
    }

    private void ApplyWidget(AppSettings settings, ThemeColor download, ThemeColor upload)
    {
        if (!settings.ShowFloatingWidget)
        {
            _widget?.Close();
            _widget = null;
            return;
        }

        if (_widget is null)
        {
            _widget = new FloatingWidgetWindow(settings);
            _widget.ContextMenuRequested += (_, _) => ShowPreferences();
            _widget.Show();
            _widget.Place();
        }

        _widget.ApplySettings(settings, download, upload, darkSurface: !SystemTheme.IsAppLight());
    }

    private void ShowPreferences()
    {
        if (_store is null)
        {
            return;
        }

        if (_preferences is { IsLoaded: true })
        {
            _preferences.Activate();
            return;
        }

        _preferences = new PreferencesWindow(_store);
        _preferences.Closed += (_, _) => _preferences = null;
        _preferences.Show();
        _preferences.Activate();
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
        _overlay?.Stop();
        _overlay?.Close();
        _widget?.Close();
        _tray?.Dispose();
        _monitor?.Dispose();
        base.OnExit(e);
    }
}
