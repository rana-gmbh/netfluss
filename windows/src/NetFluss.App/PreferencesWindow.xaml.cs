// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly TrayMeterRenderer _renderer = new();
    private bool _loading;

    public PreferencesWindow(SettingsStore store)
    {
        InitializeComponent();

        _store = store;

        ApplyTheme();
        PopulateChoices();
        LoadFromSettings();
        RefreshPreview();

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
        var (downloadInk, uploadInk) = SystemTheme.DefaultInk();
        var swatch = new SolidColorBrush(Color.FromRgb(taskbar.R, taskbar.G, taskbar.B));

        var items = new List<PreviewItem>(PreviewSizes.Length);

        foreach (var size in PreviewSizes)
        {
            var options = new TrayMeterOptions
            {
                Size = size,
                Layout = ToLayout(settings.MeterStyle),
                DownloadColor = settings.ResolveDownloadColor(downloadInk),
                UploadColor = settings.ResolveUploadColor(uploadInk),
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

        UpdateSwatch(DownloadSwatch, settings.ResolveDownloadColor(downloadInk), taskbar, settings.EnforceContrast);
        UpdateSwatch(UploadSwatch, settings.ResolveUploadColor(uploadInk), taskbar, settings.EnforceContrast);

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
            DownloadColor = settings.ResolveDownloadColor(downloadInk),
            UploadColor = settings.ResolveUploadColor(uploadInk),
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

    private sealed record PreviewItem(BitmapSource Image, int Size, string Caption, Brush Swatch);
}
