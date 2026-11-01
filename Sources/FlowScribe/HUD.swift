import AppKit

/// A small pill near the bottom of the screen that shows "Listening",
/// "Transcribing" or a problem. It never takes keyboard focus, so the text
/// still lands in the field you were typing in.
@MainActor
final class HUD {
    private let panel: NSPanel
    private let label = NSTextField(labelWithString: "")
    private var hideTask: Task<Void, Never>?

    init() {
        panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 200, height: 36),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered, defer: true)
        panel.isFloatingPanel = true
        panel.level = .statusBar
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]

        let background = NSVisualEffectView()
        background.material = .hudWindow
        background.state = .active
        background.wantsLayer = true
        background.layer?.cornerRadius = 18

        label.font = .systemFont(ofSize: 13, weight: .medium)
        label.textColor = .labelColor
        label.alignment = .center
        label.lineBreakMode = .byWordWrapping
        label.maximumNumberOfLines = 3
        label.translatesAutoresizingMaskIntoConstraints = false
        background.addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: background.leadingAnchor, constant: 16),
            label.trailingAnchor.constraint(equalTo: background.trailingAnchor, constant: -16),
            label.centerYAnchor.constraint(equalTo: background.centerYAnchor),
        ])
        panel.contentView = background
    }

    /// Shows a message. Sticky messages stay until replaced or hidden;
    /// others disappear after `seconds`.
    func show(_ message: String, sticky: Bool = false, seconds: Double = 3) {
        hideTask?.cancel()
        label.stringValue = message

        let screen = NSScreen.main ?? NSScreen.screens.first
        let visible = screen?.visibleFrame ?? .zero
        let width = min(max(label.intrinsicContentSize.width + 40, 140), 460)
        let height: CGFloat = message.count > 60 ? 64 : 36
        panel.setFrame(
            NSRect(x: visible.midX - width / 2, y: visible.minY + 40, width: width, height: height),
            display: true)
        panel.orderFrontRegardless()

        guard !sticky else { return }
        hideTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(seconds))
            guard !Task.isCancelled else { return }
            self?.panel.orderOut(nil)
        }
    }

    func hide() {
        hideTask?.cancel()
        panel.orderOut(nil)
    }
}
