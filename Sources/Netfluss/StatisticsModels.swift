// Copyright (C) 2026 Rana GmbH
//
// This file is part of Netfluss.
//
// Netfluss is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Netfluss is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Netfluss. If not, see <https://www.gnu.org/licenses/>.

import Foundation

struct StatisticsTrafficAmounts: Codable, Equatable, Sendable {
    var downloadBytes: UInt64 = 0
    var uploadBytes: UInt64 = 0

    var totalBytes: UInt64 { downloadBytes + uploadBytes }

    mutating func add(downloadBytes: UInt64, uploadBytes: UInt64) {
        self.downloadBytes += downloadBytes
        self.uploadBytes += uploadBytes
    }

    mutating func merge(_ other: StatisticsTrafficAmounts) {
        add(downloadBytes: other.downloadBytes, uploadBytes: other.uploadBytes)
    }
}

struct StatisticsArchive: Codable, Sendable {
    static let currentAppTrafficSchemaVersion = 2

    var createdAt: Date
    var lastAdapterSampleAt: Date?
    var lastAppSampleAt: Date?
    var appTrafficSchemaVersion: Int
    var adapterDisplayNames: [String: String]
    var adapterMinute: [String: [String: StatisticsTrafficAmounts]]
    var adapterHourly: [String: [String: StatisticsTrafficAmounts]]
    var adapterDaily: [String: [String: StatisticsTrafficAmounts]]
    var appMinute: [String: [String: StatisticsTrafficAmounts]]
    var appHourly: [String: [String: StatisticsTrafficAmounts]]
    var appDaily: [String: [String: StatisticsTrafficAmounts]]

    static let empty = StatisticsArchive(
        createdAt: Date(),
        lastAdapterSampleAt: nil,
        lastAppSampleAt: nil,
        appTrafficSchemaVersion: currentAppTrafficSchemaVersion,
        adapterDisplayNames: [:],
        adapterMinute: [:],
        adapterHourly: [:],
        adapterDaily: [:],
        appMinute: [:],
        appHourly: [:],
        appDaily: [:]
    )

    private enum CodingKeys: String, CodingKey {
        case createdAt
        case lastAdapterSampleAt
        case lastAppSampleAt
        case appTrafficSchemaVersion
        case adapterDisplayNames
        case adapterMinute
        case adapterHourly
        case adapterDaily
        case appMinute
        case appHourly
        case appDaily
    }

    init(
        createdAt: Date,
        lastAdapterSampleAt: Date?,
        lastAppSampleAt: Date?,
        appTrafficSchemaVersion: Int = Self.currentAppTrafficSchemaVersion,
        adapterDisplayNames: [String: String],
        adapterMinute: [String: [String: StatisticsTrafficAmounts]],
        adapterHourly: [String: [String: StatisticsTrafficAmounts]],
        adapterDaily: [String: [String: StatisticsTrafficAmounts]],
        appMinute: [String: [String: StatisticsTrafficAmounts]],
        appHourly: [String: [String: StatisticsTrafficAmounts]],
        appDaily: [String: [String: StatisticsTrafficAmounts]]
    ) {
        self.createdAt = createdAt
        self.lastAdapterSampleAt = lastAdapterSampleAt
        self.lastAppSampleAt = lastAppSampleAt
        self.appTrafficSchemaVersion = appTrafficSchemaVersion
        self.adapterDisplayNames = adapterDisplayNames
        self.adapterMinute = adapterMinute
        self.adapterHourly = adapterHourly
        self.adapterDaily = adapterDaily
        self.appMinute = appMinute
        self.appHourly = appHourly
        self.appDaily = appDaily
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        createdAt = try container.decode(Date.self, forKey: .createdAt)
        lastAdapterSampleAt = try container.decodeIfPresent(Date.self, forKey: .lastAdapterSampleAt)
        lastAppSampleAt = try container.decodeIfPresent(Date.self, forKey: .lastAppSampleAt)
        appTrafficSchemaVersion = try container.decodeIfPresent(Int.self, forKey: .appTrafficSchemaVersion) ?? 0
        adapterDisplayNames = try container.decodeIfPresent([String: String].self, forKey: .adapterDisplayNames) ?? [:]
        adapterMinute = try container.decodeIfPresent([String: [String: StatisticsTrafficAmounts]].self, forKey: .adapterMinute) ?? [:]
        adapterHourly = try container.decodeIfPresent([String: [String: StatisticsTrafficAmounts]].self, forKey: .adapterHourly) ?? [:]
        adapterDaily = try container.decodeIfPresent([String: [String: StatisticsTrafficAmounts]].self, forKey: .adapterDaily) ?? [:]
        appMinute = try container.decodeIfPresent([String: [String: StatisticsTrafficAmounts]].self, forKey: .appMinute) ?? [:]
        appHourly = try container.decodeIfPresent([String: [String: StatisticsTrafficAmounts]].self, forKey: .appHourly) ?? [:]
        appDaily = try container.decodeIfPresent([String: [String: StatisticsTrafficAmounts]].self, forKey: .appDaily) ?? [:]
    }
}

struct StatisticsAdapterDelta: Sendable {
    let id: String
    let displayName: String
    let downloadBytes: UInt64
    let uploadBytes: UInt64
}

