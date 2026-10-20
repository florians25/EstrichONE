import SwiftUI
import UIKit

/// Meldet, wenn eine Zeit lang niemand den Bildschirm berührt hat (Poolgerät: automatische Sperre).
/// Ein unsichtbarer Gestenerkenner am Fenster sieht jede Berührung, ohne sie abzufangen.
struct InactivityMonitor: UIViewRepresentable {
    let timeout: TimeInterval
    let onTimeout: @MainActor () -> Void

    func makeUIView(context: Context) -> UIView {
        let view = WindowAttachingView()
        view.onWindow = { window in context.coordinator.attach(to: window) }
        return view
    }

    func updateUIView(_ uiView: UIView, context: Context) {
        context.coordinator.timeout = timeout
        context.coordinator.onTimeout = onTimeout
    }

    static func dismantleUIView(_ uiView: UIView, coordinator: Coordinator) {
        coordinator.detach()
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(timeout: timeout, onTimeout: onTimeout)
    }

    @MainActor
    final class Coordinator: NSObject, UIGestureRecognizerDelegate {
        var timeout: TimeInterval
        var onTimeout: @MainActor () -> Void
        private var recognizer: UIGestureRecognizer?
        private var timer: Timer?

        init(timeout: TimeInterval, onTimeout: @escaping @MainActor () -> Void) {
            self.timeout = timeout
            self.onTimeout = onTimeout
        }

        func attach(to window: UIWindow) {
            guard recognizer == nil else { return }
            let recognizer = TouchObserver(target: self, action: #selector(touched))
            recognizer.delegate = self
            recognizer.cancelsTouchesInView = false
            recognizer.delaysTouchesBegan = false
            window.addGestureRecognizer(recognizer)
            self.recognizer = recognizer
            restart()
        }

        func detach() {
            timer?.invalidate()
            if let recognizer {
                recognizer.view?.removeGestureRecognizer(recognizer)
            }
            recognizer = nil
        }

        @objc private func touched() {
            restart()
        }

        private func restart() {
            timer?.invalidate()
            timer = Timer.scheduledTimer(withTimeInterval: timeout, repeats: false) { [weak self] _ in
                MainActor.assumeIsolated { self?.onTimeout() }
            }
        }

        func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer,
                               shouldRecognizeSimultaneouslyWith otherGestureRecognizer: UIGestureRecognizer) -> Bool {
            true
        }
    }

    /// Meldet jede Berührung, erkennt aber nie eine Geste.
    private final class TouchObserver: UIGestureRecognizer {
        override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
            super.touchesBegan(touches, with: event)
            state = .began
        }

        override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent) {
            super.touchesEnded(touches, with: event)
            state = .ended
        }

        override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent) {
            super.touchesCancelled(touches, with: event)
            state = .cancelled
        }
    }

    private final class WindowAttachingView: UIView {
        var onWindow: ((UIWindow) -> Void)?

        override func didMoveToWindow() {
            super.didMoveToWindow()
            if let window { onWindow?(window) }
        }
    }
}
