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
        var (systemDownload, systemUpload) = SystemTheme.DefaultInk();
        var (downloadInk, uploadInk) = settings.ResolveRateColors(systemDownload, systemUpload);

        return new TrayMeterOptions
        {
            Size = Dpi.TrayIconSize(),
            Layout = PreferencesWindow.ToLayout(settings.MeterStyle),
            DownloadColor = downloadInk,
            UploadColor = uploadInk,
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

        // Adapter visibility was the same shape of bug as the theme: HiddenAdapters was
        // stored and round-trip tested, and nothing ever handed it to the monitor.
        _monitor.Visibility = settings.VisibilityOptions();
        _monitor.Refresh();

        var (systemDownload, systemUpload) = SystemTheme.DefaultInk();
        var (download, upload) = settings.ResolveRateColors(systemDownload, systemUpload);

        // One resolved palette for every themed window, so a theme cannot reach some
        // surfaces and not others — which is how it came to be wired to none of them.
        var surface = settings.Theme.Surface(SystemTheme.IsAppLight());

        ApplyOverlay(settings, download, upload);
        ApplyWidget(settings, download, upload, surface);
        ApplyPopoverTheme();

        var overlayCarriesTheMeter = _overlay is { IsAnchored: true };

        // While another surface shows the numbers, the tray icon drops to a static glyph so
        // the rates are not displayed twice — but it stays *present*, because it is where a
        // Windows user looks for a background app's menu. Hiding it is opt-in.
        _tray.Options = overlayCarriesTheMeter
            ? BuildMeterOptions() with { Layout = TrayMeterLayout.Icon }
            : BuildMeterOptions();

        _tray.Redraw();
        _tray.IsVisible = !(settings.HideTrayIcon && overlayCarriesTheMeter);

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
            _overlay = new TaskbarOverlayWindow(_monitor!)
            {
                ContextMenu = SurfaceMenu.Build(ShowPreferences, Shutdown),
            };

            _overlay.Clicked += (_, _) => TogglePopover();

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

    /// <summary>Pushes the current theme into the popover, whenever one exists to push into.</summary>
    private void ApplyPopoverTheme()
    {
        if (_store is null || _popover is null)
        {
            return;
        }

        var settings = _store.Settings;
        var (systemDownload, systemUpload) = SystemTheme.DefaultInk();
        var (download, upload) = settings.ResolveRateColors(systemDownload, systemUpload);

        _popover.ApplyTheme(settings.Theme.Surface(SystemTheme.IsAppLight()), download, upload);
    }

    private void ApplyWidget(AppSettings settings, ThemeColor download, ThemeColor upload, SurfacePalette surface)
    {
        if (!settings.ShowFloatingWidget)
        {
            _widget?.Close();
            _widget = null;
            return;
        }

        if (_widget is null)
        {
            _widget = new FloatingWidgetWindow(settings)
            {
                ContextMenu = SurfaceMenu.Build(ShowPreferences, Shutdown),
            };

            _widget.Clicked += (_, _) => TogglePopover();
            _widget.Show();
            _widget.Place();
        }

        _widget.ApplySettings(settings, download, upload, surface);
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

        _preferences = new PreferencesWindow(_store, _monitor!);
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
            _popover = new PopoverWindow(_monitor, _store!.Settings);
            _popover.Hidden += (_, _) => _popoverHiddenAt = DateTime.UtcNow;

            // Themed on creation as well as on every settings change: the window is built
            // lazily on first open, so waiting for a change would show it unthemed once.
            ApplyPopoverTheme();
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
