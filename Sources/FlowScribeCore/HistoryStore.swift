import Foundation

public struct Note: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public let text: String
    public let date: Date

    public init(id: UUID = UUID(), text: String, date: Date = Date()) {
        self.id = id
        self.text = text
        self.date = date
    }
}

/// Keeps only the most recent transcriptions on disk. Older ones are removed
/// from the file on every save, so nothing beyond the limit is ever kept.
public final class HistoryStore {
    public static let limit = 5

    public private(set) var notes: [Note] = []
    private let fileURL: URL
    private let lock = NSLock()

    public init(fileURL: URL) {
        self.fileURL = fileURL
        notes = Self.load(from: fileURL)
        // Enforce the limit on files written by older builds as well.
        if notes.count > Self.limit {
            notes = Array(notes.prefix(Self.limit))
            try? save()
        }
    }

    /// Default location: ~/Library/Application Support/FlowScribe/history.json
    public static func defaultURL() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("FlowScribe", isDirectory: true)
            .appendingPathComponent("history.json")
    }

    /// Adds a note at the top and drops everything past the limit.
    /// Empty text is ignored. Returns the notes after the change.
    @discardableResult
    public func add(_ text: String, date: Date = Date()) throws -> [Note] {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        lock.lock()
        defer { lock.unlock() }
        guard !trimmed.isEmpty else { return notes }
        notes.insert(Note(text: trimmed, date: date), at: 0)
        if notes.count > Self.limit { notes.removeLast(notes.count - Self.limit) }
        try save()
        return notes
    }

    private func save() throws {
        try FileManager.default.createDirectory(
            at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let data = try encoder.encode(notes)
        // Atomic: a crash mid-write never leaves a half-written file.
        try data.write(to: fileURL, options: [.atomic])
        // Transcripts are private; only this user may read the file.
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: fileURL.path)
    }

    private static func load(from url: URL) -> [Note] {
        guard let data = try? Data(contentsOf: url) else { return [] }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        // A damaged file starts a fresh history instead of crashing the app.
        return (try? decoder.decode([Note].self, from: data)) ?? []
    }
}
