import AVFoundation

/// Records the default microphone as 16 kHz mono Float samples, the format
/// the speech model expects. The microphone is only open while recording.
final class AudioRecorder {
    enum RecorderError: LocalizedError {
        case noInput
        case converter
        var errorDescription: String? {
            switch self {
            case .noInput: return "No microphone found"
            case .converter: return "Could not convert microphone audio"
            }
        }
    }

    static let sampleRate: Double = 16_000
    /// Hard stop so a forgotten hands-free recording cannot grow forever.
    static let maxSeconds: Double = 600

    /// Called once on the main queue when a recording reaches `maxSeconds`.
    var onLimitReached: (() -> Void)?

    private var engine: AVAudioEngine?
    private var samples: [Float] = []
    private var limitReported = false
    private let lock = NSLock()

    var isRecording: Bool { engine != nil }

    /// UID of the microphone chosen in the menu; nil follows the system default.
    /// A chosen microphone that is unplugged also falls back to the default.
    func start(deviceUID: String? = nil) throws {
        stopEngine()
        lock.lock(); samples.removeAll(keepingCapacity: true); limitReported = false; lock.unlock()

        // A fresh engine each time picks up the current default input, so
        // plugging in a headset or switching to AirPods just works.
        let engine = AVAudioEngine()
        let input = engine.inputNode
        if let deviceUID, var deviceID = AudioDevices.inputs().first(where: { $0.uid == deviceUID })?.id,
            let unit = input.audioUnit
        {
            AudioUnitSetProperty(
                unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0,
                &deviceID, UInt32(MemoryLayout<AudioDeviceID>.size))
        }
        let inputFormat = input.outputFormat(forBus: 0)
        guard inputFormat.sampleRate > 0, inputFormat.channelCount > 0 else { throw RecorderError.noInput }
        guard
            let target = AVAudioFormat(
                commonFormat: .pcmFormatFloat32, sampleRate: Self.sampleRate, channels: 1, interleaved: false),
            let converter = AVAudioConverter(from: inputFormat, to: target)
        else { throw RecorderError.converter }

        let maxSamples = Int(Self.sampleRate * Self.maxSeconds)
        input.installTap(onBus: 0, bufferSize: 1024, format: inputFormat) { [weak self] buffer, _ in
            guard let self else { return }
            let ratio = Self.sampleRate / inputFormat.sampleRate
            let capacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 32
            guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
            var supplied = false
            var error: NSError?
            converter.convert(to: out, error: &error) { _, status in
                if supplied {
                    status.pointee = .noDataNow
                    return nil
                }
                supplied = true
                status.pointee = .haveData
                return buffer
            }
            guard error == nil, let channel = out.floatChannelData?[0] else { return }
            let chunk = UnsafeBufferPointer(start: channel, count: Int(out.frameLength))
            self.lock.lock()
            var reachedLimit = false
            if self.samples.count < maxSamples {
                self.samples.append(contentsOf: chunk)
            } else if !self.limitReported {
                self.limitReported = true
                reachedLimit = true
            }
            self.lock.unlock()
            if reachedLimit {
                DispatchQueue.main.async { self.onLimitReached?() }
            }
        }

        engine.prepare()
        try engine.start()
        self.engine = engine
    }

    /// Stops recording and returns everything captured.
    func stop() -> [Float] {
        stopEngine()
        lock.lock(); defer { lock.unlock() }
        let captured = samples
        samples = []
        return captured
    }

    private func stopEngine() {
        guard let engine else { return }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        self.engine = nil
    }
}
