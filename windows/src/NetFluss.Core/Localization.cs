// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Globalization;
using System.Resources;

namespace NetFluss.Core;

/// <summary>Language choices offered in Preferences, matching the macOS <c>AppLanguage</c>.</summary>
public enum AppLanguage
{
    System,
    English,
    German,
    SimplifiedChinese,
    TraditionalChinese,
}

/// <summary>
/// String lookup. Keys are the English source strings, exactly as on macOS — the .resx
/// files are generated from the same Localizable.strings catalogues by
/// <c>windows/tools/strings2resx.py</c>, so a key that works on the Mac works here.
///
/// <para>A missing key returns the key itself, which is what <c>NSLocalizedString</c> does
/// and what keeps a half-translated build readable rather than blank.</para>
/// </summary>
public static class Localization
{
    private static readonly ResourceManager Resources =
        new("NetFluss.Core.Resources.Strings", typeof(Localization).Assembly);

    private static CultureInfo? _override;

    /// <summary>
    /// .NET folds resource names, so two macOS keys differing only in capitalization
    /// cannot both be stored under their own name — resgen drops the later one with a
    /// warning. NetFluss has three such pairs, each a title-case heading beside a
    /// sentence-case control label, and both halves must stay reachable in German and
    /// Chinese.
    ///
    /// <para><c>strings2resx.py</c> therefore keeps the first of each colliding group
    /// under its exact name and appends "~2", "~3", … to the rest. Probing those suffixes
    /// on a miss reverses it, which keeps the promise above: call sites pass the macOS key
    /// verbatim and never learn that any of this happened.</para>
    /// </summary>
    private const char CollisionSuffix = '~';

    /// <summary>Must stay equal to <c>COLLISION_LIMIT</c> in <c>strings2resx.py</c>.</summary>
    private const int CollisionLimit = 9;

    public static AppLanguage Current { get; private set; } = AppLanguage.System;

    public static void Use(AppLanguage language)
    {
        Current = language;
        _override = CultureFor(language);
    }

    /// <summary>Localized string for <paramref name="key"/>, or the key when untranslated.</summary>
    public static string L(string key)
    {
        var culture = _override ?? CultureInfo.CurrentUICulture;

        // The overwhelming majority of keys are stored under their own name and resolve here.
        var value = Resources.GetString(key, culture);
        if (value is not null)
        {
            return value;
        }

        // Only a key that lost a case collision reaches this loop, and only a genuinely
        // absent key runs it to completion — which already means a bug, so the extra
        // lookups cost nothing that matters.
        for (var ordinal = 2; ordinal <= CollisionLimit; ordinal++)
        {
            value = Resources.GetString($"{key}{CollisionSuffix}{ordinal}", culture);
            if (value is not null)
            {
                return value;
            }
        }

        return key;
    }

    /// <summary>Localized string with composite formatting, e.g. <c>L("Collecting since {0}", date)</c>.</summary>
    public static string L(string key, params object?[] args)
    {
        var format = L(key);
        try
        {
            return string.Format(_override ?? CultureInfo.CurrentUICulture, format, args);
        }
        catch (FormatException)
        {
            // A translator can break the placeholders; showing the unformatted string beats
            // crashing a menu-bar app that has no window to show an error in.
            return format;
        }
    }

    public static CultureInfo? CultureFor(AppLanguage language) => language switch
    {
        AppLanguage.System => null,
        AppLanguage.English => CultureInfo.GetCultureInfo("en"),
        AppLanguage.German => CultureInfo.GetCultureInfo("de"),
        AppLanguage.SimplifiedChinese => CultureInfo.GetCultureInfo("zh-Hans"),
        AppLanguage.TraditionalChinese => CultureInfo.GetCultureInfo("zh-Hant"),
        _ => null,
    };

    public static string DisplayName(AppLanguage language) => language switch
    {
        AppLanguage.System => L("System Default"),
        AppLanguage.English => "English",
        AppLanguage.German => "Deutsch",
        AppLanguage.SimplifiedChinese => "简体中文",
        AppLanguage.TraditionalChinese => "繁體中文",
        _ => "English",
    };
}
