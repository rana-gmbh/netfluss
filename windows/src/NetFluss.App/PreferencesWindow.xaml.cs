// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetFluss.Core;
using NetFluss.Tray;

namespace NetFluss.App;

/// <summary>
/// Preferences, in the shape of Windows 11 Settings: one scrolling column of grouped cards
/// with the control on the right, rather than the macOS tabbed <c>Form</c>.
///
/// <para>Changes apply immediately and persist immediately — there is no OK button, which
/// is both what Settings does and what the macOS preferences window already did.</para>
/// </summary>
public partial class PreferencesWindow : Window
{
    /// <summary>Tray sizes at 100%, 125%, 150% and 200% scaling.</summary>
    private static readonly int[] PreviewSizes = [16, 20, 24, 32];

    /// <summary>A plausible busy moment, so the preview shows the hard case rather than "0".</summary>
    private static readonly RateTotals PreviewTotals = new(4_720_000, 834_000);

    private readonly SettingsStore _store;
    private readonly NetworkMonitorService _monitor;
    private readonly TrayMeterRenderer _renderer = new();
    private bool _loading;

    public PreferencesWindow(SettingsStore store, NetworkMonitorService monitor)
    {
        InitializeComponent();

        _store = store;
        _monitor = monitor;

        // The checklist has to offer every adapter the machine has, including ones currently
        // filtered out of the popover — otherwise an adapter hidden by mistake could never be
        // found again to unhide it.
        _monitor.PropertyChanged += OnMonitorChanged;

        ApplyTheme();
        PopulateChoices();
        LoadFromSettings();
        RefreshPreview();
        RefreshAdapterList();
        RefreshDns();
        DnsAdapterBox.SelectionChanged += OnDnsAdapterChanged;

        // Both need the window handle, so neither can run from the constructor body.
        SourceInitialized += (_, _) =>
        {
            ApplyMicaBackdrop();
            FitToWorkArea();
        };
    }

    /// <summary>
    /// Shrinks the window to whatever the display actually has room for.
    ///
    /// <para>The XAML size is a preference, not a promise. A 760-unit window is comfortable
    /// on a desktop and taller than the work area on a 1366×768 laptop, which is still a
    /// shipping configuration; a Preferences window whose last card cannot be scrolled to is
    /// worse than a cramped one.</para>
    ///
    /// <para>Deliberately not <see cref="SystemParameters.WorkArea"/>: those statics are
    /// resolved against the primary monitor at process start and do not survive a
    /// PerMonitorV2 app being opened on a second display with different scaling. The monitor
    /// under the window, and the window's own DPI, are the only two things that are true.</para>
    /// </summary>
    private void FitToWorkArea()
    {
        const double Margin = 48;

        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        // GetDpiForWindow, not VisualTreeHelper.GetDpi: the visual tree still reports a
        // scale of 1.0 this early, which silently turned the clamp into a no-op on the
        // 200% display it was written for.
        var scale = GetDpiForWindow(handle) / 96.0;
        if (scale <= 0)
        {
            scale = 1;
        }

        var availableWidth = ((info.WorkRight - info.WorkLeft) / scale) - Margin;
        var availableHeight = ((info.WorkBottom - info.WorkTop) / scale) - Margin;

        Width = Math.Max(MinWidth, Math.Min(Width, availableWidth));
        Height = Math.Max(MinHeight, Math.Min(Height, availableHeight));

        // The window was centred for its original size, so re-centre for the new one.
        Left = (info.WorkLeft / scale) + ((availableWidth + Margin - Width) / 2);
        Top = (info.WorkTop / scale) + ((availableHeight + Margin - Height) / 2);
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public int MonitorLeft;
        public int MonitorTop;
        public int MonitorRight;
        public int MonitorBottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
    }

