// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

/// <summary>
/// Settings persistence. The defaults matter as much as the round-trip: they are the
/// registration list, and a fresh install on Windows should behave like a fresh install on
/// macOS rather than quietly differing.
/// </summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "netfluss-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Defaults_MatchTheMacOsRegistrationList()
    {
        var settings = new AppSettings();

        Assert.Equal(1, settings.RefreshIntervalSeconds);
        Assert.False(settings.UseBits);
        Assert.Equal(MeterStyle.TwoLine, settings.MeterStyle);
        Assert.Equal(AppLanguage.System, settings.Language);
        Assert.Equal("system", settings.ThemeId);

        // Both decided by the Phase 0 contact sheet; see windows/README.md.
        Assert.False(settings.ShowArrows);
        Assert.True(settings.EnforceContrast);
    }

    [Fact]
    public void Changes_SurviveAReload()
    {
        var first = new SettingsStore(Path_);
        first.Batch(settings =>
        {
            settings.MeterStyle = MeterStyle.DownloadOnly;
            settings.UseBits = true;
            settings.RefreshIntervalSeconds = 3;
            settings.Language = AppLanguage.German;
            settings.DownloadAccent = "teal";
            settings.AdapterOrder = ["{A}", "{B}"];
            settings.AdapterCustomNames["{A}"] = "Office";
        });

        var reloaded = new SettingsStore(Path_).Settings;

        Assert.Equal(MeterStyle.DownloadOnly, reloaded.MeterStyle);
        Assert.True(reloaded.UseBits);
        Assert.Equal(3, reloaded.RefreshIntervalSeconds);
        Assert.Equal(AppLanguage.German, reloaded.Language);
        Assert.Equal("teal", reloaded.DownloadAccent);
        Assert.Equal(["{A}", "{B}"], reloaded.AdapterOrder);
        Assert.Equal("Office", reloaded.AdapterCustomNames["{A}"]);
    }

    /// <summary>Enums must persist by name, or reordering the enum would silently remap settings.</summary>
    [Fact]
    public void Enums_ArePersistedByName()
    {
        var store = new SettingsStore(Path_);
        store.Batch(settings =>
        {
            settings.MeterStyle = MeterStyle.UploadOnly;
            settings.Language = AppLanguage.TraditionalChinese;
        });

        var json = File.ReadAllText(Path_);

        Assert.Contains("\"UploadOnly\"", json);
        Assert.Contains("\"TraditionalChinese\"", json);
    }

    [Fact]
    public void SingleChange_IsPersistedWithoutABatch()
    {
        var store = new SettingsStore(Path_);
        store.Settings.UseBits = true;

        Assert.True(new SettingsStore(Path_).Settings.UseBits);
    }

    [Fact]
    public void Changed_FiresOncePerBatch()
    {
        var store = new SettingsStore(Path_);
        var fired = 0;
        store.Changed += (_, _) => fired++;

        store.Batch(settings =>
        {
            settings.UseBits = true;
            settings.ShowArrows = true;
            settings.MeterStyle = MeterStyle.Icon;
        });

        Assert.Equal(1, fired);
    }

    /// <summary>
    /// A half-written or hand-mangled file must not stop the app launching. A tray app has
    /// no window to report a load failure in, so defaults are the only sane outcome.
    /// </summary>
    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, "{ this is not json");

        Assert.Equal(MeterStyle.TwoLine, new SettingsStore(Path_).Settings.MeterStyle);
    }

    [Fact]
    public void MissingFile_FallsBackToDefaults()
        => Assert.Equal(1, new SettingsStore(Path_).Settings.RefreshIntervalSeconds);

    /// <summary>Unknown keys from a newer build must not throw away the rest of the file.</summary>
    [Fact]
    public void UnknownKeys_AreIgnored()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, """{ "UseBits": true, "SomethingFromTheFuture": 42 }""");

        Assert.True(new SettingsStore(Path_).Settings.UseBits);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.2, 1)]
    [InlineData(3, 3)]
    [InlineData(99, 5)]
    public void RefreshInterval_IsClampedToTheOfferedRange(double set, double expected)
    {
        var settings = new AppSettings { RefreshIntervalSeconds = set };
        Assert.Equal(expected, settings.RefreshIntervalSeconds);
    }

    /// <summary>
    /// Every default must be serialisable, checked by writing rather than by inspection.
    ///
    /// <para>Regression: the floating widget's "not placed yet" position was
    /// <c>double.NaN</c>, which System.Text.Json refuses to write at all. Since the store
    /// saves on every property change, that turned the very first settings change into an
    /// ArgumentException that <c>Save</c> does not catch — the app would have thrown the
    /// first time anyone touched Preferences.</para>
    /// </summary>
    [Fact]
    public void DefaultSettings_CanBeWritten()
    {
        var store = new SettingsStore(Path_);

        // Touch one property to trigger a real save through the normal path.
        store.Settings.UseBits = true;

        Assert.True(File.Exists(Path_), "saving the default settings produced no file");
        Assert.DoesNotContain("NaN", File.ReadAllText(Path_), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnplacedWidget_RoundTripsAsAbsent()
    {
        var store = new SettingsStore(Path_);
        store.Settings.ShowFloatingWidget = true;

        var reloaded = new SettingsStore(Path_).Settings;

        Assert.Null(reloaded.FloatingWidgetLeft);
        Assert.Null(reloaded.FloatingWidgetTop);
    }

    [Fact]
    public void PlacedWidget_RemembersItsPosition()
    {
        var store = new SettingsStore(Path_);
        store.Batch(settings =>
        {
            settings.FloatingWidgetLeft = 1234.5;
            settings.FloatingWidgetTop = 678.5;
        });

        var reloaded = new SettingsStore(Path_).Settings;

        Assert.Equal(1234.5, reloaded.FloatingWidgetLeft);
        Assert.Equal(678.5, reloaded.FloatingWidgetTop);
    }

    [Fact]
    public void NewSurfaceDefaults_MatchTheChosenBehaviour()
    {
        var settings = new AppSettings();

        // The overlay is the default placement; the tray is what it falls back to.
        Assert.Equal(MeterSurface.TaskbarOverlay, settings.MeterSurface);
        Assert.Equal(ReadoutStyle.Unified, settings.ReadoutStyle);
        Assert.Equal(11, settings.ReadoutFontSize);
        Assert.False(settings.ShowFloatingWidget);
        Assert.Equal("netfluss", settings.TrayIconGlyph);
    }

    [Theory]
    [InlineData(2, 8)]
    [InlineData(11, 11)]
    [InlineData(40, 16)]
    public void ReadoutFontSize_IsClampedToTheMacOsRange(double set, double expected)
        => Assert.Equal(expected, new AppSettings { ReadoutFontSize = set }.ReadoutFontSize);

    /// <summary>
    /// The notification-area icon must be present on a fresh install, whatever surface the
    /// meter is on.
    ///
    /// <para>Regression, and the reason this test is worth its length: the tray icon used to
    /// hide itself automatically whenever the taskbar overlay anchored. A user who also
    /// turned off the floating widget was then left with no visible NetFluss icon anywhere —
    /// no discoverable route to Preferences, and no way to quit the app short of Task
    /// Manager. Hiding it is now something the user asks for explicitly.</para>
    /// </summary>
    [Fact]
    public void TrayIcon_IsNotHiddenByDefault()
    {
        var settings = new AppSettings();

        Assert.Equal(MeterSurface.TaskbarOverlay, settings.MeterSurface);
        Assert.False(settings.ShowFloatingWidget);
        Assert.False(settings.HideTrayIcon);
    }

    [Fact]
    public void AccentResolution_FallsBackForSystem()
    {
        var settings = new AppSettings { DownloadAccent = "system" };
        var fallback = ThemeColor.FromHex("ABCDEF");

        Assert.Equal(fallback, settings.ResolveDownloadColor(fallback));
    }

    [Fact]
    public void AccentResolution_UsesTheNamedPalette()
    {
        var settings = new AppSettings { UploadAccent = "teal" };

        Assert.Equal(ThemeColor.FromHex("00B7C3"), settings.ResolveUploadColor(ThemeColor.FromHex("000000")));
    }
}
