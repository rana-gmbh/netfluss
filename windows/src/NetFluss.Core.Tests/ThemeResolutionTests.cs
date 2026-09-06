// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

/// <summary>
/// The theme picker used to be write-only: Preferences stored <c>ThemeId</c> and nothing in
/// the app ever read it back, so choosing Dracula changed a line in the settings file and
/// nothing on screen. These pin the wiring that fixed it, and the precedence between a theme
/// and the per-row accent colours.
/// </summary>
public class ThemeResolutionTests
{
    private static readonly ThemeColor SystemDownload = ThemeColor.FromHex("4CC2FF");
    private static readonly ThemeColor SystemUpload = ThemeColor.FromHex("6CCB5F");

    [Fact]
    public void SystemTheme_UsesWhateverWindowsWouldUse()
    {
        var settings = new AppSettings { ThemeId = "system" };

        var (download, upload) = settings.ResolveRateColors(SystemDownload, SystemUpload);

        Assert.Equal(SystemDownload, download);
        Assert.Equal(SystemUpload, upload);
    }

    [Theory]
    [InlineData("dracula")]
    [InlineData("nord")]
    [InlineData("solarized")]
    public void ExplicitTheme_ReplacesTheSystemColours(string themeId)
    {
        var theme = AppTheme.Named(themeId);
        var settings = new AppSettings { ThemeId = themeId };

        var (download, upload) = settings.ResolveRateColors(SystemDownload, SystemUpload);

        Assert.Equal(theme.DownloadColor, download);
        Assert.Equal(theme.UploadColor, upload);
        Assert.NotEqual(SystemDownload, download);
    }

    /// <summary>
    /// A user who has pinned a colour keeps it. The theme sets the palette; an explicit
    /// accent is a decision about one row and outranks it, which is how macOS behaves.
    /// </summary>
    [Fact]
    public void ExplicitAccent_OutranksTheTheme()
    {
        var settings = new AppSettings
        {
            ThemeId = "dracula",
            UploadAccent = "orange",
        };

        var (download, upload) = settings.ResolveRateColors(SystemDownload, SystemUpload);

        // Upload was pinned, so it ignores Dracula.
        Assert.Equal(ThemeColor.FromHex("F7630C"), upload);

        // Download was left on Automatic, so it follows Dracula.
        Assert.Equal(AppTheme.Dracula.DownloadColor, download);
    }

    [Fact]
    public void CustomHex_OutranksTheThemeToo()
    {
        var settings = new AppSettings
        {
            ThemeId = "nord",
            DownloadAccent = "custom",
            DownloadCustomHex = "FF00FF",
        };

        var (download, _) = settings.ResolveRateColors(SystemDownload, SystemUpload);

        Assert.Equal(ThemeColor.FromHex("FF00FF"), download);
    }

    [Fact]
    public void UnknownThemeId_FallsBackToSystem()
    {
        var settings = new AppSettings { ThemeId = "monokai-that-was-never-shipped" };

        var (download, upload) = settings.ResolveRateColors(SystemDownload, SystemUpload);

        Assert.Equal(SystemDownload, download);
        Assert.Equal(SystemUpload, upload);
    }

    [Fact]
    public void SystemTheme_IsTheOnlyOneThatDefersToWindows()
    {
        Assert.False(AppTheme.System.IsExplicit);
        Assert.All(
            AppTheme.All.Where(t => t.Id != AppTheme.System.Id),
            theme => Assert.True(theme.IsExplicit));
    }

    /// <summary>
    /// The system theme has to answer for both Windows modes, because the popover it paints
    /// would otherwise be a dark panel on a light desktop.
    /// </summary>
    [Fact]
    public void SystemSurface_FollowsWindowsLightAndDark()
    {
        var light = AppTheme.System.Surface(systemIsLight: true);
        var dark = AppTheme.System.Surface(systemIsLight: false);

        Assert.False(light.IsDark);
        Assert.True(dark.IsDark);
        Assert.NotEqual(light.Background, dark.Background);

        // Text must not vanish into its own background in either mode.
        Assert.True(Contrast.Ratio(light.TextPrimary, light.Background) > 4.5);
        Assert.True(Contrast.Ratio(dark.TextPrimary, dark.Background) > 4.5);
    }

    [Theory]
    [InlineData("dracula")]
    [InlineData("nord")]
    [InlineData("solarized")]
    public void ExplicitSurface_IgnoresWindowsAndStaysReadable(string themeId)
    {
        var theme = AppTheme.Named(themeId);

        var asLight = theme.Surface(systemIsLight: true);
        var asDark = theme.Surface(systemIsLight: false);

        // An explicit theme means the same thing whatever Windows is doing.
        Assert.Equal(asLight, asDark);
        Assert.Equal(theme.BackgroundColor, asLight.Background);

        Assert.True(
            Contrast.Ratio(asLight.TextPrimary, asLight.Background) > 4.5,
            $"{themeId} primary text is unreadable on its own background");
    }

    /// <summary>No theme may leave a surface colour unresolved for the app layer to guess.</summary>
    [Fact]
    public void EverySurface_IsFullyPopulated()
    {
        foreach (var theme in AppTheme.All)
        {
            foreach (var systemIsLight in new[] { true, false })
            {
                var surface = theme.Surface(systemIsLight);

                Assert.NotEqual(default, surface.Background);
                Assert.NotEqual(default, surface.Card);
                Assert.NotEqual(default, surface.TextPrimary);
                Assert.NotEqual(default, surface.TextSecondary);
            }
        }
    }
}
