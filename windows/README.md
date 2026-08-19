# NetFluss for Windows

Native Windows port of NetFluss, tracking the macOS app's feature set.
Design and rationale live in [../docs/WINDOWS-PORT-PLAN.md](../docs/WINDOWS-PORT-PLAN.md).

**Status: Phase 0 (spike).** Live rates in the notification area, a minimal popover, and
the verification harness. No service, no VPN, no Preferences yet.

## Build

```
dotnet build windows/NetFluss.sln -c Release
dotnet test windows/NetFluss.sln -c Release
dotnet run --project windows/src/NetFluss.App
```

Requires the .NET 9 SDK on Windows 10 1809 or later. There is no Visual Studio
requirement — `NetFluss.sln` opens in VS 2022 17.11+ but the CLI is sufficient.

## Projects

| Project | Target | Role |
|---|---|---|
| `NetFluss.Core` | `net9.0` | Models, formatters, themes, localization. Platform-neutral **on purpose** so it builds and unit-tests off Windows. |
| `NetFluss.Native` | `net9.0-windows` | Win32 interop. Today: the IP Helper interface table. |
| `NetFluss.Tray` | `net9.0-windows` | Notification-area meter rendering. No WPF, so it runs headless. |
| `NetFluss.TrayPreview` | `net9.0-windows` | Renders the tray contact sheet. CI runs this and uploads the PNG. |
| `NetFluss.App` | `net9.0-windows` | WPF shell — tray host, timer, popover. |
| `*.Tests` | | xUnit. `Native.Tests` needs a real Windows host. |

## Two things that are easy to get wrong

**GDI handles.** `Bitmap.GetHicon()` returns an unmanaged icon nothing will free. The meter
repaints every second, so a leak here exhausts the 10,000-handle process limit within hours
and the app silently stops drawing. `TrayIconHost` assigns the new icon *then* destroys the
previous handle — never the current one, which the shell is still painting from.

**64-bit counters.** `System.Net.NetworkInformation` exposes 32-bit octet counters that wrap
every ~34 seconds on a saturated gigabit link. `NetFluss.Native` calls `GetIfTable2` for the
64-bit `InOctets`/`OutOctets` instead. `MIB_IF_ROW2` is hand-marshalled, and a wrong stride
does not throw — it yields a believable first row and garbage after it. `InterfaceTableTests`
walks every row and asserts each one is plausible, which is what catches that.

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

## Verifying without a Windows machine

CI on `windows-latest` is the verification loop. Every push touching `windows/**` builds,
runs both test suites, and uploads **`tray-contact-sheet.png`** — every tray layout rendered
at 16/20/24/32 px (100%/125%/150%/200% scaling) over light and dark taskbar swatches, at 6×
magnification with a 1:1 inset. That artifact is how the Phase 0 question gets answered:
*is a 16 px tray icon legible enough to be the default, or does the taskbar-overlay window
have to be first-class?*
