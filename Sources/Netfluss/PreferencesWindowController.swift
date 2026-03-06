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

import AppKit
import SwiftUI

@MainActor
final class PreferencesWindowController {
    static let shared = PreferencesWindowController()
    private var window: NSWindow?

    func show(monitor: NetworkMonitor) {
        if let window {
            window.makeKeyAndOrderFront(nil)
            window.orderFrontRegardless()
            NSApp.activate(ignoringOtherApps: true)
            return
        }

        let view = PreferencesView()
            .environmentObject(monitor)
        let hosting = NSHostingController(rootView: view)
        hosting.sizingOptions = []

        let window = NSWindow(contentViewController: hosting)
        window.title = "Preferences"
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
        window.setContentSize(NSSize(width: 420, height: 820))
        window.minSize = NSSize(width: 420, height: 400)
        window.maxSize = NSSize(width: 600, height: 10000)
        window.isReleasedWhenClosed = false
        window.center()
        window.makeKeyAndOrderFront(nil)
        window.orderFrontRegardless()
        // LSUIElement apps need explicit activation for text fields in sheets to work.
        NSApp.activate(ignoringOtherApps: true)

        self.window = window
    }
}

@MainActor
final class AddDNSWindowController {
    static let shared = AddDNSWindowController()
    private var panel: NSPanel?
    private var onSave: ((DNSPreset) -> Void)?

    func show(onSave: @escaping (DNSPreset) -> Void) {
        if let panel {
            panel.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        self.onSave = onSave

        let view = AddDNSPanelView { [weak self] preset in
            onSave(preset)
            self?.close()
        } onCancel: { [weak self] in
            self?.close()
        }
        let hosting = NSHostingController(rootView: view)

        let panel = NSPanel(contentViewController: hosting)
        panel.title = "Add Custom DNS"
        panel.styleMask = [.titled, .closable, .nonactivatingPanel]
        panel.isFloatingPanel = true
        panel.becomesKeyOnlyIfNeeded = false
        panel.setContentSize(NSSize(width: 300, height: 200))
        panel.isReleasedWhenClosed = false
        panel.center()
        panel.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        // Force panel to become key window so text fields receive input
        panel.makeKey()

        self.panel = panel
    }

    private func close() {
        panel?.close()
        panel = nil
        onSave = nil
    }
}

struct AddDNSPanelView: View {
    let onSave: (DNSPreset) -> Void
    let onCancel: () -> Void

    @State private var name: String = ""
    @State private var primary: String = ""
    @State private var secondary: String = ""

    private var isValid: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty &&
        !primary.trimmingCharacters(in: .whitespaces).isEmpty
    }

    var body: some View {
        VStack(spacing: 16) {
            Text("Add Custom DNS")
                .font(.headline)
            VStack(alignment: .leading, spacing: 8) {
                TextField("Name (e.g. My DNS)", text: $name)
                    .textFieldStyle(.roundedBorder)
                TextField("Primary DNS (e.g. 1.1.1.1)", text: $primary)
                    .textFieldStyle(.roundedBorder)
                TextField("Secondary DNS (optional)", text: $secondary)
                    .textFieldStyle(.roundedBorder)
            }
            HStack(spacing: 12) {
                Button("Cancel", action: onCancel)
                    .keyboardShortcut(.cancelAction)
                Button("Add") { save() }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
                    .disabled(!isValid)
            }
        }
        .padding(24)
    }

    private func save() {
        let trimName = name.trimmingCharacters(in: .whitespaces)
        let trimPrimary = primary.trimmingCharacters(in: .whitespaces)
        let trimSecondary = secondary.trimmingCharacters(in: .whitespaces)
        var servers = [trimPrimary]
        if !trimSecondary.isEmpty { servers.append(trimSecondary) }
        onSave(DNSPreset(
            id: UUID().uuidString,
            name: trimName,
            servers: servers,
            isBuiltIn: false
        ))
    }
}
