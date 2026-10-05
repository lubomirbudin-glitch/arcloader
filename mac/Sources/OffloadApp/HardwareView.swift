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
                    section("Připojení") {
                        ForEach(hw.drives) { d in
                            HStack(alignment: .firstTextBaseline, spacing: 10) {
                                Text(d.role).fontWeight(.medium).frame(width: 110, alignment: .leading)
                                Text(d.root).font(.caption.monospaced()).foregroundStyle(.secondary)
                                    .lineLimit(1).truncationMode(.middle).frame(width: 160, alignment: .leading)
                                Text(d.description).textSelection(.enabled)
                            }
                        }
                    }
                    section("Doporučení") {
                        ForEach(hw.advice) { a in
                            Label(a.text, systemImage: icon(a.kind)).foregroundStyle(color(a.kind)).textSelection(.enabled)
                        }
                    }
                    section("Měření") {
                        HStack {
                            Button("Změřit rychlost…") { hw.askMeasure = true }.disabled(!hw.canMeasure)
                            Text(hw.measureHint).font(.caption).foregroundStyle(.secondary)
                        }
                        ForEach(hw.measurements) { m in
                            HStack(spacing: 10) {
                                Text(m.role).frame(width: 110, alignment: .leading)
                                Text(m.root).font(.caption.monospaced()).foregroundStyle(.secondary)
                                    .lineLimit(1).truncationMode(.middle).frame(width: 160, alignment: .leading).help(m.root)
                                Text("čtení \(m.read)").monospacedDigit()
                                Text("zápis \(m.write)").monospacedDigit()
                            }
                        }
                    }
                    section("USB porty") {
                        Toggle("Zobrazit interní porty", isOn: $hw.showInternal)
                        Text("macOS neví, kde je port na počítači fyzicky — pojmenujte si ho (např. „přední levý“), název se pak použije všude.")
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
                if hw.busy { Button("Zastavit") { hw.stop() }.disabled(!hw.canStop) }
                Button("Prověřit znovu") { hw.run(measure: false) }.disabled(hw.busy)
                Button("Zavřít") { NSApp.keyWindow?.performClose(nil) }
            }
            .padding(14)
        }
        .frame(minWidth: 680, minHeight: 480)
        .onAppear { hw.windowOpened() }
        .onDisappear { hw.windowClosed() }
        .alert("Změřit rychlost?", isPresented: $hw.askMeasure) {
            Button("Změřit") { hw.run(measure: true) }
            Button("Zrušit", role: .cancel) {}
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
        TextField("vlastní název", text: $text)
            .textFieldStyle(.roundedBorder)
            .help("Vlastní název portu, např. přední levý")
            .onAppear { text = hw.portName(connector) }
            .onChange(of: text) { hw.rename(connector, $0) }
    }
}
