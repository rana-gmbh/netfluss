// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using NetFluss.Core;
using NetFluss.Tray;

namespace NetFluss.App;

/// <summary>
/// Owns the notification-area icon and repaints it on every monitor tick.
///
/// <para><b>The GDI contract.</b> <c>Bitmap.GetHicon()</c> hands back an unmanaged icon that
/// nothing will ever free for us. At one tick per second this leaks ~86,000 handles a day
/// and hits the 10,000-per-process GDI limit within three hours, at which point the app
/// stops drawing anything at all. So: assign the new icon first, then destroy the previous
/// handle — never the current one, which the shell is still painting from.</para>
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly TrayMeterRenderer _renderer = new();
    private readonly NetworkMonitorService _monitor;

    private nint _currentIconHandle;
    private Icon? _currentIcon;
    private int _lastRenderedSize;
    private string _lastRenderedText = string.Empty;

    public TrayIconHost(NetworkMonitorService monitor, TrayMeterOptions options)
    {
        _monitor = monitor;
        Options = options;

        _icon = new TaskbarIcon
        {
            ToolTipText = "NetFluss",
            ContextMenu = BuildContextMenu(),
        };

        _icon.TrayLeftMouseUp += (_, _) => LeftClicked?.Invoke(this, EventArgs.Empty);
        _icon.ForceCreate();

        _monitor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NetworkMonitorService.Totals))
            {
                Update(_monitor.Totals);
            }
        };

        Update(_monitor.Totals);
    }

    public event EventHandler? LeftClicked;

    public event EventHandler? PreferencesRequested;

    public event EventHandler? QuitRequested;

    public TrayMeterOptions Options { get; set; }

    public void Update(RateTotals totals)
    {
        var size = Dpi.TrayIconSize();
        var options = Options with { Size = size };

        // Repainting an identical bitmap costs a GDI round trip and a shell redraw for
        // nothing. On an idle machine this skips the overwhelming majority of ticks.
        var text = string.Concat(
            RateFormatter.FormatCompact(totals.RxRateBps, options.UseBits),
            "/",
            RateFormatter.FormatCompact(totals.TxRateBps, options.UseBits));

        if (size == _lastRenderedSize && text == _lastRenderedText && _currentIcon is not null)
        {
            UpdateTooltip(totals, options);
            return;
        }

        _lastRenderedSize = size;
        _lastRenderedText = text;

        var handle = _renderer.RenderIconHandle(totals, options);
        var previousHandle = _currentIconHandle;
        var previousIcon = _currentIcon;

        var icon = Icon.FromHandle(handle);
        _currentIconHandle = handle;
        _currentIcon = icon;
        _icon.Icon = icon;

        // Only now is the old handle unreferenced by the shell.
        previousIcon?.Dispose();
        if (previousHandle != nint.Zero)
        {
            Dpi.DestroyIcon(previousHandle);
        }

        UpdateTooltip(totals, options);
    }

    private void UpdateTooltip(RateTotals totals, TrayMeterOptions options)
        => _icon.ToolTipText = string.Concat(
            "NetFluss\n↓ ",
            RateFormatter.FormatRate(totals.RxRateBps, options.UseBits),
            "\n↑ ",
            RateFormatter.FormatRate(totals.TxRateBps, options.UseBits));

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var preferences = new MenuItem { Header = "Preferences…" };
        preferences.Click += (_, _) => PreferencesRequested?.Invoke(this, EventArgs.Empty);

        var quit = new MenuItem { Header = "Quit NetFluss" };
        quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(preferences);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    public void Dispose()
    {
        _icon.Dispose();
        _currentIcon?.Dispose();

        if (_currentIconHandle != nint.Zero)
        {
            Dpi.DestroyIcon(_currentIconHandle);
            _currentIconHandle = nint.Zero;
        }
    }
}
