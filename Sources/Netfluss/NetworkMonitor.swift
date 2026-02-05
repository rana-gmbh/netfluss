import Foundation
import CoreWLAN
import SystemConfiguration

@MainActor
final class NetworkMonitor: ObservableObject {
    @Published var adapters: [AdapterStatus] = []
    @Published var totals = RateTotals(rxRateBps: 0, txRateBps: 0)

    private var timer: DispatchSourceTimer?
    private var lastSample: [String: InterfaceSample] = [:]
    private var lastUpdate: Date?
    private var currentInterval: Double?

    func start(interval: Double) {
        let clamped = max(0.2, min(interval, 10.0))
        if currentInterval == clamped, timer != nil { return }
        currentInterval = clamped

        timer?.cancel()
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.main)
        timer.schedule(deadline: .now(), repeating: clamped)
        timer.setEventHandler { [weak self] in
            self?.refresh()
        }
        timer.resume()
        self.timer = timer
    }

    private func refresh() {
        let now = Date()
        let samples = InterfaceSampler.fetchSamples()
        let typeMap = InterfaceSampler.interfaceTypes()
        let displayMap = InterfaceSampler.interfaceDisplayNames()
        let wifiInfoMap = InterfaceSampler.wifiInfo()

        var updatedAdapters: [AdapterStatus] = []
        var totalRxRate: Double = 0
        var totalTxRate: Double = 0

        for sample in samples {
            let previous = lastSample[sample.name]
            let deltaTime = now.timeIntervalSince(lastUpdate ?? now)

            let rxRate = InterfaceSampler.rate(current: sample.rxBytes, previous: previous?.rxBytes, deltaTime: deltaTime)
            let txRate = InterfaceSampler.rate(current: sample.txBytes, previous: previous?.txBytes, deltaTime: deltaTime)

            let type = typeMap[sample.name] ?? .other
            let displayName = displayMap[sample.name] ?? sample.name

            let wifiInfo = wifiInfoMap[sample.name]

            let isUp = (sample.flags & UInt32(IFF_UP)) != 0
            let linkSpeed = type == .ethernet ? sample.baudrate : nil

            let adapter = AdapterStatus(
                id: sample.name,
                name: sample.name,
                displayName: displayName,
                type: type,
                isUp: isUp,
                linkSpeedBps: linkSpeed,
                wifiMode: wifiInfo?.mode,
                wifiTxRateMbps: wifiInfo?.txRate,
                wifiSSID: wifiInfo?.ssid,
                rxBytes: sample.rxBytes,
                txBytes: sample.txBytes,
                rxRateBps: rxRate,
                txRateBps: txRate
            )
            updatedAdapters.append(adapter)

            totalRxRate += rxRate
            totalTxRate += txRate
        }

        adapters = updatedAdapters.sorted { $0.displayName.localizedCaseInsensitiveCompare($1.displayName) == .orderedAscending }
        totals = RateTotals(rxRateBps: totalRxRate, txRateBps: totalTxRate)
        lastSample = Dictionary(uniqueKeysWithValues: samples.map { ($0.name, $0) })
        lastUpdate = now
    }
}

enum InterfaceSampler {
    static func fetchSamples() -> [InterfaceSample] {
        var samples: [InterfaceSample] = []
        var pointer: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&pointer) == 0, let first = pointer else { return [] }

        defer { freeifaddrs(pointer) }

        var current: UnsafeMutablePointer<ifaddrs>? = first
        while let addr = current?.pointee {
            defer { current = addr.ifa_next }

            guard let sa = addr.ifa_addr, sa.pointee.sa_family == UInt8(AF_LINK) else { continue }
            guard let data = addr.ifa_data else { continue }

            let ifdata = data.assumingMemoryBound(to: if_data.self).pointee
            let name = String(cString: addr.ifa_name)

            let sample = InterfaceSample(
                name: name,
                flags: addr.ifa_flags,
                rxBytes: UInt64(ifdata.ifi_ibytes),
                txBytes: UInt64(ifdata.ifi_obytes),
                baudrate: UInt64(ifdata.ifi_baudrate)
            )
            samples.append(sample)
        }

        return samples
    }

    static func interfaceTypes() -> [String: AdapterType] {
        guard let list = SCNetworkInterfaceCopyAll() as? [SCNetworkInterface] else { return [:] }
        var map: [String: AdapterType] = [:]
        for iface in list {
            guard let bsdName = SCNetworkInterfaceGetBSDName(iface) as String? else { continue }
            if let type = SCNetworkInterfaceGetInterfaceType(iface) {
                if CFEqual(type, kSCNetworkInterfaceTypeIEEE80211) {
                    map[bsdName] = .wifi
                } else if CFEqual(type, kSCNetworkInterfaceTypeEthernet) {
                    map[bsdName] = .ethernet
                } else {
                    map[bsdName] = .other
                }
            } else {
                map[bsdName] = .other
            }
        }
        return map
    }

    static func interfaceDisplayNames() -> [String: String] {
        guard let list = SCNetworkInterfaceCopyAll() as? [SCNetworkInterface] else { return [:] }
        var map: [String: String] = [:]
        for iface in list {
            guard let bsdName = SCNetworkInterfaceGetBSDName(iface) as String? else { continue }
            if let name = SCNetworkInterfaceGetLocalizedDisplayName(iface) as String? {
                map[bsdName] = name
            }
        }
        return map
    }

    struct WifiInfo {
        let mode: String
        let txRate: Double
        let ssid: String?
    }

    static func wifiInfo() -> [String: WifiInfo] {
        let client = CWWiFiClient.shared()
        guard let interfaces = client.interfaces() else { return [:] }
        var map: [String: WifiInfo] = [:]
        for iface in interfaces {
            guard let name = iface.interfaceName else { continue }
            let mode = wifiModeString(for: iface)
            let rate = iface.transmitRate()
            let ssid = iface.ssid()
            map[name] = WifiInfo(mode: mode, txRate: rate, ssid: ssid)
        }
        return map
    }

    static func wifiModeString(for iface: CWInterface) -> String {
        let band = iface.wlanChannel()?.channelBand
        switch band {
        case .band6GHz?:
            return "Wi-Fi (6 GHz)"
        case .band5GHz?:
            return "Wi-Fi (5 GHz)"
        case .band2GHz?:
            return "Wi-Fi (2.4 GHz)"
        case .bandUnknown?:
            return "Wi-Fi"
        case nil:
            return "Wi-Fi"
        @unknown default:
            return "Wi-Fi"
        }
    }

    static func rate(current: UInt64, previous: UInt64?, deltaTime: Double) -> Double {
        guard let previous, deltaTime > 0 else { return 0 }
        let delta = current >= previous ? current - previous : 0
        return Double(delta) / deltaTime
    }
}
