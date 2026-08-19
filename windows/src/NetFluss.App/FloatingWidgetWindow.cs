// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NetFluss.Core;

namespace NetFluss.App;

/// <summary>
/// An always-on-top desktop panel showing the same readout as the taskbar overlay.
///
/// <para>The Windows form of the macOS <b>Pin</b> feature, and the one placement with no
/// platform risk at all: it owns its own window and depends on nothing undocumented. That
/// makes it the honest recommendation for anyone the taskbar overlay lets down — a second
/// monitor, an unusual shell, or a Windows update that moves the tray.</para>
///
/// <para>Independent of the meter surface rather than an alternative to it: a user can run
/// the tray meter and this together, which is how the Mac's Pin behaves.</para>
/// </summary>
internal sealed class FloatingWidgetWindow : Window
{
    private readonly MeterReadout _readout = new();
    private readonly Border _frame;
    private readonly AppSettings _settings;

    internal FloatingWidgetWindow(AppSettings settings)
    {
        _settings = settings;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        _frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 9, 14, 9),
            Child = _readout,
        };

        Content = _frame;

        // Drag anywhere. There is no title bar to grab, and a widget the user cannot move is
        // a widget in the wrong place.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };

        MouseRightButtonUp += (_, _) => ContextMenuRequested?.Invoke(this, EventArgs.Empty);

        // Position is remembered, so it survives a restart the way the macOS pin does.
        LocationChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                _settings.FloatingWidgetLeft = Left;
                _settings.FloatingWidgetTop = Top;
            }
        };
    }

    internal event EventHandler? ContextMenuRequested;

    internal void ApplySettings(AppSettings settings, ThemeColor download, ThemeColor upload, bool darkSurface)
    {
        _readout.Layout = settings.ReadoutStyle;
        _readout.ApplyAppearance(settings.ReadoutFontSize + 3, download, upload, download);

        // Its own backdrop, unlike the overlay: this floats over arbitrary wallpaper and
        // windows, so it needs a surface of its own to stay readable.
        _frame.Background = new SolidColorBrush(darkSurface
            ? Color.FromArgb(0xE0, 0x20, 0x20, 0x20)
            : Color.FromArgb(0xE0, 0xFA, 0xFA, 0xFA));

        _frame.BorderBrush = new SolidColorBrush(darkSurface
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x22, 0x00, 0x00, 0x00));

        _frame.BorderThickness = new Thickness(1);
    }

    internal void Update(RateTotals totals, bool useBits) => _readout.Update(totals, useBits);

    internal void Place()
    {
        if (_settings.FloatingWidgetLeft is { } left && _settings.FloatingWidgetTop is { } top)
        {
            Left = left;
            Top = top;
            ClampToScreen();
            return;
        }

        // First run: the lower-right corner, out of the way and near the taskbar clock where
        // the eye already goes for this kind of number.
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 220;
        Top = workArea.Bottom - 90;
    }

    /// <summary>
    /// A remembered position can be off-screen after a monitor is unplugged, which would
    /// leave the widget invisible and the preference apparently broken.
    /// </summary>
    private void ClampToScreen()
    {
        var workArea = SystemParameters.WorkArea;

        if (Left < workArea.Left || Left > workArea.Right - 60)
        {
            Left = workArea.Right - 220;
        }

        if (Top < workArea.Top || Top > workArea.Bottom - 40)
        {
            Top = workArea.Bottom - 90;
        }
    }
}
