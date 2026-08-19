// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.ComponentModel;
using System.Windows;
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