    /// <summary>
    /// Windows 11 22H2+ Mica. Silently skipped elsewhere — the solid page brush underneath
    /// is a complete look on its own, so this is polish, never a requirement.
    /// </summary>
    private void ApplyMicaBackdrop()
    {
        const int DwmwaSystemBackdropType = 38;
        const int DwmwaUseImmersiveDarkMode = 20;
        const int BackdropMica = 2;

        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = SystemTheme.IsAppLight() ? 0 : 1;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var backdrop = BackdropMica;
            _ = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Older Windows without dwmapi surfaces; the flat background stands alone.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private void ApplyTheme()
    {
        var light = SystemTheme.IsAppLight();

        void Brush(string key, string hex) =>
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        // Windows 11 Settings' own values for each surface, rather than approximations —
        // the window sits next to real Settings often enough for a mismatch to show.
        Brush("PageBrush", light ? "#F3F3F3" : "#202020");
        Brush("CardBrush", light ? "#FBFBFB" : "#2B2B2B");
        Brush("CardBorderBrush", light ? "#E5E5E5" : "#333333");
        Brush("TextBrush", light ? "#1A1A1A" : "#FFFFFF");
        Brush("SecondaryTextBrush", light ? "#5D5D5D" : "#C5C5C5");
        Brush("SwitchOffBrush", light ? "#00000000" : "#00000000");
        Brush("SwitchBorderBrush", light ? "#8A8A8A" : "#9A9A9A");
        Brush("SwitchKnobBrush", light ? "#5A5A5A" : "#CFCFCF");
        Brush("SwitchKnobOnBrush", light ? "#FFFFFF" : "#000000");

        // Informational, not an error: the overlay failing to anchor is a supported outcome.
        Brush("NoticeBrush", light ? "#FFF4CE" : "#433519");

        // The active-preset checkmark, matching the green the macOS DNS list uses.
        Brush("ActiveBrush", light ? "#0F7B0F" : "#6CCB5F");

        Resources["AccentBrush"] = new SolidColorBrush(SystemParameters.WindowGlassColor.A == 0
            ? (Color)ColorConverter.ConvertFromString("#0078D4")
            : SystemParameters.WindowGlassColor);
    }

    private void PopulateChoices()
    {
        _loading = true;

        SurfaceBox.ItemsSource = new[]
        {
            new Choice<MeterSurface>(MeterSurface.TaskbarOverlay, "On the taskbar"),
            new Choice<MeterSurface>(MeterSurface.Tray, "Notification area"),
        };

        ReadoutStyleBox.ItemsSource = new[]
        {
            new Choice<ReadoutStyle>(ReadoutStyle.Unified, "One line"),
            new Choice<ReadoutStyle>(ReadoutStyle.Stacked, "Two lines"),
            new Choice<ReadoutStyle>(ReadoutStyle.Total, "Combined total"),
        };

        ReadoutSizeBox.ItemsSource = new[]
        {
            new Choice<double>(9, "Small"),
            new Choice<double>(11, "Default"),
            new Choice<double>(13, "Large"),
            new Choice<double>(16, "Largest"),
        };

        GlyphBox.ItemsSource = TrayGlyphLibrary.Options
            .Select(option => new Choice<string>(option.Id, option.Label))
            .ToArray();

        MeterStyleBox.ItemsSource = new[]
        {
            new Choice<MeterStyle>(MeterStyle.TwoLine, "Download and upload"),
            new Choice<MeterStyle>(MeterStyle.DownloadOnly, "Download only"),
            new Choice<MeterStyle>(MeterStyle.UploadOnly, "Upload only"),
            new Choice<MeterStyle>(MeterStyle.Icon, "Icon only"),
        };

        UnitsBox.ItemsSource = new[]
        {
            new Choice<bool>(false, "Bytes per second"),
            new Choice<bool>(true, "Bits per second"),
        };

        IntervalBox.ItemsSource = new[]
        {
            new Choice<double>(1, "Every second"),
            new Choice<double>(2, "Every 2 seconds"),
            new Choice<double>(3, "Every 3 seconds"),
            new Choice<double>(5, "Every 5 seconds"),
        };

        LanguageBox.ItemsSource = new[]
        {
            new Choice<AppLanguage>(AppLanguage.System, "System default"),
            new Choice<AppLanguage>(AppLanguage.English, "English"),
            new Choice<AppLanguage>(AppLanguage.German, "Deutsch"),
            new Choice<AppLanguage>(AppLanguage.SimplifiedChinese, "简体中文"),
            new Choice<AppLanguage>(AppLanguage.TraditionalChinese, "繁體中文"),
        };

        ThemeBox.ItemsSource = AppTheme.All
            .Select(theme => new Choice<string>(theme.Id, theme.DisplayName))
            .ToArray();

        var accents = new[]
        {
            new Choice<string>("system", "Automatic"),
            new Choice<string>("blue", "Blue"),
            new Choice<string>("green", "Green"),
            new Choice<string>("teal", "Teal"),
            new Choice<string>("purple", "Purple"),
            new Choice<string>("orange", "Orange"),
            new Choice<string>("pink", "Pink"),
            new Choice<string>("yellow", "Yellow"),
            new Choice<string>("white", "White"),
            new Choice<string>("black", "Black"),
        };

        DownloadAccentBox.ItemsSource = accents;
        UploadAccentBox.ItemsSource = accents.ToArray();

        _loading = false;
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var settings = _store.Settings;

        Select(SurfaceBox, settings.MeterSurface);
        Select(ReadoutStyleBox, settings.ReadoutStyle);
        Select(ReadoutSizeBox, settings.ReadoutFontSize);
        Select(GlyphBox, TrayGlyphLibrary.Normalize(settings.TrayIconGlyph));
        Select(MeterStyleBox, settings.MeterStyle);
        Select(UnitsBox, settings.UseBits);
        WidgetToggle.IsChecked = settings.ShowFloatingWidget;
        HideTrayToggle.IsChecked = settings.HideTrayIcon;
        ShowInactiveToggle.IsChecked = settings.ShowInactiveAdapters;
        TotalsVisibleOnlyToggle.IsChecked = settings.TotalsFromVisibleAdaptersOnly;
        Select(IntervalBox, settings.RefreshIntervalSeconds);
        Select(LanguageBox, settings.Language);
        Select(ThemeBox, settings.ThemeId);
        Select(DownloadAccentBox, settings.DownloadAccent);
        Select(UploadAccentBox, settings.UploadAccent);

        ArrowsToggle.IsChecked = settings.ShowArrows;
        ContrastToggle.IsChecked = settings.EnforceContrast;
        LaunchToggle.IsChecked = LaunchAtLogin.IsEnabled();

        ContrastCaption.Text = SystemTheme.IsShellLight()
            ? "Your taskbar is light, where saturated greens lose contrast against the blues."
            : "Darkens or brightens the rate colours so both rows read equally on your taskbar.";

        LaunchCaption.Text = "Adds NetFluss to your sign-in apps.";
        PreviewCaption.Text = SystemTheme.IsShellLight()
            ? "Your taskbar is light. Shown at each display scaling."
            : "Your taskbar is dark. Shown at each display scaling.";

        FooterText.Text = $"Settings are stored in {SettingsStore.DefaultPath}";

        _loading = false;

        SurfaceBox.SelectionChanged += OnChanged;
        ReadoutStyleBox.SelectionChanged += OnChanged;
        ReadoutSizeBox.SelectionChanged += OnChanged;
        WidgetToggle.Click += OnChanged;
        HideTrayToggle.Click += OnChanged;
        ShowInactiveToggle.Click += OnChanged;
        TotalsVisibleOnlyToggle.Click += OnChanged;
        GlyphBox.SelectionChanged += OnChanged;
        MeterStyleBox.SelectionChanged += OnChanged;
        UnitsBox.SelectionChanged += OnChanged;
        IntervalBox.SelectionChanged += OnChanged;
        LanguageBox.SelectionChanged += OnChanged;
        ThemeBox.SelectionChanged += OnChanged;
        DownloadAccentBox.SelectionChanged += OnChanged;
        UploadAccentBox.SelectionChanged += OnChanged;

        ArrowsToggle.Click += OnChanged;
        ContrastToggle.Click += OnChanged;
        LaunchToggle.Click += OnChanged;
    }

    private void OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        // One batch, so a single change writes the file once and the app re-applies once.
        _store.Batch(settings =>
        {
            settings.MeterSurface = Value(SurfaceBox, settings.MeterSurface);
            settings.ReadoutStyle = Value(ReadoutStyleBox, settings.ReadoutStyle);
            settings.ReadoutFontSize = Value(ReadoutSizeBox, settings.ReadoutFontSize);
            settings.ShowFloatingWidget = WidgetToggle.IsChecked == true;
            settings.HideTrayIcon = HideTrayToggle.IsChecked == true;
            settings.ShowInactiveAdapters = ShowInactiveToggle.IsChecked == true;
            settings.TotalsFromVisibleAdaptersOnly = TotalsVisibleOnlyToggle.IsChecked == true;
            settings.TrayIconGlyph = Value(GlyphBox, settings.TrayIconGlyph);
            settings.MeterStyle = Value(MeterStyleBox, settings.MeterStyle);
            settings.UseBits = Value(UnitsBox, settings.UseBits);
            settings.RefreshIntervalSeconds = Value(IntervalBox, settings.RefreshIntervalSeconds);
            settings.Language = Value(LanguageBox, settings.Language);
            settings.ThemeId = Value(ThemeBox, settings.ThemeId);
            settings.DownloadAccent = Value(DownloadAccentBox, settings.DownloadAccent);
            settings.UploadAccent = Value(UploadAccentBox, settings.UploadAccent);
            settings.ShowArrows = ArrowsToggle.IsChecked == true;
            settings.EnforceContrast = ContrastToggle.IsChecked == true;
            settings.LaunchAtLogin = LaunchToggle.IsChecked == true;
        });

        // Registry, not the settings file — Windows owns this one, and writing it is the
        // only thing that actually makes the app start.
        LaunchAtLogin.Set(_store.Settings.LaunchAtLogin);
        LaunchToggle.IsChecked = LaunchAtLogin.IsEnabled();

        NetFluss.Core.Localization.Use(_store.Settings.Language);
        RefreshPreview();
    }

