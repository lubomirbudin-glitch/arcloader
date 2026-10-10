import AppKit
import OffloadKit
import SwiftUI

struct HardwareView: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject var hw: HardwareModel

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    section("Connections") {
                        ForEach(hw.drives) { d in
                            HStack(alignment: .firstTextBaseline, spacing: 10) {
                                Text(d.role).fontWeight(.medium).frame(width: 110, alignment: .leading)
                                Text(d.root).font(.caption.monospaced()).foregroundStyle(.secondary)
                                    .lineLimit(1).truncationMode(.middle).frame(width: 160, alignment: .leading)
                                Text(d.description).textSelection(.enabled)
                            }
                        }
                    }
                    section("Advice") {
                        ForEach(hw.advice) { a in
                            Label(a.text, systemImage: icon(a.kind)).foregroundStyle(color(a.kind)).textSelection(.enabled)
                        }
                    }
                    section("Speed") {
                        HStack {
                            Button("Measure speed…") { hw.askMeasure = true }.disabled(!hw.canMeasure)
                            Text(hw.measureHint).font(.caption).foregroundStyle(.secondary)
                        }
                        ForEach(hw.measurements) { m in
                            HStack(spacing: 10) {
                                Text(m.role).frame(width: 110, alignment: .leading)
                                Text(m.root).font(.caption.monospaced()).foregroundStyle(.secondary)
                                    .lineLimit(1).truncationMode(.middle).frame(width: 160, alignment: .leading).help(m.root)
                                Text("read \(m.read)").monospacedDigit()
                                Text("write \(m.write)").monospacedDigit()
                            }
                        }
                    }
                    section("USB ports") {
                        Toggle("Show internal ports", isOn: $hw.showInternal)
                        Text("macOS doesn't know where a port physically is — name it (e.g. “front left”) and the name is used everywhere.")
                            .font(.caption).foregroundStyle(.secondary)
                        ForEach(hw.portGroups) { g in
                            Text(g.controller).font(.subheadline.weight(.semibold)).padding(.top, 6)
                            ForEach(g.ports) { p in
                                HStack(spacing: 10) {
                                    Text(p.label).frame(width: 210, alignment: .leading)
                                    Text(p.device).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
                                    Text(p.drives).foregroundStyle(.secondary)
                                    PortNameField(hw: hw, connector: p.connector).frame(width: 180)
                                }
                                .font(.callout)
                            }
                        }
                    }
                }
                .padding(20)
            }
            Divider()
            HStack {
                Text(hw.status).foregroundStyle(.secondary).lineLimit(2)
                Spacer()
                if hw.busy { Button("Stop") { hw.stop() }.disabled(!hw.canStop) }
                Button("Check again") { hw.run(measure: false) }.disabled(hw.busy)
                Button("Close") { NSApp.keyWindow?.performClose(nil) }
            }
            .padding(14)
        }
        .frame(minWidth: 680, minHeight: 480)
        .onAppear { hw.windowOpened() }
        .onDisappear { hw.windowClosed() }
        .alert("Measure speed?", isPresented: $hw.askMeasure) {
            Button("Measure") { hw.run(measure: true) }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(hw.measureQuestion)
        }
    }

    @ViewBuilder
    private func section<Content: View>(_ title: String, @ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.headline)
            content()
        }
    }

    private func icon(_ kind: AdviceKind) -> String {
        switch kind {
        case .warning: return "exclamationmark.triangle.fill"
        case .info: return "info.circle"
        case .tip: return "lightbulb"
        }
    }

    private func color(_ kind: AdviceKind) -> Color {
        switch kind {
        case .warning: return .orange
        case .info: return .primary
        case .tip: return .secondary
        }
    }
}

/// The user's name of one socket. Keeps what is typed (spaces included) and saves the trimmed name on every change.
struct PortNameField: View {
    @ObservedObject var hw: HardwareModel
    let connector: UsbConnector
    @State private var text = ""

    var body: some View {
        TextField("custom name", text: $text)
            .textFieldStyle(.roundedBorder)
            .help("Your name for this port, e.g. front left")
            .onAppear { text = hw.portName(connector) }
            .onChange(of: text) { hw.rename(connector, $0) }
    }
}
