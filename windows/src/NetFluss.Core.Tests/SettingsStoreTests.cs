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