    /// <summary>
    /// Renders the real tray bitmaps at every scaling. The 16 px case is the whole
    /// difficulty of this port, so it is shown rather than described — and it means a user
    /// choosing "Download only" can see what they get before they commit to it.
    /// </summary>
    private void RefreshPreview()
    {
        var settings = _store.Settings;
        var shellLight = SystemTheme.IsShellLight();
        var taskbar = SystemTheme.TaskbarBackground();
        var (systemDownload, systemUpload) = SystemTheme.DefaultInk();
        var (downloadInk, uploadInk) = settings.ResolveRateColors(systemDownload, systemUpload);
        var swatch = new SolidColorBrush(Color.FromRgb(taskbar.R, taskbar.G, taskbar.B));

        var items = new List<PreviewItem>(PreviewSizes.Length);

        foreach (var size in PreviewSizes)
        {
            var options = new TrayMeterOptions
            {
                Size = size,
                Layout = ToLayout(settings.MeterStyle),
                DownloadColor = downloadInk,
                UploadColor = uploadInk,
                UseBits = settings.UseBits,
                ShowArrows = settings.ShowArrows,
                TaskbarBackground = taskbar,
                MinimumContrastRatio = settings.EnforceContrast ? Contrast.MinimumReadableRatio : 0,
            };

            using var bitmap = _renderer.RenderBitmap(PreviewTotals, options);

            items.Add(new PreviewItem(
                ToImageSource(bitmap),
                size,
                $"{size * 100 / 16}%",
                swatch));
        }

        PreviewStrip.ItemsSource = items;

        UpdateSwatch(DownloadSwatch, downloadInk, taskbar, settings.EnforceContrast);
        UpdateSwatch(UploadSwatch, uploadInk, taskbar, settings.EnforceContrast);

        PreviewCaption.Text = shellLight
            ? "Your taskbar is light. Shown at each display scaling."
            : "Your taskbar is dark. Shown at each display scaling.";

        UpdatePlacementCaptions();

        // The glyph only appears in Icon mode, so grey the row out rather than letting a
        // user change a setting and see nothing happen.
        var iconMode = settings.MeterStyle == MeterStyle.Icon;
        GlyphBox.IsEnabled = iconMode;
        GlyphPreview.Opacity = iconMode ? 1.0 : 0.4;
        GlyphTitle.Opacity = iconMode ? 1.0 : 0.6;

        using var glyph = _renderer.RenderBitmap(PreviewTotals, new TrayMeterOptions
        {
            Size = 32,
            Layout = TrayMeterLayout.Icon,
            IconGlyph = TrayGlyphLibrary.Normalize(settings.TrayIconGlyph),
            DownloadColor = downloadInk,
            UploadColor = uploadInk,
            TaskbarBackground = taskbar,
            MinimumContrastRatio = settings.EnforceContrast ? Contrast.MinimumReadableRatio : 0,
        });

        GlyphPreview.Source = ToImageSource(glyph);
    }

