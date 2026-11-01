import FluidAudio
import Foundation

/// Wraps NVIDIA Parakeet Unified English (0.6B) running on the Neural Engine
/// through FluidAudio. It outputs punctuation and capitals itself, and handles
/// recordings longer than 15 s with overlapping windows.
///
/// The int8 encoder runs on the Neural Engine, which keeps CPU, GPU and app
/// memory use low. The model (a few hundred MB) downloads once on first launch
/// to ~/Library/Application Support/FluidAudio/Models and then works offline.
actor Transcriber {
    private var manager: UnifiedAsrManager?

    var isReady: Bool { manager != nil }

    func load() async throws {
        if manager != nil { return }
        let asr = UnifiedAsrManager(encoderPrecision: .int8)
        try await asr.loadModels(to: nil, configuration: nil, progressHandler: nil)
        // Warm up once so the first real dictation is not slowed by the
        // Neural Engine compiling its execution plan.
        _ = try? await asr.transcribe([Float](repeating: 0, count: 16_000))
        manager = asr
    }

    func transcribe(_ samples: [Float]) async throws -> String {
        guard let manager else { throw ASRError.notInitialized }
        return try await manager.transcribe(samples)
    }
}
