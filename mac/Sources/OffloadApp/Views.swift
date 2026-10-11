import AppKit
import OffloadKit
import SwiftUI

// MARK: - Root

struct ContentView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        RootView().themed()
    }
}

/// The top bar over the two columns (steps on the left, the queue on the right) — or the settings page.
private struct RootView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        VStack(spacing: 0) {
            if model.settingsSection != nil {
                SettingsPage()
            } else {
                TopBar()
                HSplitView {
                    SetupColumn()
                        .frame(minWidth: Metrics.leftMin, idealWidth: Metrics.leftIdeal, maxWidth: Metrics.leftMax)
                    QueueColumn()
                        .frame(minWidth: 520)
                }
            }
        }
        .background(p.bg)
        .ignoresSafeArea()
        .frame(minWidth: 940, minHeight: 640)
        .alert(Product.name, isPresented: Binding(get: { model.message != nil }, set: { if !$0 { model.message = nil } })) {
            Button("OK") { model.message = nil }
        } message: {
            Text(model.message ?? "")
        }
    }
}

