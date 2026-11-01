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

    private var engine: AVAudioEngine?
    private var samples: [Float] = []
    private let lock = NSLock()

    var isRecording: Bool { engine != nil }

    func start() throws {
        stopEngine()
        lock.lock(); samples.removeAll(keepingCapacity: true); lock.unlock()

        // A fresh engine each time picks up the current default input, so
        // plugging in a headset or switching to AirPods just works.
        let engine = AVAudioEngine()
        let input = engine.inputNode
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
            if self.samples.count < maxSamples { self.samples.append(contentsOf: chunk) }
            self.lock.unlock()
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
