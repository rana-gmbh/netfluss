import SwiftUI
import AppKit

struct MenuBarLabelView: View {
    @EnvironmentObject private var monitor: NetworkMonitor
    @AppStorage("refreshInterval") private var refreshInterval: Double = 1.0
    @AppStorage("useBits") private var useBits: Bool = false

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            Text("↑ \(RateFormatter.formatRate(monitor.totals.txRateBps, useBits: useBits))")
                .foregroundStyle(.green)
            Text("↓ \(RateFormatter.formatRate(monitor.totals.rxRateBps, useBits: useBits))")
                .foregroundStyle(.primary)
        }
        .font(.system(size: 10, weight: .medium))
        .monospacedDigit()
        .task {
            monitor.start(interval: refreshInterval)
        }
        .onChange(of: refreshInterval) { newValue in
            monitor.start(interval: newValue)
        }
    }
}

struct MenuBarView: View {
    @EnvironmentObject private var monitor: NetworkMonitor
    @AppStorage("showInactive") private var showInactive: Bool = false
    @AppStorage("showOtherAdapters") private var showOtherAdapters: Bool = false
    @AppStorage("useBits") private var useBits: Bool = false

    var body: some View {
        let adapters = filteredAdapters()

        if adapters.isEmpty {
            Text("No active adapters")
                .padding(.vertical, 8)
        } else {
            ForEach(adapters) { adapter in
                AdapterRow(adapter: adapter, useBits: useBits)
            }
        }

        Divider()

        Button("Preferences...") {
            PreferencesWindowController.shared.show(monitor: monitor)
        }

        Button("Quit Netfluss") {
            NSApplication.shared.terminate(nil)
        }
    }

    private func filteredAdapters() -> [AdapterStatus] {
        let hidden = Set(UserDefaults.standard.stringArray(forKey: "hiddenAdapters") ?? [])
        return monitor.adapters.filter { adapter in
            if !showOtherAdapters, adapter.type == .other { return false }
            if !showInactive, adapter.rxRateBps == 0, adapter.txRateBps == 0, adapter.isUp == false { return false }
            if hidden.contains(adapter.id) { return false }
            return true
        }
    }
}

struct AdapterRow: View {
    let adapter: AdapterStatus
    let useBits: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(titleText())
                    .font(.system(size: 12, weight: .semibold))
                Spacer()
                Text(linkText())
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            HStack {
                Text("DL \(RateFormatter.formatRate(adapter.rxRateBps, useBits: useBits))")
                Text("UL \(RateFormatter.formatRate(adapter.txRateBps, useBits: useBits))")
                Spacer()
                Text(modeText())
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 6)
        .padding(.horizontal, 4)
    }

    private func titleText() -> String {
        if adapter.type == .wifi, let ssid = adapter.wifiSSID, !ssid.isEmpty {
            return "Wi-Fi (\(ssid))"
        }
        return adapter.displayName
    }

    private func linkText() -> String {
        if adapter.type == .wifi {
            let rate = RateFormatter.formatMbps(adapter.wifiTxRateMbps)
            return "Link \(rate)"
        }
        if adapter.type == .ethernet {
            return "Link \(RateFormatter.formatLinkSpeed(adapter.linkSpeedBps, useBits: true))"
        }
        return ""
    }

    private func modeText() -> String {
        if adapter.type == .wifi {
            return adapter.wifiMode ?? "Wi-Fi"
        }
        if adapter.type == .ethernet {
            return "Ethernet"
        }
        return ""
    }
}
