import SwiftUI

@main
struct NetflussApp: App {
    @StateObject private var appState = AppState()

    var body: some Scene {
        Settings {
            PreferencesView()
                .environmentObject(appState.monitor)
        }
    }
}
