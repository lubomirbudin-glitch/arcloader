import AppKit
import OffloadKit
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

    /// "Keep running" after closing the window: a click on the Dock icon brings the window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { true }

    /// A running hardware measurement stops and deletes its temporary file before the process exits (≤ 10 s); a card
    /// history record still waiting for the history lock gets 10 s more — never lost silently (logged).
    func applicationWillTerminate(_ notification: Notification) {
        MainActor.assumeIsolated { model?.waitForHardware() }
        if !HistoryRecorder.drain(timeout: 10) {
            NSLog("%@", "history: \(HistoryRecorder.waiting) record(s) not written before quitting — " + Msg.historyNotWritten)
        }
    }
}

/// Starts the app — or, in a debug build with `--ui-snapshots <folder>`, renders the main views offscreen to PNG files
/// (dark and light, 1360 and 1000 px) and exits without opening a window.
@main
enum Launcher {
    static func main() {
#if DEBUG
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--ui-snapshots"), i + 1 < args.count {
            MainActor.assumeIsolated { Snapshots.run(to: args[i + 1]) }
            return
        }
#endif
        OffloadApp.main()
    }
}

struct OffloadApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var model = AppModel()

    var body: some Scene {
        WindowGroup("Offload") {
            ContentView()
                .environmentObject(model)
                .onAppear { delegate.model = model }
        }
        .defaultSize(width: 1360, height: 900)
        // The new look draws its own top bar (logo, queue pill, Hardware, Settings) where the title bar was.
        .windowStyle(.hiddenTitleBar)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(replacing: .appSettings) { SettingsMenuItem(model: model) }
            CommandGroup(before: .windowList) { HardwareMenuItem() }
            CommandMenu("Tools") { VerifyBackupMenuItem(model: model) }
        }
        // One Hardware window: opening it again only brings it to the front (no second run).
        Window("Hardware — USB ports and disks", id: "hardware") {
            HardwareView(hw: model.hardware)
                .environmentObject(model)
        }
        .defaultSize(width: 860, height: 700)
    }
}

/// Window › Hardware… (⌘U — like Ctrl+U on Windows; free in an app without a Format menu); the top bar's Hardware
/// button opens the same window. A View, so it can use `openWindow` (macOS 13+).
struct HardwareMenuItem: View {
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Button("Hardware — USB Ports and Disks…") { openWindow(id: "hardware") }
            .keyboardShortcut("u", modifiers: .command)
    }
}

/// Tools › Verify Backup… (⇧⌘O, like Ctrl+Shift+O on Windows): a card folder on a destination, or a whole day.
struct VerifyBackupMenuItem: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Button("Verify Backup…") { model.verifyBackup() }
            .keyboardShortcut("o", modifiers: [.command, .shift])
    }
}

/// Offload › Settings… (⌘,): the settings page in the main window.
struct SettingsMenuItem: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Button("Settings…") { model.openSettings() }
            .keyboardShortcut(",", modifiers: .command)
    }
}
