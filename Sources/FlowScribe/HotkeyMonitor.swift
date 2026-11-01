import AppKit

/// Watches the Right Option key.
///
/// - Hold it: records while held, transcribes on release (push to talk).
/// - Double tap it: records hands free until you tap it once more.
/// - Escape cancels a recording. Pressing another key while holding it
///   (an Option+letter shortcut) cancels too, so normal shortcuts still work.
@MainActor
final class HotkeyMonitor {
    enum Event { case start, finish, cancel }

    var onEvent: ((Event) -> Void)?

    private enum State { case idle, holding, waitingForSecondTap, handsFree }
    private var state: State = .idle
    private var pressedAt = Date.distantPast
    private var tapTimer: Timer?
    private var monitors: [Any] = []

    private let rightOptionKeyCode: UInt16 = 61
    private let escapeKeyCode: UInt16 = 53
    /// Device-dependent bit for the right Option key (NX_DEVICERALTKEYMASK).
    private let rightOptionMask: UInt = 0x40
    /// A press shorter than this is a tap, not a hold.
    private let tapThreshold: TimeInterval = 0.30
    /// The second tap of a double tap must arrive within this window.
    private let doubleTapWindow: TimeInterval = 0.35

    func start() {
        stop()
        let mask: NSEvent.EventTypeMask = [.flagsChanged, .keyDown]
        if let global = NSEvent.addGlobalMonitorForEvents(matching: mask, handler: { [weak self] event in
            MainActor.assumeIsolated { self?.handle(event) }
        }) {
            monitors.append(global)
        }
        if let local = NSEvent.addLocalMonitorForEvents(matching: mask, handler: { [weak self] event in
            MainActor.assumeIsolated { self?.handle(event) }
            return event
        }) {
            monitors.append(local)
        }
    }

    func stop() {
        monitors.forEach(NSEvent.removeMonitor)
        monitors.removeAll()
    }

    /// Called by the app when recording had to stop for another reason.
    func reset() {
        tapTimer?.invalidate()
        state = .idle
    }

    private func handle(_ event: NSEvent) {
        switch event.type {
        case .flagsChanged where event.keyCode == rightOptionKeyCode:
            let isDown = event.modifierFlags.rawValue & rightOptionMask != 0
            isDown ? keyDown() : keyUp()
        case .keyDown:
            if event.keyCode == escapeKeyCode, state != .idle {
                cancel()
            } else if state == .holding || state == .waitingForSecondTap {
                // Option was used as a modifier for a shortcut, not to dictate.
                cancel()
            }
        default:
            break
        }
    }

    private func keyDown() {
        switch state {
        case .idle:
            state = .holding
            pressedAt = Date()
            onEvent?(.start)  // start at once so the first word is not clipped
        case .waitingForSecondTap:
            tapTimer?.invalidate()
            state = .handsFree  // already recording since the first tap
        case .handsFree:
            state = .idle
            onEvent?(.finish)
        case .holding:
            break
        }
    }

    private func keyUp() {
        guard state == .holding else { return }
        if Date().timeIntervalSince(pressedAt) >= tapThreshold {
            state = .idle
            onEvent?(.finish)
            return
        }
        // A short tap: wait briefly to see whether it is a double tap.
        state = .waitingForSecondTap
        tapTimer?.invalidate()
        tapTimer = Timer.scheduledTimer(withTimeInterval: doubleTapWindow, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, self.state == .waitingForSecondTap else { return }
                self.cancel()  // a single accidental tap records nothing
            }
        }
    }

    private func cancel() {
        tapTimer?.invalidate()
        state = .idle
        onEvent?(.cancel)
    }
}
