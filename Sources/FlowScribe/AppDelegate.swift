import AVFoundation
import AppKit
import FlowScribeCore
import ServiceManagement
import os

private let timingLog = Logger(subsystem: "com.akashmadanu.flowscribe", category: "timing")

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let hotkey = HotkeyMonitor()
    private let recorder = AudioRecorder()
    private let transcriber = Transcriber()
    private let history = HistoryStore(fileURL: HistoryStore.defaultURL())
    private let hud = HUD()

    private enum Status: Equatable {
        case loading, ready, recording, transcribing
        case failed(String)
    }
    private var status: Status = .loading { didSet { updateIcon() } }

    /// Dictations finish in the order they were spoken, even if a new one
    /// starts while the previous one is still being transcribed.
    private var pipeline: Task<Void, Never>?
    private var lastInsert: (app: pid_t?, end: Character, at: Date)?
    /// A released dictation still recording its short tail.
    private var pendingFinish: (releasedAt: Date, targetApp: pid_t?, stoppedAtLimit: Bool, task: Task<Void, Never>)?

    /// Recordings shorter than this are treated as accidental.
    private let minimumSamples = Int(AudioRecorder.sampleRate * 0.25)

    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        updateIcon()

        hotkey.onEvent = { [weak self] event in self?.handle(event) }
        hotkey.start()
        recorder.onLimitReached = { [weak self] in
            MainActor.assumeIsolated { self?.recordingLimitReached() }
        }

        // Ask for both permissions up front so the first dictation works.
        AVCaptureDevice.requestAccess(for: .audio) { _ in }
        if !TextInserter.hasAccessibility(prompt: true) {
            Task { await rearmHotkeyWhenTrusted() }
        }

        Task { await loadModel() }
        registerLoginItemOnce()
    }

    /// Start at login, set up once on first launch. If the user later turns it
    /// off in System Settings > General > Login Items, that choice is kept.
    private func registerLoginItemOnce() {
        let key = "loginItemRegistered"
        guard !UserDefaults.standard.bool(forKey: key) else { return }
        do {
            try SMAppService.mainApp.register()
            UserDefaults.standard.set(true, forKey: key)
        } catch {
            NSLog("FlowScribe: login item registration failed: \(error)")
        }
    }

    /// Keyboard monitors installed before Accessibility access is granted stay
    /// deaf until they are installed again, so re-install them once it is.
    private func rearmHotkeyWhenTrusted() async {
        while !TextInserter.hasAccessibility(prompt: false) {
            try? await Task.sleep(for: .seconds(2))
        }
        hotkey.start()
    }

    // MARK: - Model

    private func loadModel() async {
        status = .loading
        do {
            try await transcriber.load()
            status = .ready
        } catch {
            status = .failed("Speech model failed to load: \(error.localizedDescription)")
        }
    }

    // MARK: - Dictation

    private func handle(_ event: HotkeyMonitor.Event) {
        switch event {
        case .start: startRecording()
        case .finish: finishRecording()
        case .cancel: cancelRecording()
        }
    }

    private func startRecording() {
        completeRecording()
        guard !recorder.isRecording else { return }
        if case .failed(let message) = status {
            fail(message)
            hotkey.reset()
            return
        }
        guard AVCaptureDevice.authorizationStatus(for: .audio) == .authorized else {
            fail("Allow microphone access in System Settings > Privacy & Security > Microphone")
            hotkey.reset()
            return
        }
        do {
            try recorder.start()
            status = .recording
            NSSound(named: "Tink")?.play()
            hud.show("Listening", sticky: true)
        } catch {
            fail("Microphone error: \(error.localizedDescription)")
            hotkey.reset()
        }
    }

    private func cancelRecording() {
        guard recorder.isRecording else { return }
        pendingFinish?.task.cancel()
        pendingFinish = nil
        _ = recorder.stop()
        status = transcriberReadyStatus()
        hud.hide()
    }

    /// The recording hit `AudioRecorder.maxSeconds`: stop instead of quietly
    /// dropping what comes after, and type everything up to that point.
    private func recordingLimitReached() {
        guard recorder.isRecording, pendingFinish == nil else { return }
        hotkey.reset()
        NSSound(named: "Basso")?.play()
        finishRecording(stoppedAtLimit: true)
    }

    private func finishRecording(stoppedAtLimit: Bool = false) {
        guard recorder.isRecording, pendingFinish == nil else { return }
        let releasedAt = Date()
        let targetApp = NSWorkspace.shared.frontmostApplication?.processIdentifier
        // Keep listening briefly: people often release the key while still
        // saying the last word, which otherwise gets clipped.
        let task = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(250))
            guard !Task.isCancelled else { return }
            self?.completeRecording()
        }
        pendingFinish = (releasedAt, targetApp, stoppedAtLimit, task)
    }

    /// Stops the microphone after the tail and queues the audio. Also called
    /// early when a new dictation starts during the tail, so none is lost.
    private func completeRecording() {
        guard let pending = pendingFinish else { return }
        pendingFinish = nil
        pending.task.cancel()
        let samples = recorder.stop()
        guard samples.count >= minimumSamples else {
            status = transcriberReadyStatus()
            hud.hide()
            return
        }
        let releasedAt = pending.releasedAt
        let targetApp = pending.targetApp
        let stoppedAtLimit = pending.stoppedAtLimit
        NSSound(named: "Pop")?.play()
        let previous = pipeline
        pipeline = Task { [weak self] in
            await previous?.value
            await self?.process(
                samples, targetApp: targetApp, releasedAt: releasedAt, stoppedAtLimit: stoppedAtLimit)
        }
    }

    private func process(_ samples: [Float], targetApp: pid_t?, releasedAt: Date, stoppedAtLimit: Bool) async {
        // Wait for the model if the user dictates during the first download.
        if !(await transcriber.isReady) {
            hud.show("Getting the speech model ready", sticky: true)
            while !(await transcriber.isReady) {
                if case .failed(let message) = status { fail(message); return }
                try? await Task.sleep(for: .milliseconds(200))
            }
        }
        status = .transcribing
        hud.show("Transcribing", sticky: true)

        let raw: String
        let transcribeStart = Date()
        do {
            raw = try await transcriber.transcribe(samples)
        } catch {
            status = .ready
            fail("Transcription failed: \(error.localizedDescription)")
            return
        }
        status = recorder.isRecording ? .recording : .ready

        // Read each time (a few bytes) so edits apply to the next dictation.
        var text = WordList.load(from: WordList.defaultURL()).apply(to: TextCleaner.clean(raw))
        guard !text.isEmpty else {
            NSSound(named: "Basso")?.play()
            hud.show("No speech heard. Check your microphone input.")
            return
        }

        // Save first, so the text is never lost whatever happens next.
        do { try history.add(text) } catch { NSLog("FlowScribe: history save failed: \(error)") }

        if let last = lastInsert, last.app == targetApp, Date().timeIntervalSince(last.at) < 120 {
            text = TextCleaner.joinPrefix(previousEnd: last.end, next: text) + text
        }

        let transcribeMs = Int(Date().timeIntervalSince(transcribeStart) * 1000)
        let outcome = await TextInserter.insert(text)
        // Cmd+V is posted 900 ms before insert() returns (clipboard restore delay).
        let pasteMs = Int(Date().timeIntervalSince(releasedAt) * 1000) - (outcome == .pasted ? 900 : 0)
        let seconds = String(format: "%.1f", Double(samples.count) / AudioRecorder.sampleRate)
        timingLog.notice(
            "FlowScribe: \(seconds, privacy: .public) s audio, model \(transcribeMs) ms, release to paste \(pasteMs) ms, \(MemoryUsage.footprintMegabytes()) MB"
        )

        switch outcome {
        case .pasted:
            if stoppedAtLimit {
                let minutes = Int(AudioRecorder.maxSeconds / 60)
                hud.show("Stopped at the \(minutes)-minute limit. Everything before it was typed.", seconds: 6)
            } else {
                hud.hide()
            }
            if let end = text.last { lastInsert = (targetApp, end, Date()) }
        case .copiedNoPermission:
            NSSound(named: "Basso")?.play()
            hud.show("Copied. Turn on FlowScribe in Privacy & Security > Accessibility to paste automatically.", seconds: 6)
        case .copiedSecureInput:
            hud.show("Copied. A password field blocks typing, so press Cmd+V.", seconds: 4)
        }
    }

    private func transcriberReadyStatus() -> Status {
        if case .failed = status { return status }
        return status == .loading ? .loading : .ready
    }

    private func fail(_ message: String) {
        NSSound(named: "Basso")?.play()
        hud.show(message, seconds: 6)
    }

    // MARK: - Menu bar

    private func updateIcon() {
        let name: String
        switch status {
        case .loading: name = "arrow.down.circle"
        case .ready: name = "mic"
        case .recording: name = "mic.fill"
        case .transcribing: name = "ellipsis.circle"
        case .failed: name = "exclamationmark.triangle"
        }
        let image = NSImage(systemSymbolName: name, accessibilityDescription: "FlowScribe")
        image?.isTemplate = true
        statusItem.button?.image = image
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()

        let statusText: String
        switch status {
        case .loading: statusText = "Downloading speech model (first launch only)"
        case .ready: statusText = "Hold Right Option to dictate. Double tap for hands free."
        case .recording: statusText = "Listening"
        case .transcribing: statusText = "Transcribing"
        case .failed(let message): statusText = message
        }
        menu.addItem(disabled(statusText))

        if case .failed = status {
            menu.addItem(item("Try loading the model again", #selector(retryModel)))
        }
        if !TextInserter.hasAccessibility(prompt: false) {
            menu.addItem(item("Allow pasting (Accessibility access)", #selector(openAccessibility)))
        }

        menu.addItem(.separator())
        menu.addItem(disabled(history.notes.isEmpty ? "No transcriptions yet" : "Last 5 (click to copy)"))
        for note in history.notes {
            let title = note.text.replacingOccurrences(of: "\n", with: " ")
            let menuItem = item(title.count > 70 ? String(title.prefix(70)) + "…" : title, #selector(copyNote(_:)))
            menuItem.representedObject = note.text
            menuItem.toolTip = note.text
            menu.addItem(menuItem)
        }

        menu.addItem(.separator())
        menu.addItem(item("Edit word list…", #selector(editWordList)))
        menu.addItem(disabled("Memory in use: \(MemoryUsage.footprintMegabytes()) MB"))
        menu.addItem(item("Quit FlowScribe", #selector(quit), key: "q"))
    }

    private func item(_ title: String, _ action: Selector, key: String = "") -> NSMenuItem {
        let menuItem = NSMenuItem(title: title, action: action, keyEquivalent: key)
        menuItem.target = self
        return menuItem
    }

    private func disabled(_ title: String) -> NSMenuItem {
        let menuItem = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        menuItem.isEnabled = false
        return menuItem
    }

    @objc private func copyNote(_ sender: NSMenuItem) {
        guard let text = sender.representedObject as? String else { return }
        TextInserter.copy(text)
    }

    @objc private func editWordList() {
        let url = WordList.defaultURL()
        if !FileManager.default.fileExists(atPath: url.path) {
            try? FileManager.default.createDirectory(
                at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try? WordList.template.write(to: url, atomically: true, encoding: .utf8)
        }
        NSWorkspace.shared.open(
            [url], withApplicationAt: URL(fileURLWithPath: "/System/Applications/TextEdit.app"),
            configuration: NSWorkspace.OpenConfiguration())
    }

    @objc private func retryModel() {
        Task { await loadModel() }
    }

    @objc private func openAccessibility() {
        _ = TextInserter.hasAccessibility(prompt: true)
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}
