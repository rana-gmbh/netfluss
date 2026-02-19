# Netfluss

A minimal macOS menubar app showing real-time upload and download rates across all active network adapters.

## Features

### Menubar
- Live upload ↑ and download ↓ rates displayed in the menu bar
- Monospaced digits for stable layout
- Configurable colours for upload and download labels (Preferences → Appearance)

### Popover
- **Header** — total Download and Upload rates shown prominently at the top
- **Adapter cards** — each active network interface as a card with:
  - SF Symbol icon for Wi-Fi, Ethernet, or other adapters
  - Link speed badge (Wi-Fi TX rate or Ethernet speed)
  - Per-card DL/UL rates with coloured arrows
  - Wi-Fi frequency band (2.4 GHz / 5 GHz / 6 GHz) or "Ethernet"
- **IP addresses** — External and Internal IP shown between adapters and Top Apps, each with a one-click copy button
- **Top Apps** — optional section listing the top 10 processes by network traffic, with a relative usage bar per app (enable in Preferences)
- **Footer** — quick access to Preferences and Quit

### Preferences
- Refresh interval (0.5 – 5 seconds)
- Show/hide inactive adapters
- Show/hide other adapters (VPN, virtual interfaces)
- Per-adapter visibility toggles
- Display rates in bits or bytes
- Upload / Download label colours (8 swatches: Green, Blue, Orange, Yellow, Teal, Purple, Pink, White)
- Top Apps toggle (uses `nettop` — may be restricted on some systems)

## Requirements

- macOS 13 Ventura or later
- Xcode 15+ or Swift 5.9+ toolchain (to build from source)

## Install

Download `Netfluss-1.2.zip` from the [latest release](https://github.com/rana-gmbh/netfluss/releases/latest), unzip, and move `Netfluss.app` to `/Applications`.

**First launch — Gatekeeper**

Because Netfluss is not notarized with an Apple Developer certificate, macOS will block it on first run. Two ways to open it:

- **Right-click** `Netfluss.app` → **Open** → **Open** in the dialog
- Or run once in Terminal, then launch normally:
  ```bash
  xattr -dr com.apple.quarantine /Applications/Netfluss.app
  ```

## Build from source

```bash
swift build -c release
```

Or open `Package.swift` in Xcode, select the `Netfluss` scheme, and run.

## Notes

- Wi-Fi SSID and band use CoreWLAN. macOS may prompt for Location Services permission to expose SSID details.
- Ethernet link speed is read from `ifi_baudrate` and may show `—` when unavailable.
- External IP is fetched from `api.ipify.org` and cached for 60 seconds.
- Top Apps uses `nettop -P -L 1 -n -x`. If no data appears, macOS NStat restrictions may be in effect on your system.
