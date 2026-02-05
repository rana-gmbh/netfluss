import Foundation

struct RateFormatter {
    static func formatRate(_ bytesPerSecond: Double, useBits: Bool) -> String {
        let value = max(0, bytesPerSecond)
        if useBits {
            return format(value * 8.0, units: ["b/s", "Kb/s", "Mb/s", "Gb/s", "Tb/s"])
        }
        return format(value, units: ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"])
    }

    static func formatLinkSpeed(_ bps: UInt64?, useBits: Bool) -> String {
        guard let bps else { return "—" }
        if useBits {
            return format(Double(bps), units: ["b/s", "Kb/s", "Mb/s", "Gb/s", "Tb/s"])
        }
        return format(Double(bps) / 8.0, units: ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"])
    }

    static func formatMbps(_ value: Double?) -> String {
        guard let value else { return "—" }
        if value >= 1000 {
            return String(format: "%.1f Gb/s", value / 1000.0)
        }
        return String(format: "%.0f Mb/s", value)
    }

    private static func format(_ value: Double, units: [String]) -> String {
        var adjusted = value
        var unitIndex = 0
        while adjusted >= 1000.0 && unitIndex < units.count - 1 {
            adjusted /= 1000.0
            unitIndex += 1
        }
        let formatString: String
        switch adjusted {
        case 0..<10:
            formatString = "%.2f"
        case 10..<100:
            formatString = "%.1f"
        default:
            formatString = "%.0f"
        }
        return String(format: formatString + " %@", adjusted, units[unitIndex])
    }
}