    /// <summary>
    /// Explains the placement choice, including when it has quietly not been honoured.
    ///
    /// <para>The taskbar overlay sits on geometry Windows does not promise, so it can fail
    /// on a machine where nothing is wrong with the app. Saying so is the difference between
    /// a documented limitation and a setting that appears broken.</para>
    /// </summary>
    private void UpdatePlacementCaptions()
    {
        var overlayChosen = _store.Settings.MeterSurface == MeterSurface.TaskbarOverlay;
        var fellBack = overlayChosen && NetFlussApplication.OverlayFellBackToTray;

        SurfaceCaption.Text = overlayChosen
            ? "Beside the clock, with room for full units. Windows offers no supported way to do this, so it is best effort — NetFluss falls back to the notification area if the taskbar moves out from under it."
            : "A 16–32 px icon, depending on your display scaling. Cramped, and it never breaks.";

        FallbackNotice.Visibility = fellBack ? Visibility.Visible : Visibility.Collapsed;

        // Hiding the tray icon is only meaningful when something else carries the meter, and
        // it is worth saying plainly what it costs: the icon is the obvious way back here.
        HideTrayToggle.IsEnabled = overlayChosen;
        HideTrayCaption.Text = overlayChosen
            ? "The icon stays as a plain glyph so the rates are not shown twice. Hiding it leaves right-clicking the taskbar meter as the only way to reach Preferences or quit."
            : "Only available when the meter is on the taskbar — otherwise this is where the meter lives.";
    }

