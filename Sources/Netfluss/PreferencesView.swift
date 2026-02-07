import SwiftUI

struct PreferencesView: View {
    @AppStorage("refreshInterval") private var refreshInterval: Double = 1.0
    @AppStorage("showInactive") private var showInactive: Bool = false
    @AppStorage("showOtherAdapters") private var showOtherAdapters: Bool = false
    @AppStorage("useBits") private var useBits: Bool = false
    @AppStorage("showTopApps") private var showTopApps: Bool = false
    @State private var hiddenAdapters: Set<String> = []

    @EnvironmentObject private var monitor: NetworkMonitor

    var body: some View {
        let adapters = visibleAdapters()

        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                GroupBox("Update") {
                    VStack(alignment: .leading, spacing: 10) {
                        HStack {
                            Slider(value: $refreshInterval, in: 0.5...5.0, step: 0.5)
                            Text("\(refreshInterval, specifier: "%.1f") s")
                                .frame(width: 60, alignment: .trailing)
                        }
                        Toggle("Show inactive adapters", isOn: $showInactive)
                        Toggle("Show other adapters (VPN, virtual)", isOn: $showOtherAdapters)
                    }
                    .padding(.top, 4)
                }

                GroupBox("Adapters") {
                    VStack(alignment: .leading, spacing: 8) {
                        if adapters.isEmpty {
                            Text("No adapters match current filters.")
                                .font(.system(size: 11))
                                .foregroundStyle(.secondary)
                        } else {
                            ForEach(adapters, id: \.id) { adapter in
                                Toggle(adapter.displayName, isOn: bindingFor(adapter.id))
                            }
                        }
                    }
                    .padding(.top, 4)
                }

                GroupBox("Units") {
                    VStack(alignment: .leading, spacing: 8) {
                        Toggle("Display rates in bits", isOn: $useBits)
                    }
                    .padding(.top, 4)
                }

                GroupBox("Top Apps") {
                    VStack(alignment: .leading, spacing: 8) {
                        Toggle("Show top apps/services (uses nettop)", isOn: $showTopApps)
                        Text("If this shows no data, macOS may restrict nettop access on your system.")
                            .font(.system(size: 11))
                            .foregroundStyle(.secondary)
                    }
                    .padding(.top, 4)
                }
            }
            .padding(20)
        }
        .frame(width: 420, height: 460)
        .onAppear {
            hiddenAdapters = Set(UserDefaults.standard.stringArray(forKey: "hiddenAdapters") ?? [])
        }
    }

    private func allAdapters() -> [AdapterStatus] {
        monitor.adapters.sorted { $0.displayName.localizedCaseInsensitiveCompare($1.displayName) == .orderedAscending }
    }

    private func visibleAdapters() -> [AdapterStatus] {
        allAdapters().filter { adapter in
            if !showOtherAdapters, adapter.type == .other { return false }
            if !showInactive, adapter.rxRateBps == 0, adapter.txRateBps == 0, adapter.isUp == false { return false }
            return true
        }
    }

    private func bindingFor(_ id: String) -> Binding<Bool> {
        Binding(
            get: { !hiddenAdapters.contains(id) },
            set: { isOn in
                if isOn {
                    hiddenAdapters.remove(id)
                } else {
                    hiddenAdapters.insert(id)
                }
                UserDefaults.standard.set(Array(hiddenAdapters), forKey: "hiddenAdapters")
            }
        )
    }
}
