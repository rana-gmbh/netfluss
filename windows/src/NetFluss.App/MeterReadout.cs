// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetFluss.Core;

namespace NetFluss.App;

/// <summary>
/// The rate readout used by the taskbar overlay and the floating widget.
///
/// <para>This is the thing the notification area cannot give us. Freed from a 16 px square,
/// the rates can be laid out the way the macOS menu bar lays them out — full units, real
/// hinted text, arrows that cost nothing because there is width to spare.</para>
///
/// <para>Built in code rather than XAML because both hosts size themselves around it and
/// need to measure it before they have a window to lay out in.</para>
/// </summary>
internal sealed class MeterReadout : UserControl
{
    private readonly TextBlock _downloadText = new();
    private readonly TextBlock _uploadText = new();
    private readonly TextBlock _downloadArrow = new() { Text = "↓" };
    private readonly TextBlock _uploadArrow = new() { Text = "↑" };
    private readonly Grid _root = new();

    private ReadoutStyle _style = ReadoutStyle.Unified;

    internal MeterReadout()
    {
        Focusable = false;
        IsTabStop = false;

        foreach (var block in new[] { _downloadArrow, _downloadText, _uploadArrow, _uploadText })
        {
            block.VerticalAlignment = VerticalAlignment.Center;
            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(block, TextRenderingMode.ClearType);
        }

        // Tabular figures, so the numbers do not jitter sideways every tick as digits change
        // width. The macOS side gets this from the monospaced font design setting.
        var tabular = new FontFamily("Consolas, Cascadia Mono, Segoe UI");
        _downloadText.FontFamily = tabular;
        _uploadText.FontFamily = tabular;

        Content = _root;
        BuildLayout();
    }

    internal ReadoutStyle Layout
    {
        get => _style;
        set
        {
            if (_style == value)
            {
                return;
            }

            _style = value;
            BuildLayout();
        }
    }

    internal void ApplyAppearance(double fontSize, ThemeColor download, ThemeColor upload, ThemeColor secondary)
    {
        var downloadBrush = Freeze(download);
        var uploadBrush = Freeze(upload);
        var secondaryBrush = Freeze(secondary);

        _downloadText.Foreground = downloadBrush;
        _uploadText.Foreground = uploadBrush;
        _downloadArrow.Foreground = downloadBrush;
        _uploadArrow.Foreground = uploadBrush;

        // The stacked style halves the line height, so it needs a smaller face to fit the
        // same taskbar; the macOS stack style does exactly this.
        var effective = _style == ReadoutStyle.Stacked ? Math.Max(8, fontSize - 2) : fontSize;

        foreach (var block in new[] { _downloadText, _uploadText })
        {
            block.FontSize = effective;
        }

        foreach (var block in new[] { _downloadArrow, _uploadArrow })
        {
            block.FontSize = Math.Max(8, effective - 1);
        }

        _ = secondaryBrush;
    }

    internal void Update(RateTotals totals, bool useBits)
    {
        if (_style == ReadoutStyle.Total)
        {
            _downloadText.Text = RateFormatter.FormatRate(totals.RxRateBps + totals.TxRateBps, useBits);
            return;
        }

        _downloadText.Text = RateFormatter.FormatRate(totals.RxRateBps, useBits);
        _uploadText.Text = RateFormatter.FormatRate(totals.TxRateBps, useBits);
    }

    private void BuildLayout()
    {
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        _root.RowDefinitions.Clear();

        switch (_style)
        {
            case ReadoutStyle.Total:
                _root.Children.Add(Row(_downloadArrow, _downloadText, showArrow: false));
                break;

            case ReadoutStyle.Stacked:
                _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // Upload above download, matching the tray meter.
                var upper = Row(_uploadArrow, _uploadText, showArrow: true);
                var lower = Row(_downloadArrow, _downloadText, showArrow: true);
                Grid.SetRow(upper, 0);
                Grid.SetRow(lower, 1);
                _root.Children.Add(upper);
                _root.Children.Add(lower);
                break;

            default:
                var unified = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };

                unified.Children.Add(Row(_uploadArrow, _uploadText, showArrow: true));
                unified.Children.Add(new FrameworkElement { Width = 12 });
                unified.Children.Add(Row(_downloadArrow, _downloadText, showArrow: true));
                _root.Children.Add(unified);
                break;
        }
    }

    private static StackPanel Row(TextBlock arrow, TextBlock value, bool showArrow)
    {
        // Rebuild rather than reparent: a TextBlock can only have one parent, and these are
        // reused across layout changes when the user switches style.
        if (arrow.Parent is StackPanel oldArrowParent)
        {
            oldArrowParent.Children.Remove(arrow);
        }

        if (value.Parent is StackPanel oldValueParent)
        {
            oldValueParent.Children.Remove(value);
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        if (showArrow)
        {
            arrow.Margin = new Thickness(0, 0, 3, 0);
            row.Children.Add(arrow);
        }

        row.Children.Add(value);
        return row;
    }

    private static SolidColorBrush Freeze(ThemeColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
