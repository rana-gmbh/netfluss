// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.ComponentModel;
using System.Windows;
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

    public PopoverWindow(NetworkMonitorService monitor)
    {
        InitializeComponent();

        _monitor = monitor;
        AdapterList.ItemsSource = monitor.Adapters;
        monitor.PropertyChanged += OnMonitorChanged;

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
