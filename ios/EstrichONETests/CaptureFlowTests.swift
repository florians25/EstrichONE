import Testing
import CoreGraphics
@testable import EstrichONE

@Suite("Erfassen nach der Anlage und Kamera")
struct CaptureFlowTests {
    @Test("„Gleich danach“ startet in jedem Anlageweg dasselbe für die neue Bauakte")
    func followUpAfterCreation() {
        #expect(NewBauakteView.Next.open.followUp == .open)
        #expect(NewBauakteView.Next.photograph.followUp == .photo)
        #expect(NewBauakteView.Next.inspect.followUp == .inspection)
    }

    @Test("„Fertig“ wartet, bis alle ausgelösten Fotos gespeichert sind, und schließt genau einmal")
    func closeWaitsForPendingShots() {
        var shots = PendingShots()
        var closes: [Bool] = []
        closes.append(shots.takeClose())

        shots.shutterPressed()
        shots.shutterPressed()
        shots.requestClose()
        #expect(shots.isClosing)
        closes.append(shots.takeClose())

        shots.shotSettled()
        closes.append(shots.takeClose())
        shots.shotSettled()
        closes.append(shots.takeClose())
        closes.append(shots.takeClose())
        #expect(closes == [false, false, false, true, false])
    }

    @Test("Ohne Fotos unterwegs schließt die Kamera sofort; hängt eine Aufnahme, nach Ablauf der Wartezeit")
    func closeImmediatelyOrAfterTimeout() {
        var idle = PendingShots()
        idle.requestClose()
        let idleCloses = idle.takeClose()
        #expect(idleCloses)

        var stuck = PendingShots()
        var closes: [Bool] = []
        stuck.shutterPressed()
        stuck.forceClose()
        closes.append(stuck.takeClose())
        stuck.requestClose()
        closes.append(stuck.takeClose())
        stuck.forceClose()
        closes.append(stuck.takeClose())
        #expect(closes == [false, false, true])
        stuck.shotSettled()
        #expect(stuck.count == 0)
    }
}

@Suite("Videos stärker komprimiert")
struct VideoCompressorTests {
    @Test("Höchstens 720p im Hoch- und Querformat, Seitenverhältnis bleibt, nie vergrößert, gerade Pixelzahlen")
    func targetSize() {
        #expect(VideoCompressor.targetSize(for: CGSize(width: 1920, height: 1080)) == CGSize(width: 1280, height: 720))
        #expect(VideoCompressor.targetSize(for: CGSize(width: 3840, height: 2160)) == CGSize(width: 1280, height: 720))
        #expect(VideoCompressor.targetSize(for: CGSize(width: 1080, height: 1920)) == CGSize(width: 720, height: 1280))
        #expect(VideoCompressor.targetSize(for: CGSize(width: 1440, height: 1080)) == CGSize(width: 960, height: 720))
        #expect(VideoCompressor.targetSize(for: CGSize(width: 640, height: 480)) == CGSize(width: 640, height: 480))
        #expect(VideoCompressor.targetSize(for: CGSize(width: 1281, height: 721)) == CGSize(width: 1280, height: 720))
    }

    @Test("Rechnerisch etwa 15 MB je Minute (2 Mbit/s Video, 64 kbit/s Ton)")
    func bytesPerMinute() {
        #expect(VideoCompressor.bytesPerMinute == 15_480_000)
    }
}
