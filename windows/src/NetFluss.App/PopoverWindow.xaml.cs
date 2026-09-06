// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NetFluss.Core;

namespace NetFluss.App;

/// <summary>
/// The popover. Phase 0 shows only header totals and adapter cards; the remaining
/// sections (IP/flow, DNS, Wi-Fi, Router, Top Apps, Data Usage) land in Phase 1.
/// </summary>
public partial class PopoverWindow : Window
{
    private readonly NetworkMonitorService _monitor;
    private readonly AppSettings _settings;

    public PopoverWindow(NetworkMonitorService monitor, AppSettings settings)
    {
        InitializeComponent();

        _monitor = monitor;
        _settings = settings;
        AdapterList.ItemsSource = monitor.Adapters;
        monitor.PropertyChanged += OnMonitorChanged;

        Width = settings.PopoverWidth;
        Height = settings.PopoverHeight;

        // A borderless window gets no resize hit-testing of its own; the hook below supplies
        // it. Installed here rather than in a constructor body because it needs the handle.
        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ResizeHook);

        // Remembered on every change rather than on close: the popover is dismissed by
        // deactivating, which is not a close, so waiting for one would never save anything.
        SizeChanged += (_, _) =>
        {
            if (!IsLoaded)
            {
                return;
            }

            _settings.PopoverWidth = ActualWidth;
            _settings.PopoverHeight = ActualHeight;
        };

        // Dismiss-on-deactivate, matching NSPopover.
        Deactivated += (_, _) =>
        {
            if (!IsVisible)
            {
                return;
            }

            Hide();
            Hidden?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised after a dismiss, so the tray toggle can suppress an instant reopen.</summary>
    public event EventHandler? Hidden;

    /// <summary>
    /// Repaints the popover for the selected theme.
    ///
    /// <para>The colours were hardcoded dark in XAML, which meant the popover ignored both
    /// the chosen theme and Windows' own light mode — a Dracula or Solarized user got the
    /// same near-black panel as everyone else, which is most of what "the theme is not
    /// reflected in the app" looked like.</para>
    ///
    /// <para>Card and border are derived from the palette rather than carried in it: they
    /// are the same surface lifted or dropped a little, and asking every theme to specify
    /// them would be four more chances to leave one out.</para>
    /// </summary>
    public void ApplyTheme(SurfacePalette surface, ThemeColor download, ThemeColor upload)
    {
        void Set(string key, Color color) => Resources[key] = new SolidColorBrush(color);

        Set("PopoverBackgroundBrush", Color.FromArgb(0xF2, surface.Background.R, surface.Background.G, surface.Background.B));
        Set("PopoverCardBrush", Color.FromArgb(0xFF, surface.Card.R, surface.Card.G, surface.Card.B));
        Set("PopoverTextBrush", Color.FromRgb(surface.TextPrimary.R, surface.TextPrimary.G, surface.TextPrimary.B));
        Set("PopoverSecondaryBrush", Color.FromRgb(surface.TextSecondary.R, surface.TextSecondary.G, surface.TextSecondary.B));
        Set("PopoverDownloadBrush", Color.FromRgb(download.R, download.G, download.B));
        Set("PopoverUploadBrush", Color.FromRgb(upload.R, upload.G, upload.B));

        // A hairline of the opposite tone: white lifts a dark panel off the desktop, black
        // grounds a light one.
        Set("PopoverBorderBrush", surface.IsDark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x22, 0x00, 0x00, 0x00));
    }

    /// <summary>
    /// Supplies the resize borders that <c>WindowStyle="None"</c> takes away.
    ///
    /// <para>WPF reports every point of a chromeless window as client area, so
    /// <c>ResizeMode="CanResize"</c> alone gives a window that cannot actually be resized.
    /// Answering WM_NCHITTEST with the edge codes hands the drag back to Windows, which
    /// then does the resize itself — with the snapping and the double-click-to-maximise
    /// behaviour a hand-rolled mouse loop would have to reimplement badly.</para>
    ///
    /// <para>Worked in physical pixels throughout: the message carries screen coordinates in
    /// device pixels, and converting them to WPF units to compare against a device-pixel
    /// window rect is how an eight-pixel grip becomes a four-pixel one at 200% scaling.</para>
    /// </summary>
    private nint ResizeHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || !GetWindowRect(hwnd, out var bounds))
        {
            return nint.Zero;
        }

        // Signed: a point on a monitor left of the primary one has a negative X.
        var x = (short)(lParam & 0xFFFF);
        var y = (short)((lParam >> 16) & 0xFFFF);

        var grip = (int)Math.Round(ResizeGripDips * (GetDpiForWindow(hwnd) / 96.0));

        var left = x < bounds.Left + grip;
        var right = x >= bounds.Right - grip;
        var top = y < bounds.Top + grip;
        var bottom = y >= bounds.Bottom - grip;

        var hit = (left, right, top, bottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => HtClient,
        };

        handled = true;
        return hit;
    }

    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    /// <summary>Grip width in device-independent units — comfortable without swallowing clicks.</summary>
    private const double ResizeGripDips = 6;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    public void ShowNearTray()
    {
        UpdateTotals();
        Show();
        PositionNearTray();
        Activate();
    }

    /// <summary>
    /// Anchors the popover to the working-area corner nearest the tray.
    ///
    /// <para>Phase 0 approximation: the taskbar can be docked to any edge and can live on a
    /// secondary monitor, so Phase 1 replaces this with the same edge-aware placement the
    /// macOS popover uses, driven by <c>Shell_TrayWnd</c>'s actual rect and the
    /// <c>ABM_GETTASKBARPOS</c> edge.</para>
    /// </summary>
    private void PositionNearTray()
    {
        var workArea = SystemParameters.WorkArea;
        const double Margin = 8;

        Left = workArea.Right - ActualWidth - Margin;
        Top = workArea.Bottom - ActualHeight - Margin;

        // Keep the window fully on screen when the taskbar sits on the left or top edge.
        if (Left < workArea.Left)
        {
            Left = workArea.Left + Margin;
        }

        if (Top < workArea.Top)
        {
            Top = workArea.Top + Margin;
        }
    }

    private void OnMonitorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkMonitorService.Totals))
        {
            UpdateTotals();
        }
    }

    private void UpdateTotals()
    {
        DownloadText.Text = RateFormatter.FormatRate(_monitor.Totals.RxRateBps, useBits: false);
        UploadText.Text = RateFormatter.FormatRate(_monitor.Totals.TxRateBps, useBits: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitor.PropertyChanged -= OnMonitorChanged;
        base.OnClosed(e);
    }
}