struct StatisticsAppDelta: Sendable {
    let name: String
    let downloadBytes: UInt64
    let uploadBytes: UInt64
}

struct StatisticsRange: Equatable, Identifiable, Sendable {
    enum Granularity: Equatable, Sendable {
        case minute
        case hour
        case day
    }

    let id: String
    let title: String
    let start: Date
    let end: Date
    let granularity: Granularity

    var bucketTitle: String {
        switch granularity {
        case .minute: return "Minute Traffic"
        case .hour: return "Hourly Traffic"
        case .day: return "Daily Traffic"
        }
    }

    static let presetIDs = ["today", "yesterday", "thisWeek", "thisMonth", "thisYear"]

    static func today(now: Date = Date(), calendar: Calendar = .autoupdatingCurrent) -> StatisticsRange {
        let start = calendar.startOfDay(for: now)
        return StatisticsRange(id: "today", title: "Today", start: start, end: now, granularity: .hour)
    }

    static func yesterday(now: Date = Date(), calendar: Calendar = .autoupdatingCurrent) -> StatisticsRange {
        let todayStart = calendar.startOfDay(for: now)
        let yesterdayStart = calendar.date(byAdding: .day, value: -1, to: todayStart) ?? todayStart
        return StatisticsRange(id: "yesterday", title: "Yesterday", start: yesterdayStart, end: todayStart, granularity: .hour)
    }

    static func thisWeek(now: Date = Date(), calendar: Calendar = .autoupdatingCurrent) -> StatisticsRange {
        let weekInterval = calendar.dateInterval(of: .weekOfYear, for: now)
        let start = weekInterval?.start ?? calendar.startOfDay(for: now)
        return StatisticsRange(id: "thisWeek", title: "This Week", start: start, end: now, granularity: .day)
    }

    static func thisMonth(now: Date = Date(), calendar: Calendar = .autoupdatingCurrent) -> StatisticsRange {
        let monthInterval = calendar.dateInterval(of: .month, for: now)
        let start = monthInterval?.start ?? calendar.startOfDay(for: now)
        return StatisticsRange(id: "thisMonth", title: "This Month", start: start, end: now, granularity: .day)
    }

    static func thisYear(now: Date = Date(), calendar: Calendar = .autoupdatingCurrent) -> StatisticsRange {
        let yearInterval = calendar.dateInterval(of: .year, for: now)
        let start = yearInterval?.start ?? calendar.startOfDay(for: now)
        return StatisticsRange(id: "thisYear", title: "This Year", start: start, end: now, granularity: .day)
    }

    private static let shortDateFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "MMM d"
        return f
    }()

    static func custom(start: Date, end: Date, calendar: Calendar = .autoupdatingCurrent) -> StatisticsRange {
        let span = end.timeIntervalSince(start)
        let granularity: Granularity = span <= 2 * 86400 ? .hour : .day
        let title = "\(shortDateFormatter.string(from: start)) – \(shortDateFormatter.string(from: end))"
        return StatisticsRange(id: "custom", title: title, start: start, end: end, granularity: granularity)
    }

    static func preset(for id: String, now: Date = Date(), calendar: Calendar = .autoupdatingCurrent) -> StatisticsRange? {
        switch id {
        case "today": return today(now: now, calendar: calendar)
        case "yesterday": return yesterday(now: now, calendar: calendar)
        case "thisWeek": return thisWeek(now: now, calendar: calendar)
        case "thisMonth": return thisMonth(now: now, calendar: calendar)
        case "thisYear": return thisYear(now: now, calendar: calendar)
        default: return nil
        }
    }

    static func presetTitle(for id: String) -> String {
        switch id {
        case "today": return "Today"
        case "yesterday": return "Yesterday"
        case "thisWeek": return "This Week"
        case "thisMonth": return "This Month"
        case "thisYear": return "This Year"
        default: return id
        }
    }
}

struct StatisticsTimelinePoint: Identifiable, Sendable {
    let id: String
    let date: Date
    let downloadBytes: UInt64
    let uploadBytes: UInt64
}

struct StatisticsAdapterRow: Identifiable, Sendable {
    let id: String
    let name: String
    let downloadBytes: UInt64
    let uploadBytes: UInt64

    var totalBytes: UInt64 { downloadBytes + uploadBytes }
}

struct StatisticsAppRow: Identifiable, Sendable {
    let id: String
    let name: String
    let bytes: UInt64
}

struct StatisticsReport: Sendable {
    let range: StatisticsRange
    let createdAt: Date
    let coverageStart: Date?
    let lastAdapterSampleAt: Date?
    let lastAppSampleAt: Date?
    let totalDownloadBytes: UInt64
    let totalUploadBytes: UInt64
    let timeline: [StatisticsTimelinePoint]
    let adapters: [StatisticsAdapterRow]
    let topDownloadApps: [StatisticsAppRow]
    let topUploadApps: [StatisticsAppRow]

    var hasAdapterData: Bool {
        totalDownloadBytes > 0 || totalUploadBytes > 0 || !adapters.isEmpty || !timeline.isEmpty
    }

    var hasAppData: Bool {
        !topDownloadApps.isEmpty || !topUploadApps.isEmpty
    }
}
