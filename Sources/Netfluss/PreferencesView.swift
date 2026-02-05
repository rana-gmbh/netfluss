import SwiftUI

struct PreferencesView: View {
    @AppStorage("refreshInterval") private var refreshInterval: Double = 1.0
    @AppStorage("showInactive") private var showInactive: Bool = false
    @AppStorage("showOtherAdapters") private var showOtherAdapters: Bool = false
    @AppStorage("useBits") private var useBits: Bool = false
    @State private var hiddenAdapters: Set<String> = []

    @EnvironmentObject private var monitor: NetworkMonitor

    var body: some View {
        Form {
            Section("Update") {
                HStack {
                    Slider(value: $refreshInterval, in: 0.5...5.0, step: 0.5)
                    Text("\(refreshInterval, specifier: "%.1f") s")
                        .frame(width: 60, alignment: .trailing)
                }
                Toggle("Show inactive adapters", isOn: $showInactive)
                Toggle("Show other adapters (VPN, virtual)", isOn: $showOtherAdapters)
            }

            Section("Adapters") {
                ForEach(allAdapters()) { adapter in
                    Toggle(adapter.displayName, isOn: bindingFor(adapter.id))
                }
            }

            Section("Units") {
                Toggle("Display rates in bits", isOn: $useBits)
            }
        }
        .padding(20)
        .frame(width: 380)
        .onAppear {
            hiddenAdapters = Set(UserDefaults.standard.stringArray(forKey: "hiddenAdapters") ?? [])
        }
    }

    private func allAdapters() -> [AdapterStatus] {
        monitor.adapters.sorted { $0.displayName.localizedCaseInsensitiveCompare($1.displayName) == .orderedAscending }
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
