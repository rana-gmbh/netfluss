# NetFluss for Windows — Port Plan

**Goal:** a native Windows NetFluss at feature parity with macOS 2.5, distributed from GitHub *and* the Microsoft Store from a single signed build.

**Scope reference:** macOS NetFluss 2.5 — ~23,500 lines of Swift across 55 files (menu bar, popover, Network Slice, VPN client, Statistics, Speed Test, 4 router integrations, Wi-Fi manager, DNS switcher, 4 languages × 400 strings).

---

## 1. Recommended stack

| Layer | Choice | Why |
|---|---|---|
| Runtime | **.NET 9**, self-contained, x64 + ARM64 | No runtime prerequisite; ARM64 matters for Snapdragon X laptops |
| UI | **WPF** + [WPF-UI](https://github.com/lepoco/wpfui) (Fluent/Mica theming) | The whole app is Win32 interop — tray icons, layered overlay windows, topmost popovers. WPF's interop is unrestricted and mature |
| Tray | **H.NotifyIcon** | The maintained NotifyIcon for .NET; rich popups + context menus |
| Win32 bindings | **Microsoft.Windows.CsWin32** (source generator) | Typed P/Invoke for `iphlpapi`, `wlanapi`, `rasapi32`, `dnsapi` generated from a text file — saves weeks of hand-written interop |
| ETW | **Microsoft.Diagnostics.Tracing.TraceEvent** | Per-process network events |
| Charts | **LiveCharts2** (SkiaSharp) | Closest match to SwiftUI Charts for the Statistics window |
| Web | **WebView2** | Runs the existing Speed Test HTML/JS **verbatim** |
| Installer | **WiX v5** → MSI | Needs a per-machine install for the service |

### Why not WinUI 3
WinUI 3 has **no** notification-area support (you re-implement it via Win32 interop anyway), adds packaging friction, and is a poor fit for owner-drawn taskbar overlay windows. WPF gives the identical Fluent look through WPF-UI with none of that.

### Why not Avalonia / Swift-on-Windows
Avalonia would trade a native Windows app for a lowest-common-denominator one. Swift builds on Windows but **SwiftUI does not exist there** — you would rewrite the entire UI layer regardless and inherit an unsupported toolchain. Rewriting in C# is the shorter path.

---

## 2. The taskbar question — three display modes

Windows has no menu-bar text area, and **Microsoft removed the DeskBand API in Windows 11**, which is what killed NetSpeedMonitor and DU Meter's taskbar band. There is no supported replacement. Ship all three of these, user-selectable:

### Mode 1 — Tray icon meter *(default, always supported)*
Render a two-line bitmap into the notification-area icon on every tick (`↑ 1.2M` / `↓ 8.4M`), regenerating the `HICON` at the DPI-correct size (`SM_CXSMICON`, 16px → 24/32px at 150/200%). This is what every surviving Windows net meter does. Robust, survives Windows updates, works identically on Windows 10 and 11. Icon mode from the Mac version maps to a single static glyph.

> **Onboarding gotcha:** Windows 11 hides new tray icons in the overflow flyout by default — a user who installs NetFluss sees *nothing*. Mitigate with a first-run card explaining how to pin it, and have the installer write `IsPromoted=1` under `HKCU\Control Panel\NotifyIconSettings` for the app's icon so it appears in the tray immediately.

### Mode 2 — Taskbar meter *(opt-in, best-effort)*
A topmost `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` layered window positioned directly over the taskbar, left of the tray: locate `Shell_TrayWnd` → `TrayNotifyWnd`, compute the rect, and re-anchor on `WM_DISPLAYCHANGE`, DPI change, taskbar move/auto-hide and `TaskbarCreated`. This is the only way to get real DU-Meter-style text with the NetFluss themes and per-element colours. Label it in Preferences as best-effort — a Windows update can move the taskbar out from under it.

### Mode 3 — Floating widget
The existing **Pin** feature, as a proper always-on-top tool window. Zero platform risk, and the natural home for the "live widget" use case. A Windows 11 Widgets-board tile is a possible later addition, not a v1 item.

### Popover
A WPF window opened from the tray on left-click, positioned above the tray with the same edge-awareness logic as macOS, `WS_EX_TOOLWINDOW`, dismiss-on-deactivate, and Mica backdrop on Windows 11 via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`. Right-click → context menu (Speed Test, Network Slice, Preferences, About, Quit), matching macOS exactly.

---

## 3. Architecture — app + service

This mirrors the macOS app + privileged-helper split, and for the same reason: several features need elevation.

```
NetFluss.exe            (user session, unelevated)
  ├── Tray meter / popover / Preferences / Statistics / Network Slice / Speed Test
  ├── Adapter counters      GetIfTable2            — no admin
  ├── Wi-Fi info & join     wlanapi                — no admin
  ├── IP / gateway          GetBestRoute2          — no admin
  ├── Router monitors       HttpClient             — no admin
  └── IKEv2 / L2TP VPN      RasDial                — no admin
        │
        │  named pipe (ACL: interactive users), message schema mirroring
        │  NetflussHelperProtocol
        ▼
NetFluss.Service.exe    (LocalSystem, auto-start, installed by the MSI)
  ├── ETW Kernel-Network session  → Top Apps, Network Slice, app statistics
  ├── DNS switching               SetInterfaceDnsSettings
  ├── Adapter enable/disable      (the ↺ reconnect button)
  ├── OpenVPN process supervision (openvpn.exe + wintun)
  └── WireGuard tunnel            embeddable-dll-service + wireguard-nt
```

**Design rule: the app must be fully useful without the service.** Rates, adapters, Wi-Fi, IP/flow, routers, Speed Test, and adapter-level Statistics all work unelevated. Only Top Apps, Network Slice, app statistics, DNS switching, adapter reset, and OpenVPN/WireGuard require it. That preserves a portable no-install build and mirrors how the Mac helper is optional today.

### Solution layout

```
windows/
  src/NetFluss.App/        WPF — tray, popover, Preferences, Statistics, Slice, SpeedTest
  src/NetFluss.Core/       models, formatters, themes, localization, statistics store
  src/NetFluss.Native/     CsWin32 P/Invoke surface
  src/NetFluss.Routers/    FritzBox, UniFi, OpenWRT, OPNsense
  src/NetFluss.Service/    Windows service (ETW, DNS, VPN)
  src/NetFluss.Ipc/        shared pipe contracts
  installer/               WiX v5
  assets/                  icons, flags
  .github/workflows/       build → sign → release → winget
```

---

## 4. Feature-by-feature mapping

### Core metering
| macOS | Windows | Notes |
|---|---|---|
| `getifaddrs` / `ifi_ibytes` | `GetIfTable2` → `MIB_IF_ROW2.InOctets/OutOctets` | 64-bit counters. **The macOS 26.5 frozen-`ifi_ibytes` bug has no Windows equivalent** — the whole `NetworkStatisticsClient` fallback path disappears |
| `ifi_baudrate` link speed | `ReceiveLinkSpeed` / `TransmitLinkSpeed` | |
| Adapter type / loopback / tunnel filtering | `MIB_IF_ROW2.Type`, `MediaType`, `InterfaceAndOperStatusFlags` | Exclusion logic ports 1:1 |
| Refresh timer | `DispatcherTimer` / `PeriodicTimer` | Suspend on session lock via `SystemEvents.SessionSwitch` — keep the energy discipline |

### Wi-Fi
| macOS (CoreWLAN) | Windows (Native Wifi API) |
|---|---|
| SSID, BSSID, band, channel, Tx rate, standard, security | `WlanQueryInterface(wlan_intf_opcode_current_connection)` → `WLAN_CONNECTION_ATTRIBUTES` + `WLAN_BSS_ENTRY` |
| RSSI | `WLAN_BSS_ENTRY.lRssi` |
| **Noise / SNR** | ❌ **Not exposed by the Windows WLAN API** — the (i) detail popover loses two fields |
| Scan + list networks | `WlanScan` + `WlanGetAvailableNetworkList` |
| Join network + persist password | `WlanSetProfile` (XML profile) + `WlanConnect` — **per-user, no admin needed**, and the profile is reused by the Windows Wi-Fi flyout afterwards, exactly like the Mac Known Networks behaviour |
| Location permission prompt | Same requirement: Windows 11 24H2+ gates Wi-Fi scan results behind Location — pleasing symmetry with the macOS CoreWLAN prompt |

### Per-process traffic (Top Apps, Network Slice, app statistics)
This is the one genuinely hard port. macOS gets it free from `netstat -n -b -v`; **Windows has no unelevated equivalent**:

- `GetPerTcpConnectionEStats` returns per-connection byte counts, but `SetPerTcpConnectionEStats` **requires Administrators membership** — dead end for an unelevated app.
- Task Manager's and Resource Monitor's Network columns are built on ETW, which also needs admin.
- `NetworkInformation.GetAttributedNetworkUsageAsync` (WinRT) gives per-app totals but only at coarse historical granularity — usable for Statistics, useless for a live view.

**Decision:** the service hosts an ETW session on `Microsoft-Windows-Kernel-Network`, which emits per-PID send/recv events carrying size, local/remote address, and port. This is *richer* than the macOS netstat diffing — Network Slice gets true per-event remote endpoints instead of inferred deltas. Combine with `GetExtendedTcpTable(TCP_TABLE_OWNER_PID_ALL)` for connection listings, reverse DNS via `Dns.GetHostEntryAsync`.

> **Carry the energy lesson over.** `Microsoft-Windows-Kernel-Network` is chatty on a busy machine. Aggregate inside the service, and only stream to the UI while the popover, Network Slice, or Statistics window is actually open — the same discipline that fixed the `nettop` energy problem on macOS.

**Bonus:** Windows can show real app icons in Top Apps (`SHGetFileInfo` / `ExtractIconEx`), which the Mac version doesn't do.

### DNS switcher
`SetInterfaceDnsSettings` (iphlpapi, Windows 10 2004+) via the service. Presets, custom presets, ordering, and the green active-checkmark are all pure app logic and port directly.

### Router monitors — the easiest, highest-value port
Fritz!Box (TR-064 SOAP), UniFi (REST), OpenWRT (ubus JSON-RPC) and OPNsense (REST) are pure HTTP. Port to `HttpClient`; TOFU certificate pinning via `HttpClientHandler.ServerCertificateCustomValidationCallback`; credentials into **Windows Credential Manager** (`CredWrite`, `CRED_TYPE_GENERIC`) in place of Keychain. Fritz!Box alone justifies this for the German market.

### Speed Test — nearly free
`Packaging/Resources/SpeedTest/{mlab.html, cloudflare.html}` plus the m-lab/cloudflare JS run **unchanged** in WebView2. Port only the thin `SpeedTestManager` host bridge (`WKScriptMessageHandler` → `WebMessageReceived`) and the local resource server. Estimated: days, not weeks. History/notes storage ports with the rest of the persistence layer.

### Statistics
Same data model and retention logic. Persist to `%LOCALAPPDATA%\NetFluss\` — keep the existing JSON archive format so the two platforms stay diffable, or move to SQLite if the archive is already straining. Charts via LiveCharts2.

### VPN — port last, ship 1.0 without it
| Protocol | Windows approach | Admin? |
|---|---|---|
| **IKEv2 / L2TP** | RAS API (`RasSetEntryProperties` + `RasDial`), EAP/certificate config via EAP XML | No — per-user phonebook |
| **WireGuard** | [embeddable-dll-service](https://github.com/WireGuard/wireguard-windows/tree/master/embeddable-dll-service) + upstream-signed `wireguard.dll` (wireguard-nt), hosted in the NetFluss service | Yes |
| **OpenVPN** | Bundle `openvpn.exe` + **wintun** (upstream-signed), supervised by the service via the management interface — the same design as the macOS `OpenVPNManagementClient` | Yes, driver install at setup |

Rules: never sign or ship your own kernel driver — redistribute the upstream-signed wintun / wireguard-nt binaries. Do not name your own driver or service files `wireguard*`; upstream explicitly warns this clashes with official deployments. Invoking `openvpn.exe` as a separate process is aggregation, not linking — the same GPLv2/GPLv3 posture you already rely on for the macOS bundle.

### Everything else
| macOS | Windows |
|---|---|
| Launch at login | `HKCU\...\CurrentVersion\Run` (unpackaged) |
| Keychain | Credential Manager / DPAPI |
| `UserDefaults` | JSON settings file in `%LOCALAPPDATA%` (registry is a poor fit for ordered lists) |
| `UpdateChecker` (GitHub Releases API) | Ports as-is |
| Themes, `Color(hex:)`, `RateFormatter` | Mechanical translation |

---

## 5. Assets and localization

**Three concrete asset problems, all easy to miss:**

1. **SF Symbols cannot ship on Windows.** ~47 unique symbols are used across the SwiftUI views; Apple's licence restricts them to Apple platforms. Replace with **Fluent System Icons** (MIT) — a near-1:1 coverage map and the correct native look.
2. **Emoji country flags do not render on Windows.** Segoe UI Emoji has no regional-indicator glyphs, so the `dns.google 🇺🇸` row in Network Slice and the VPN exit-node flag would show two letter boxes. Bundle a raster/SVG flag set (e.g. `flag-icons`, MIT) keyed by the ISO code that `api.country.is` / `ipwho.is` already return.
3. **App icon** needs a multi-resolution `.ico` (16–256 px) plus light/dark tray variants derived from the existing 1024 px source.

**Localization:** keep the four `Localizable.strings` files as the **single source of truth for both platforms** and generate `.resx` (or a JSON dictionary) at build time with a small converter. 400 keys × 4 languages already exist and stay in sync — do not fork the translations. *(Note the existing trap: `Packaging/Resources/*.lproj` is what ships on macOS, `Sources/` holds a second copy. The generator should read the shipping copy.)*

---

## 6. Publishing — one signed installer, three channels

The Microsoft Store **accepts traditional MSI/EXE app listings**: you submit a *versioned HTTPS URL* to your own installer rather than an MSIX package, and the Store downloads and runs it. That single fact resolves the tension in the brief — you do **not** have to choose between Store presence and a plain GitHub download, and you keep the service and VPN drivers that MSIX would forbid.

**Registration cost: €0.** Microsoft dropped the registration fee for individual developers, and has since dropped it for **company** accounts too. Rana GmbH registers free. Revenue share is irrelevant for a free app.

### Code signing — do this first, it gates everything
Unsigned installers hit SmartScreen's "Windows protected your PC" wall, which is far more damaging than macOS Gatekeeper.

- **Recommended: Azure Trusted Signing** (now Azure Artifact Signing) — **$9.99/month** for up to 5,000 signatures, **EU organisations are eligible**, no hardware token, a first-party GitHub Actions task, and it builds SmartScreen reputation like a normal OV certificate. The three-years-trading rule from the preview has been dropped.
- **Fallback: an EV code-signing certificate** (Certum, SSL.com, DigiCert) — ~€400–600/yr, gives *instant* SmartScreen reputation, but since the 2023 CA/B key-storage rules it requires an HSM or hardware token, which is awkward in CI. Only worth it if the Trusted Signing identity validation stalls.

Sign **every** PE in the package — the app, the service, and any bundled VPN executables — as the Store's MSI/EXE requirements demand.

### Channel 1 — GitHub Releases *(primary)*
`NetFluss-X.Y.Z-x64.msi` and `-arm64.msi`, attached by a tag-triggered workflow shaped like the existing `release.yml`. The ported `UpdateChecker` already speaks the GitHub Releases API, so in-app update notification works day one.

### Channel 2 — winget *(the Homebrew-cask analogue)*
```
winget install RanaGmbH.NetFluss
```
Submit a manifest PR to `microsoft/winget-pkgs`; automate subsequent releases with `wingetcreate update` in the release workflow. Free, no review friction after the first submission, and it is how Windows power users expect to install. This is the direct counterpart to your `brew install --cask netfluss` — and unlike the tap, it needs no `brew trust` equivalent.

### Channel 3 — Microsoft Store *(MSI/EXE listing)*
Point the listing at the versioned installer URL. Two things to verify early:

- ⚠️ **Store validation may reject a GitHub release-asset URL**, because those redirect to `objects.githubusercontent.com`. Test this in the first submission. If it fails, host the canonical MSI at a stable path such as `https://downloads.ranagmbh.de/netfluss/NetFluss-X.Y.Z-x64.msi` and mirror the identical file to GitHub Releases. The installer must be a standalone offline installer — no downloader stubs.
- **GPLv3 licensing:** supply the GPLv3 as your own custom licence terms in Partner Center rather than accepting the Standard Application License Terms. The Microsoft Store permits this — it is notably friendlier to GPL software than the Mac App Store, which is why NetFluss can be in the Windows store but not Apple's.

### Explicitly not recommended: MSIX
No kernel drivers (kills OpenVPN/WireGuard), restricted service registration (kills the ETW collector, DNS switching, and adapter reset), and worse sideload UX than an MSI. Revisit only if a driver-free reduced edition ever makes sense.

---

## 7. Phasing and effort

| Phase | Work | Estimate |
|---|---|---|
| **0 — Spike** | Tray bitmap meter + `GetIfTable2` rates + popover shell. *Answers the taskbar question before committing.* | 1 week |
| **1 — Parity core** | Adapters, Wi-Fi info, IP/flow, popover sections + drag ordering, Preferences, themes, localization pipeline, launch-at-login, update checker | 3–4 weeks |
| **2 — Service** | Service + named-pipe IPC + installer integration; DNS switcher; adapter reset; ETW collector → Top Apps | 2–3 weeks |
| **3 — Windows** | Statistics window + store; Network Slice; Speed Test (days) | 3–4 weeks |
| **4 — Routers** | Fritz!Box, UniFi, OpenWRT, OPNsense + credential store + TOFU pinning | 1–2 weeks |
| **5 — VPN** | IKEv2 (RAS) → WireGuard → OpenVPN. Highest risk; **1.0 can ship without it** | 3–5 weeks |
| **6 — Ship** | Signing, WiX installer, CI, winget manifest, Store submission | 1–2 weeks |

**≈ 3 months to a 1.0 covering everything except VPN; ≈ 4–5 months to full 2.5 parity.** Phases 1 and 4 are independent and parallelisable.

---

## 8. Repository strategy

Keep Windows in **this repository** under `windows/`, with separate workflows keyed on tag prefixes — `v*` for macOS, `win-v*` for Windows.

Rationale: the localization files and Speed Test assets are shared and must not fork; one issue tracker and one star count; the README already documents the whole product. The cost is a slightly busier release list, which the tag prefix handles.

Alternative if that gets noisy: a separate `rana-gmbh/NetFluss-Windows` repo with the strings and Speed Test assets pulled in as a git submodule or synced by CI.

---

## 9. Risk register

| Risk | Severity | Mitigation |
|---|---|---|
| Per-process traffic needs an elevated service | **High** — no way around it | Ship it, and degrade gracefully when absent. Same model as the macOS helper |
| Taskbar overlay mode breaks on a Windows update | Medium | Tray icon is the default and never breaks; overlay is explicitly opt-in and best-effort |
| ETW event volume burns CPU/battery | Medium | Aggregate in-service; stream only while a consuming window is open |
| Store rejects the GitHub installer URL | Medium | Test in the first submission; fall back to hosting on ranagmbh.de |
| Trusted Signing identity validation delays | Medium | Start the application **before** Phase 6; EV certificate as fallback |
| VPN driver install friction / AV false positives | Medium | Use only upstream-signed wintun & wireguard-nt; make VPN an optional installer feature |
| Windows 11 hides the tray icon → "the app doesn't work" | Medium — will generate issues | First-run onboarding + `IsPromoted` registry hint + a README section |
| No Wi-Fi Noise/SNR on Windows | Low | Hide the two fields on Windows; document the difference |
| SF Symbols / emoji flags don't exist on Windows | Low, but easy to discover late | Fluent System Icons + a bundled flag set, decided up front |
| WebView2 runtime missing on old Windows 10 | Low | Bootstrap the evergreen runtime from the installer |

---

## 10. First three concrete actions

1. **Start the Azure Trusted Signing / Partner Center registration now.** Both are free-or-cheap, both involve identity validation with a lead time, and both gate shipping. Nothing else in this plan blocks on code.
2. **Run the Phase 0 spike.** A tray bitmap meter reading `GetIfTable2` is roughly 300 lines and settles the single biggest open design question — whether the tray icon is legible enough to be the default, or whether the taskbar overlay has to be first-class.
3. **Write the `.strings` → `.resx` converter.** Small, mechanical, and it locks in the decision to keep one set of translations for both platforms before either codebase drifts.
