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
    private DateTime _popoverHiddenAt = DateTime.MinValue;

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

        ApplySettings();
        _monitor.Start();
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
        };
    }

    private void ApplySettings()
    {
        if (_store is null || _monitor is null || _tray is null)
        {
            return;
        }

        _monitor.Interval = TimeSpan.FromSeconds(_store.Settings.RefreshIntervalSeconds);
        _monitor.ExcludeTunnelAdapters = _store.Settings.ExcludeTunnelAdapters;
        _monitor.TotalsFromVisibleAdaptersOnly = _store.Settings.TotalsFromVisibleAdaptersOnly;

        _tray.Options = BuildMeterOptions();
        _tray.Redraw();
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
        _tray?.Dispose();
        _monitor?.Dispose();
        base.OnExit(e);
    }
}
