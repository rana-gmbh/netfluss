// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Collections;
using System.Globalization;
using System.Resources;
using NetFluss.Core;
using Xunit;

namespace NetFluss.Core.Tests;

/// <summary>
/// Pins both halves of the case-collision contract between <c>strings2resx.py</c> and
/// <see cref="Localization"/>.
///
/// <para>macOS .strings keys are case-sensitive; .NET resource names are not. NetFluss has
/// three key pairs that differ only in capitalization — a title-case heading beside a
/// sentence-case control label — and resgen's response to those is to drop one and emit
/// MSB3568, leaving a build that is green but missing a string in every language. The
/// generator resolves it by suffixing the loser with "~2" and <see cref="Localization.L"/>
/// probes for that on a miss.</para>
///
/// <para>These tests fail if either side changes alone, and
/// <see cref="ResourceNames_DoNotFoldTogether"/> fails if a new collision is ever shipped
/// unresolved — which is the failure that started all this.</para>
/// </summary>
public class LocalizationCaseCollisionTests : IDisposable
{
    private static readonly string[] Cultures = ["en", "de", "zh-Hans", "zh-Hant"];

    public void Dispose() => Localization.Use(AppLanguage.System);

    [Theory]
    // The English values differ only in capitalization, which is the entire point: one is
    // a popover heading, the other the control that opens it.
    [InlineData(AppLanguage.English, "Custom date range", "Custom date range")]
    [InlineData(AppLanguage.English, "Custom Date Range", "Custom Date Range")]
    [InlineData(AppLanguage.English, "Custom Color", "Custom Color")]
    [InlineData(AppLanguage.English, "Custom color", "Custom color")]
    [InlineData(AppLanguage.English, "Add Note", "Add Note")]
    [InlineData(AppLanguage.English, "Add note", "Add note")]
    // In the translated catalogues both halves of a pair share one value, so the bug this
    // guards is invisible in English and only shows up here.
    [InlineData(AppLanguage.German, "Custom date range", "Eigener Zeitraum")]
    [InlineData(AppLanguage.German, "Custom Date Range", "Eigener Zeitraum")]
    [InlineData(AppLanguage.German, "Custom Color", "Eigene Farbe")]
    [InlineData(AppLanguage.German, "Custom color", "Eigene Farbe")]
    [InlineData(AppLanguage.German, "Add Note", "Notiz hinzufügen")]
    [InlineData(AppLanguage.German, "Add note", "Notiz hinzufügen")]
    [InlineData(AppLanguage.SimplifiedChinese, "Custom date range", "自定义日期范围")]
    [InlineData(AppLanguage.SimplifiedChinese, "Custom Date Range", "自定义日期范围")]
    [InlineData(AppLanguage.SimplifiedChinese, "Custom Color", "自定义颜色")]
    [InlineData(AppLanguage.SimplifiedChinese, "Custom color", "自定义颜色")]
    [InlineData(AppLanguage.SimplifiedChinese, "Add Note", "添加备注")]
    [InlineData(AppLanguage.SimplifiedChinese, "Add note", "添加备注")]
    [InlineData(AppLanguage.TraditionalChinese, "Custom date range", "自訂日期範圍")]
    [InlineData(AppLanguage.TraditionalChinese, "Custom Date Range", "自訂日期範圍")]
    [InlineData(AppLanguage.TraditionalChinese, "Custom Color", "自訂顏色")]
    [InlineData(AppLanguage.TraditionalChinese, "Custom color", "自訂顏色")]
    [InlineData(AppLanguage.TraditionalChinese, "Add Note", "新增備註")]
    [InlineData(AppLanguage.TraditionalChinese, "Add note", "新增備註")]
    public void CollidingKeys_BothResolve(AppLanguage language, string key, string expected)
    {
        Localization.Use(language);
        Assert.Equal(expected, Localization.L(key));
    }

    /// <summary>
    /// The regression itself: before the fix, the dropped key fell through to the key-name
    /// fallback, so German showed the English text.
    /// </summary>
    [Fact]
    public void CollidingKeys_DoNotFallBackToTheKeyName()
    {
        Localization.Use(AppLanguage.German);

        Assert.NotEqual("Custom Date Range", Localization.L("Custom Date Range"));
        Assert.NotEqual("Custom color", Localization.L("Custom color"));
        Assert.NotEqual("Add note", Localization.L("Add note"));
    }

    /// <summary>An ordinary key must still take the fast path and resolve unchanged.</summary>
    [Fact]
    public void NonCollidingKey_StillResolves()
    {
        Localization.Use(AppLanguage.German);
        Assert.Equal("Eigener Zeitraum", Localization.L("Custom date range"));
    }

    /// <summary>Probing must not turn a genuinely absent key into something else.</summary>
    [Fact]
    public void MissingKey_ReturnsTheKey()
    {
        Localization.Use(AppLanguage.English);
        Assert.Equal("Nicht vorhanden ~ key", Localization.L("Nicht vorhanden ~ key"));
    }

    /// <summary>
    /// Catches a future collision that was shipped rather than disambiguated. This does not
    /// depend on the suffix convention — it just asserts that what we ship is something
    /// .NET can actually store, which is the invariant MSB3568 was complaining about.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void ResourceNames_DoNotFoldTogether(string culture)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in ResourceNames(culture))
        {
            var folded = name.ToLowerInvariant();
            Assert.False(
                seen.TryGetValue(folded, out var existing),
                $"{culture}: '{name}' and '{existing}' are the same resource name to .NET; " +
                "regenerate with windows/tools/strings2resx.py");

            seen[folded] = name;
        }
    }

    /// <summary>Every language must carry the same resource names, or a culture goes blank.</summary>
    [Fact]
    public void AllCultures_CarryTheSameResourceNames()
    {
        var neutral = ResourceNames("en").ToHashSet(StringComparer.Ordinal);

        foreach (var culture in Cultures)
        {
            Assert.Equal(neutral.OrderBy(n => n, StringComparer.Ordinal),
                         ResourceNames(culture).OrderBy(n => n, StringComparer.Ordinal));
        }
    }

    private static List<string> ResourceNames(string culture)
    {
        var manager = new ResourceManager("NetFluss.Core.Resources.Strings", typeof(Localization).Assembly);

        // tryParents: false so "de" returns the German file itself rather than silently
        // falling back to the neutral one and comparing it against itself.
        var set = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: culture == "en")
                  ?? throw new InvalidOperationException($"no resource set for '{culture}'");

        var names = new List<string>();
        foreach (DictionaryEntry entry in set)
        {
            names.Add((string)entry.Key);
        }

        return names;
    }
}
