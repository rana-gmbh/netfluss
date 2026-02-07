import Foundation

enum AdapterType: String {
    case wifi
    case ethernet
    case other
}

struct AdapterStatus: Identifiable {
    let id: String
    let name: String
    let displayName: String
    let type: AdapterType
    let isUp: Bool
    let linkSpeedBps: UInt64?
    let wifiMode: String?
    let wifiTxRateMbps: Double?
    let wifiSSID: String?
    let rxBytes: UInt64
    let txBytes: UInt64
    let rxRateBps: Double
    let txRateBps: Double
}

struct RateTotals {
    let rxRateBps: Double
    let txRateBps: Double
}

struct AppTraffic: Identifiable {
    let id: String
    let name: String
    let rxRateBps: Double
    let txRateBps: Double
}

struct InterfaceSample {
    let name: String
    let flags: UInt32
    let rxBytes: UInt64
    let txBytes: UInt64
    let baudrate: UInt64
}
