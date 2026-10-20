import EstrichCore
import SwiftUI

/// Große Zifferntasten für die Geräte-PIN, auch mit Arbeitshandschuhen gut zu treffen (05, Abschnitt 4).
struct PINPad: View {
    @Binding var pin: String
    var isDisabled = false
    let onComplete: (String) -> Void

    private let rows: [[String]] = [["1", "2", "3"], ["4", "5", "6"], ["7", "8", "9"], ["", "0", "⌫"]]

    var body: some View {
        VStack(spacing: 28) {
            HStack(spacing: 18) {
                ForEach(0..<PINPolicy.length, id: \.self) { index in
                    Circle()
                        .fill(index < pin.count ? Color.primary : .clear)
                        .overlay { Circle().strokeBorder(Color.primary, lineWidth: 1.5) }
                        .frame(width: 16, height: 16)
                }
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("\(pin.count) von \(PINPolicy.length) Ziffern eingegeben")

            Grid(horizontalSpacing: 22, verticalSpacing: 16) {
                ForEach(rows, id: \.self) { row in
                    GridRow {
                        ForEach(row, id: \.self) { key in
                            keyButton(key)
                        }
                    }
                }
            }
        }
        .sensoryFeedback(.selection, trigger: pin)
    }

    @ViewBuilder
    private func keyButton(_ key: String) -> some View {
        if key.isEmpty {
            Color.clear.frame(width: 76, height: 76)
        } else {
            Button {
                press(key)
            } label: {
                Group {
                    if key == "⌫" {
                        Image(systemName: "delete.left").font(.title2)
                    } else {
                        Text(key).font(.system(size: 32, weight: .medium, design: .rounded))
                    }
                }
                .frame(width: 76, height: 76)
                .background(key == "⌫" ? Color.clear : Color(uiColor: .secondarySystemBackground), in: Circle())
                .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .disabled(isDisabled || (key == "⌫" && pin.isEmpty))
            .accessibilityLabel(key == "⌫" ? "Löschen" : key)
            .accessibilityIdentifier(key == "⌫" ? "pinDelete" : "pinKey\(key)")
        }
    }

    private func press(_ key: String) {
        if key == "⌫" {
            if !pin.isEmpty { pin.removeLast() }
            return
        }
        guard pin.count < PINPolicy.length else { return }
        pin.append(key)
        if pin.count == PINPolicy.length {
            onComplete(pin)
        }
    }
}