    private static void UpdateSwatch(System.Windows.Controls.Border swatch, ThemeColor color, ThemeColor taskbar, bool enforce)
    {
        var shown = enforce ? Contrast.EnsureRatio(color, taskbar, Contrast.MinimumReadableRatio) : color;
        swatch.Background = new SolidColorBrush(Color.FromRgb(shown.R, shown.G, shown.B));
    }

    internal static TrayMeterLayout ToLayout(MeterStyle style) => style switch
    {
        MeterStyle.DownloadOnly => TrayMeterLayout.DownloadOnly,
        MeterStyle.UploadOnly => TrayMeterLayout.UploadOnly,
        MeterStyle.Icon => TrayMeterLayout.Icon,
        _ => TrayMeterLayout.TwoLine,
    };

    private static BitmapSource ToImageSource(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void Select<T>(Selector box, T value)
    {
        foreach (var item in box.Items)
        {
            if (item is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                box.SelectedItem = item;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private static T Value<T>(Selector box, T fallback)
        => box.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    /// <summary>A combo entry: the stored value plus the label the user reads.</summary>
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Preferences is rebuilt every time it opens, so the monitor subscription has to come
    /// off with it — otherwise each visit leaves another handler ticking once a second
    /// against a dead window for the life of the process.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _monitor.PropertyChanged -= OnMonitorChanged;
        base.OnClosed(e);
    }

    /// <summary>
    /// One row of the adapter checklist.
    ///
    /// <para>Observable and mutable rather than a record, because the rate in
    /// <see cref="StatusText"/> changes every second. Rebuilding the ItemsSource for that
    /// would destroy and recreate the row's controls once a tick — which silently ate any
    /// rename in progress, since the text box the user was typing into stopped existing
    /// between keystrokes.</para>
    ///
    /// <para><see cref="DisplayName"/> is deliberately never updated in place for the same
    /// reason: it only changes when the user renames the adapter, and writing to it on a
    /// tick would overwrite half-typed text.</para>
    /// </summary>
    private sealed class AdapterRow(string id, string displayName, string description) : INotifyPropertyChanged
    {
        private string _statusText = string.Empty;
        private bool _isShown = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public string Description { get; } = description;

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value)
                {
                    return;
                }

                _statusText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        public bool IsShown
        {
            get => _isShown;
            set
            {
                if (_isShown == value)
                {
                    return;
                }

                _isShown = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
            }
        }
    }

    private void OnMonitorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkMonitorService.AllAdapters))
        {
            RefreshAdapterList();
        }
    }

    /// <summary>
    /// Rebuilds the checklist from the machine's current adapters.
    ///
    /// <para>Rebuilt on every monitor tick rather than bound directly, because a tick
    /// replaces the <see cref="AdapterStatus"/> records wholesale and a live binding would
    /// reset each checkbox from the settings mid-click.</para>
    /// </summary>
    private void RefreshAdapterList()
    {
        var settings = _store.Settings;

        string Status(AdapterStatus adapter) => adapter.IsUp
            ? RateFormatter.FormatRate(adapter.RxRateBps + adapter.TxRateBps, settings.UseBits)
            : "Disconnected";

        var ids = _monitor.AllAdapters.Select(adapter => adapter.Id).ToList();

        // Rebuild only when the set or the order of adapters actually changes. Anything that
        // merely *varies* — the live rate, a checkbox — is pushed into the existing rows,
        // because replacing the ItemsSource destroys the controls and takes any half-typed
        // rename with them.
        if (_adapterRows is not null && _adapterRows.Select(row => row.Id).SequenceEqual(ids, StringComparer.Ordinal))
        {
            foreach (var (row, adapter) in _adapterRows.Zip(_monitor.AllAdapters))
            {
                row.StatusText = Status(adapter);
                row.IsShown = !settings.IsAdapterHidden(adapter.Id);
            }

            UpdateAdapterCaption();
            return;
        }

        var rows = _monitor.AllAdapters
            .Select(adapter => new AdapterRow(
                adapter.Id,
                settings.AdapterDisplayName(adapter.Id, adapter.DisplayName),
                adapter.Description)
            {
                StatusText = Status(adapter),
                IsShown = !settings.IsAdapterHidden(adapter.Id),
            })
            .ToList();

        _adapterRows = rows;
        AdapterChecklist.ItemsSource = rows;
        UpdateAdapterCaption();
    }

    private List<AdapterRow>? _adapterRows;

    private void UpdateAdapterCaption()
    {
        var rows = _adapterRows ?? [];
        var hidden = rows.Count(row => !row.IsShown);
        AdapterCaption.Text = rows.Count == 0
            ? "No adapters reported yet."
            : hidden == 0
                ? $"{rows.Count} adapter(s). Untick one to keep it out of the popover."
                : $"{rows.Count} adapter(s), {hidden} hidden.";
    }

    /// <summary>Where a drag started, so a click can be told from the beginning of a drag.</summary>
    private Point _dragOrigin;
    private string? _dragAdapterId;

    private void OnAdapterRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only the handle starts a drag. Recording from anywhere on the row would mean a
        // click into the rename field could turn into a reorder as the caret was placed.
        if (e.OriginalSource is not TextBlock { Text: "⣿" } || sender is not FrameworkElement { Tag: string id })
        {
            _dragAdapterId = null;
            return;
        }

        _dragOrigin = e.GetPosition(this);
        _dragAdapterId = id;
    }

    private void OnAdapterRowMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragAdapterId is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // Windows' own drag threshold, so a shaky click is not a reorder.
        var moved = e.GetPosition(this) - _dragOrigin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _dragAdapterId;
        _dragAdapterId = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(AdapterDragFormat, dragged), DragDropEffects.Move);
    }

    private void OnAdapterRowDragOver(object sender, DragEventArgs e)
    {
        var carrying = e.Data.GetDataPresent(AdapterDragFormat);
        e.Effects = carrying ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        // An insertion line, so it is clear where the row will land rather than only which
        // row is under the pointer.
        if (carrying && sender is Border border)
        {
            border.BorderBrush = (Brush)Resources["AccentBrush"];
        }
    }

    private void OnAdapterRowDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = Brushes.Transparent;
        }
    }

    private void OnAdapterRowDrop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = Brushes.Transparent;
        }

        if (!e.Data.GetDataPresent(AdapterDragFormat) ||
            e.Data.GetData(AdapterDragFormat) is not string dragged ||
            sender is not FrameworkElement { Tag: string target } ||
            AdapterChecklist.ItemsSource is not IEnumerable<AdapterRow> rows)
        {
            return;
        }

        var order = rows.Select(row => row.Id).ToList();
        var targetIndex = order.FindIndex(id => string.Equals(id, target, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0 || string.Equals(dragged, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // The full sequence is handed over, not just the moved id: a position only means
        // anything relative to the list it sits in.
        _store.Batch(settings => settings.MoveAdapter(order, dragged, targetIndex));
        RefreshAdapterList();
    }

    private void OnResetAdapterOrder(object sender, RoutedEventArgs e)
    {
        _store.Batch(settings => settings.ResetAdapterOrder());
        RefreshAdapterList();
    }

    private void OnAdapterNameCommitted(object sender, RoutedEventArgs e)
        => CommitAdapterName(sender as TextBox);

    /// <summary>
    /// Applies a rename, from either Enter or losing focus.
    ///
    /// <para>Enter calls this directly rather than shuffling focus and letting LostFocus do
    /// it. <c>Keyboard.ClearFocus</c> moves keyboard focus without necessarily moving logical
    /// focus, so LostFocus may never fire and the rename would be silently dropped — which is
    /// exactly what it did.</para>
    /// </summary>
    private void CommitAdapterName(TextBox? box)
    {
        if (_loading || box is not { Tag: string adapterId })
        {
            return;
        }

        var typed = box.Text?.Trim();
        var current = _store.Settings.AdapterCustomNames.TryGetValue(adapterId, out var stored) ? stored : null;

        // The box shows the *effective* name, which for an unnamed adapter is the Windows
        // one. Committing that unchanged must not silently pin it as a custom label, or the
        // adapter would stop following its Windows name from then on.
        var windowsName = _monitor.AllAdapters
            .FirstOrDefault(adapter => string.Equals(adapter.Id, adapterId, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName;

        if (current is null && (string.IsNullOrEmpty(typed) || typed == windowsName))
        {
            return;
        }

        _store.Batch(settings => settings.SetAdapterName(adapterId, typed));
        RefreshAdapterList();
    }

    private void OnAdapterNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitAdapterName(box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.Text = _store.Settings.AdapterDisplayName(
                box.Tag as string ?? string.Empty,
                box.Text);

            e.Handled = true;
        }
    }

    private const string AdapterDragFormat = "NetFluss.AdapterId";

    // ======================================= DNS =======================================

    private readonly DnsController _dns = new();

    private sealed record DnsPresetRow(string Id, string Name, string ServersText, string Check);

    /// <summary>
    /// Rebuilds the DNS tab from the selected adapter's live resolvers.
    ///
    /// <para>Reading takes no privileges, so the whole tab — including which preset is
    /// currently active — is accurate in an ordinary session. Only Apply elevates.</para>
    /// </summary>
    private void RefreshDns()
    {
        var states = DnsController.Read();

        if (DnsAdapterBox.ItemsSource is not IEnumerable<Choice<string>> existing ||
            !existing.Select(c => c.Value).SequenceEqual(states.Select(s => s.AdapterName), StringComparer.Ordinal))
        {
            var previous = (DnsAdapterBox.SelectedItem as Choice<string>)?.Value;

            DnsAdapterBox.ItemsSource = states
                .Select(state => new Choice<string>(state.AdapterName, state.AdapterName))
                .ToArray();

            // Default to the adapter actually carrying traffic — on a laptop with a dozen
            // virtual interfaces, the first alphabetically is almost never the one meant.
            var busiest = _monitor.Adapters.FirstOrDefault()?.DisplayName;

            Select(DnsAdapterBox, previous
                                  ?? states.FirstOrDefault(s => s.AdapterName == busiest)?.AdapterName
                                  ?? states.FirstOrDefault()?.AdapterName
                                  ?? string.Empty);
        }

        var selected = (DnsAdapterBox.SelectedItem as Choice<string>)?.Value;
        var active = states.FirstOrDefault(s => s.AdapterName == selected)?.Servers ?? [];

        DnsCurrentCaption.Text = active.Count == 0
            ? "No DNS servers reported — this adapter is on automatic."
            : $"Currently {string.Join(", ", active)}";

        DnsPresetList.ItemsSource = _store.Settings.AllDnsPresets()
            .Select(preset => new DnsPresetRow(
                preset.Id,
                preset.Name,
                preset.IsAutomatic ? "From DHCP" : string.Join(", ", preset.Servers),
                preset.Matches(active) ? "✓" : string.Empty))
            .ToList();
    }

    private void OnDnsAdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            RefreshDns();
        }
    }

    private async void OnApplyDnsPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string presetId } button ||
            (DnsAdapterBox.SelectedItem as Choice<string>)?.Value is not { } adapter)
        {
            return;
        }

        var preset = _store.Settings.AllDnsPresets().FirstOrDefault(p => p.Id == presetId);
        if (preset is null)
        {
            return;
        }

        button.IsEnabled = false;
        DnsStatus.Text = $"Applying {preset.Name}… confirm the administrator prompt.";

        try
        {
            var result = await _dns.ApplyAsync(adapter, preset.Servers);
            DnsStatus.Text = result.Message;
        }
        finally
        {
            button.IsEnabled = true;
        }

        // Read back rather than assume: the checkmark should reflect what the adapter now
        // reports, not what was asked for.
        RefreshDns();
    }

    private void OnAddDnsPreset(object sender, RoutedEventArgs e)
    {
        var servers = DnsValidator.Parse(DnsNewServers.Text);
        DnsValidation result = DnsValidation.Ok;

        _store.Batch(settings => result = settings.AddDnsPreset(DnsNewName.Text, servers));

        if (!result.IsValid)
        {
            DnsStatus.Text = result.Error ?? "Could not add that preset.";
            return;
        }

        DnsNewName.Text = string.Empty;
        DnsNewServers.Text = string.Empty;
        DnsStatus.Text = "Preset added.";
        RefreshDns();
    }

    private void OnAdapterVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string adapterId } box)
        {
            return;
        }

        _store.Batch(settings => settings.SetAdapterHidden(adapterId, box.IsChecked != true));
        RefreshAdapterList();
    }

    private sealed record PreviewItem(BitmapSource Image, int Size, string Caption, Brush Swatch);
}
