import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    weak var model: AppModel?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    /// Quitting while a copy runs asks first, cancels the jobs and quits once they have cleaned up.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        MainActor.assumeIsolated { model?.shouldTerminate() ?? .terminateNow }
    }

    /// "Pokračovat" after closing the window: a click on the Dock icon brings the window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { true }
}

@main
struct OffloadApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var model = AppModel()

    var body: some Scene {
        WindowGroup("Offload") {
            ContentView()
                .environmentObject(model)
                .onAppear { delegate.model = model }
        }
        .defaultSize(width: 1240, height: 860)
        .windowToolbarStyle(.unifiedCompact)
        .commands {
            CommandGroup(replacing: .newItem) {}
        }
    }
}
