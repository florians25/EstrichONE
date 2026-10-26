import SwiftUI

/// Schriftskala auf Basis von Dynamic Type: Jede Rolle wächst mit der Systemeinstellung „Größerer Text“.
/// Zahlen (Uhrzeiten, Mengen, Nummern) immer mit gleich breiten Ziffern.
enum Typo {
    /// Großer Seitentitel im Inhalt (selten; sonst der Large Title der Navigation).
    static var display: Font { .largeTitle.weight(.bold) }
    /// Titel einer Bauakte oder eines Kunden.
    static var title: Font { .title2.weight(.bold) }
    /// Abschnittsüberschrift auf frei gestalteten Seiten (Heute, Bauakte).
    static var section: Font { .title3.weight(.semibold) }
    /// Titel einer Karte oder einer wichtigen Zeile.
    static var headline: Font { .headline }
    static var body: Font { .body }
    static var bodyStrong: Font { .body.weight(.semibold) }
    static var callout: Font { .callout }
    /// Zweite Zeile, Adressen, Erläuterungen.
    static var subhead: Font { .subheadline }
    static var subheadStrong: Font { .subheadline.weight(.semibold) }
    /// Metadaten: Datum, Verfasser, Zähler.
    static var meta: Font { .footnote }
    static var metaStrong: Font { .footnote.weight(.semibold) }
    /// Beschriftung über Werten (Kenndaten), Chips.
    static var label: Font { .caption.weight(.medium) }
    static var labelStrong: Font { .caption.weight(.semibold) }
    /// Kennzahl in einer Kachel.
    static var metric: Font { .title.weight(.bold).monospacedDigit() }
    /// Uhrzeit eines Einsatzes.
    static var time: Font { .title3.weight(.bold).monospacedDigit() }
}

/// Einheitliche Symbolgewichte: Inhalt `.medium`, Schaltflächen `.semibold`.
enum SymbolWeight {
    static let content: Font.Weight = .medium
    static let control: Font.Weight = .semibold
}
