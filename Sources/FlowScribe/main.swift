import AppKit

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.regular)  // Dock icon as well as the menu bar icon
    withExtendedLifetime(delegate) { app.run() }
}
