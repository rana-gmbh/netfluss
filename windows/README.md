# NetFluss for Windows

Native Windows port of NetFluss, tracking the macOS app's feature set.
Design and rationale live in [../docs/WINDOWS-PORT-PLAN.md](../docs/WINDOWS-PORT-PLAN.md).

**Status: Phase 1, in progress.** Live rates in the notification area, a minimal popover,
Preferences, and the verification harness. No service and no VPN yet.

## Build

```
dotnet build windows/NetFluss.sln -c Release
dotnet test windows/NetFluss.sln -c Release
dotnet run --project windows/src/NetFluss.App
```

Requires the .NET 10 SDK (LTS) on Windows 10 1809 or later. There is no Visual Studio
requirement — `NetFluss.sln` opens in VS 2022 17.11+ but the CLI is sufficient.

## Projects

| Project | Target | Role |
|---|---|---|
| `NetFluss.Core` | `net10.0` | Models, formatters, themes, localization. Platform-neutral **on purpose** so it builds and unit-tests off Windows. |
| `NetFluss.Native` | `net10.0-windows` | Win32 interop. Today: the IP Helper interface table. |
| `NetFluss.Tray` | `net10.0-windows` | Notification-area meter rendering. No WPF, so it runs headless. |
| `NetFluss.TrayPreview` | `net10.0-windows` | Renders the tray contact sheet. CI runs this and uploads the PNG. |
| `NetFluss.App` | `net10.0-windows` | WPF shell — tray host, timer, popover. |
| `*.Tests` | | xUnit. `Native.Tests` needs a real Windows host; `Tray.Tests` asserts on rendered pixels. |

## Three things that are easy to get wrong

**GDI handles.** `Bitmap.GetHicon()` returns an unmanaged icon nothing will free. The meter
repaints every second, so a leak here exhausts the 10,000-handle process limit within hours
and the app silently stops drawing. `TrayIconHost` assigns the new icon *then* destroys the
previous handle — never the current one, which the shell is still painting from.

**Small-icon legibility.** The macOS menu bar gives the meter a ~22 px strip as wide as it
likes. A Windows tray icon is a 16 px *square* at 100% DPI, and fitting "834K" across it
forces Segoe UI to ~6 px — below where TrueType hinting can hold a stem at one clean pixel.
`PixelFont` draws the digits from hand-built 3×5 and 4×7 grids as solid rectangles with
anti-aliasing off, integer-scaled only, so nothing is ever resampled. It is used exactly
where Segoe runs out of pixels (16 and 20 px) and nowhere else — at 24 and 32 px real
letterforms win, and an earlier revision that scored the two on ink height picked a doubled
3×5 face at 200%: taller, and visibly cruder.

The style is resolved **once per icon**, not per row. Two rows in different typefaces look
like a rendering fault, and sizing to whichever label Segoe measured widest clipped "118M"
to ".18M" at 16 px — the two fonts disagree about whether "118M" or "2.4M" is wider, because
the bitmap decimal point is one column and Segoe's is not. `EveryRow_FitsTheIcon` covers that.

**64-bit counters.** `System.Net.NetworkInformation` exposes 32-bit octet counters that wrap
every ~34 seconds on a saturated gigabit link. `NetFluss.Native` calls `GetIfTable2` for the
64-bit `InOctets`/`OutOctets` instead. `MIB_IF_ROW2` is hand-marshalled, and a wrong stride
does not throw — it yields a believable first row and garbage after it. `InterfaceTableTests`
walks every row and asserts each one is plausible, which is what catches that.

## Preferences and settings

`PreferencesWindow` follows Windows 11 Settings rather than the macOS tabbed `Form`: one
scrolling column of grouped cards, control on the right, changes applied and persisted
immediately with no OK button. It is hand-styled — the port plan names WPF-UI for the wider
Phase 1 UI, but Preferences needs four control types and a card, and a package that ships its
own theming would have to be reconciled with the NetFluss themes anyway.

The **preview strip** renders the real `TrayMeterRenderer` output at 16/20/24/32 px on the
user's actual taskbar colour. A 16 px icon is the whole difficulty of this port, so the
meter-style choice is shown rather than described.

Settings live in a JSON document at `%LOCALAPPDATA%\NetFluss\settings.json`, not the
registry: the macOS app keeps ordered lists in `UserDefaults` (adapter order, hidden
adapters, custom presets) and the registry has no ordered-collection story worth using. It is
written via write-then-replace, and a corrupt or unreadable file falls back to defaults — a
tray app has no window in which to report a load failure.

