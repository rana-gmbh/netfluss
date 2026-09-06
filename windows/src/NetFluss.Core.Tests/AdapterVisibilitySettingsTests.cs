// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

/// <summary>
/// The bridge between the stored preferences and the filter that <c>AdapterFilterTests</c>
/// already covers. It is the piece that was missing: <c>HiddenAdapters</c> was saved and
/// reloaded correctly and never once reached the monitor, so unticking an adapter changed
/// the settings file and nothing else.
/// </summary>
public class AdapterVisibilitySettingsTests
{
    private static AdapterStatus Adapter(string id, bool up = true, double rate = 100) => new()
    {
        Id = id,
        DisplayName = id,
        Description = id,
        Type = AdapterType.Ethernet,
        IsTunnel = false,
        IsNonInternet = false,
        IsUp = up,
        RxBytes = 0,
        TxBytes = 0,
        RxRateBps = rate,
        TxRateBps = 0,
    };

    [Fact]
    public void HidingAnAdapter_RemovesItFromTheVisibleSet()
    {
        var settings = new AppSettings();
        settings.SetAdapterHidden("{B}", true);

        var visible = AdapterTotalsFilter.VisibleAdapters(
            [Adapter("{A}"), Adapter("{B}")],
            settings.VisibilityOptions());

        Assert.Equal(["{A}"], visible.Select(a => a.Id));
    }

    [Fact]
    public void UnhidingPutsItBack()
    {
        var settings = new AppSettings();

        settings.SetAdapterHidden("{B}", true);
        Assert.True(settings.IsAdapterHidden("{B}"));

        settings.SetAdapterHidden("{B}", false);
        Assert.False(settings.IsAdapterHidden("{B}"));
        Assert.Empty(settings.HiddenAdapters);
    }

    /// <summary>Ticking a box that is already in that state must not append a duplicate.</summary>
    [Fact]
    public void HidingTwice_DoesNotDuplicate()
    {
        var settings = new AppSettings();

        settings.SetAdapterHidden("{B}", true);
        settings.SetAdapterHidden("{B}", true);

        Assert.Single(settings.HiddenAdapters);
    }

    /// <summary>
    /// Interface GUIDs come back from Windows with inconsistent bracket casing, so a
    /// case-sensitive comparison would hide an adapter that then reappears next tick.
    /// </summary>
    [Fact]
    public void HiddenMatching_IgnoresCase()
    {
        var settings = new AppSettings();
        settings.SetAdapterHidden("{abc}", true);

        Assert.True(settings.IsAdapterHidden("{ABC}"));

        var visible = AdapterTotalsFilter.VisibleAdapters(
            [Adapter("{ABC}")],
            settings.VisibilityOptions());

        Assert.Empty(visible);
    }

    /// <summary>
    /// A List is compared by reference, so mutating it in place would not raise
    /// PropertyChanged and the store would never save. This is what SetAdapterHidden
    /// replacing the list is for.
    /// </summary>
    [Fact]
    public void HidingAnAdapter_RaisesChangeNotification()
    {
        var settings = new AppSettings();
        var raised = 0;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.HiddenAdapters))
            {
                raised++;
            }
        };

        settings.SetAdapterHidden("{B}", true);

        Assert.True(raised > 0, "hiding an adapter raised no change, so nothing would persist it");
    }

    [Fact]
    public void ShowInactive_ControlsDisconnectedAdapters()
    {
        var down = Adapter("{D}", up: false, rate: 0);

        Assert.Empty(AdapterTotalsFilter.VisibleAdapters(
            [down],
            new AppSettings { ShowInactiveAdapters = false }.VisibilityOptions()));

        Assert.Single(AdapterTotalsFilter.VisibleAdapters(
            [down],
            new AppSettings { ShowInactiveAdapters = true }.VisibilityOptions()));
    }

    [Fact]
    public void VisibilityOptions_CarryEverySetting()
    {
        var settings = new AppSettings
        {
            ShowInactiveAdapters = true,
            ShowOtherAdapters = false,
        };

        settings.SetAdapterHidden("{X}", true);
        var options = settings.VisibilityOptions();

        Assert.True(options.ShowInactive);
        Assert.False(options.ShowOtherAdapters);
        Assert.Contains("{X}", options.Hidden);
    }

    [Theory]
    [InlineData(100, 280)]
    [InlineData(320, 320)]
    [InlineData(5000, 900)]
    public void PopoverWidth_IsClamped(double set, double expected)
        => Assert.Equal(expected, new AppSettings { PopoverWidth = set }.PopoverWidth);

    [Theory]
    [InlineData(10, 220)]
    [InlineData(460, 460)]
    [InlineData(9000, 1200)]
    public void PopoverHeight_IsClamped(double set, double expected)
        => Assert.Equal(expected, new AppSettings { PopoverHeight = set }.PopoverHeight);

    [Fact]
    public void PopoverSize_SurvivesAReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netfluss-popover-{Guid.NewGuid():N}.json");

        try
        {
            var store = new SettingsStore(path);
            store.Batch(settings =>
            {
                settings.PopoverWidth = 512;
                settings.PopoverHeight = 640;
            });

            var reloaded = new SettingsStore(path).Settings;

            Assert.Equal(512, reloaded.PopoverWidth);
            Assert.Equal(640, reloaded.PopoverHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
