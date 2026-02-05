# Netfluss

A minimal macOS menubar app showing per-adapter upload/download rates, Wi-Fi mode, and Ethernet link speed.

## Requirements
- macOS 13+
- Xcode 15+ or Swift 5.9+ toolchain

## Run (SwiftPM)
```bash
swift run
```

## Build in Xcode
1. Open `Package.swift` in Xcode.
2. Select the `Netfluss` scheme.
3. Run.

## Notes
- Wi-Fi SSID and mode use CoreWLAN. macOS may require Location Services permission to expose SSID details.
- Ethernet link speed is derived from interface `ifi_baudrate` and may show `N/A` when unavailable.