Two pieces of state are deliberately **not** in that file:

- **Start with Windows** lives in `HKCU\...\CurrentVersion\Run`, because writing it is what
  actually makes the app start and a user can remove it from Task Manager's Startup tab. The
  toggle always reads the registry back rather than trusting what was last written.
- **Light or dark** comes from `SystemUsesLightTheme` / `AppsUseLightTheme`. Windows exposes
  the shell and app themes independently, and the tray meter follows the *shell* one because
  that is what it is composited over.

## Localization

The macOS `Localizable.strings` catalogues are the **single source of truth for both
platforms**. Do not hand-edit the `.resx` files:

```
python3 windows/tools/strings2resx.py
```

It rewrites Cocoa `%@` specifiers to .NET `{0}` items and writes
`src/NetFluss.Core/Resources/platform-review.md` listing every string that mentions a
platform-specific concept — "Menu bar icon style" needs a Windows word, and the report is
where those decisions get tracked. CI runs the script with `--check` and fails if the
generated files are stale.

**Case-folded resource names.** .NET treats two resource names differing only in
capitalization as the same name; macOS `.strings` keys are case-sensitive, and NetFluss has
three pairs that differ only in case — a title-case heading beside the sentence-case control
that opens it (`Custom Date Range` / `Custom date range`). resgen's response is to drop one
and emit `MSB3568`, so the build stays green while a string vanishes from every language. It
is invisible in English, where the key doubles as the value, and only shows up as English
text leaking into German and Chinese.

The generator resolves it: the first key of each colliding group keeps its exact name and the
rest are stored as `key~2`, `key~3`, …, which `Localization.L` probes for when an exact lookup
misses. Call sites still pass the macOS key verbatim. `COLLISION_LIMIT` in the script and
`CollisionLimit` in `Localization.cs` must move together, and `LocalizationCaseCollisionTests`
fails if they don't. `MSB3568` is promoted to an error in `Directory.Build.props` so a future
collision breaks the build instead of warning — note it only fires on a full resgen, so
reproduce with `dotnet clean` first.

## Verifying without a Windows machine

CI on `windows-latest` is the verification loop. Every push touching `windows/**` builds,
runs both test suites, and uploads **`tray-contact-sheet.png`** — every tray layout rendered
at 16/20/24/32 px (100%/125%/150%/200% scaling) over light and dark taskbar swatches, at 6×
magnification with a 1:1 inset. That artifact is how the Phase 0 question gets answered:
*is a 16 px tray icon legible enough to be the default, or does the taskbar-overlay window
have to be first-class?*

### Phase 0 verdict

From the first green run's contact sheet:

- **The tray meter is viable as the default.** Two-line is clean at 24 px (150%) and 32 px
  (200%), good at 20 px (125%), and cramped but functional at 16 px (100%). Most current
  Windows laptops ship at 125–150%, so the common case is comfortable.
- **16 px is the weak spot**, which is precisely the case the opt-in taskbar-overlay window
  exists to serve. It stays a Phase 2+ item, not a Phase 0 blocker.
- **`DownloadOnly` should be offered at 16 px** — a single line gets the full icon height
  and is markedly sharper than two half-height rows.
- **Arrows off by default is correct.** `↓4.7M` visibly degrades against `4.7M` at 16–20 px;
  the glyph eats width the digits need, and the row colour already carries the meaning.
- **The upload green needs darkening on light taskbars.** At `#2ea043` on `#f3f3f3` it reads
  noticeably weaker than the download blue. Worth a contrast pass in Phase 1.

### What Phase 1 did about it

Both of the open items above are closed; the verdict above is kept as the record of what the
spike found.

- **16 px is no longer the weak spot.** `PixelFont` replaced the sub-hinting Segoe rows with
  a bitmap face, so two-line at 100% is now sharp rather than "cramped but functional".
  `BitmapFontRows_AreFullyOpaque` asserts it the only way that cannot flatter itself: every
  pixel the bitmap path draws is fully opaque, so no edge got softened.
- **The contrast gap was measured and closed.** The download blue scored 4.08:1 on the light
  taskbar against the upload green's 3.04:1 — the two rows disagreed about how important
  they were. `Contrast.EnsureRatio` now lifts both to WCAG AA (4.5:1) against whichever
  taskbar they are drawn on, stepping toward black or white so the hue survives.
  `ContrastTests` pins the original measurements and the correction.
- **`DownloadOnly` is still worth offering**, but no longer because two-line is illegible —
  it is now a preference for density, not a workaround.
