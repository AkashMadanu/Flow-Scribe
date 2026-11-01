import XCTest
@testable import FlowScribeCore

final class HistoryStoreTests: XCTestCase {
    private var url: URL!

    override func setUp() {
        url = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathComponent("history.json")
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: url.deletingLastPathComponent())
    }

    func testStartsEmptyWhenNoFile() {
        XCTAssertEqual(HistoryStore(fileURL: url).notes, [])
    }

    func testKeepsOnlyLastFiveNewestFirst() throws {
        let store = HistoryStore(fileURL: url)
        for i in 1...7 { try store.add("note \(i)") }
        XCTAssertEqual(store.notes.map(\.text), ["note 7", "note 6", "note 5", "note 4", "note 3"])
    }

    func testOlderNotesAreGoneFromDiskToo() throws {
        let store = HistoryStore(fileURL: url)
        for i in 1...6 { try store.add("note \(i)") }
        let raw = try String(contentsOf: url, encoding: .utf8)
        XCTAssertFalse(raw.contains("note 1\""))
        XCTAssertEqual(HistoryStore(fileURL: url).notes.count, 5)
    }

    func testPersistsAcrossLaunches() throws {
        try HistoryStore(fileURL: url).add("hello")
        XCTAssertEqual(HistoryStore(fileURL: url).notes.map(\.text), ["hello"])
    }

    func testIgnoresEmptyText() throws {
        let store = HistoryStore(fileURL: url)
        try store.add("   \n")
        XCTAssertEqual(store.notes, [])
        XCTAssertFalse(FileManager.default.fileExists(atPath: url.path))
    }

    func testDamagedFileStartsFresh() throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data("not json".utf8).write(to: url)
        let store = HistoryStore(fileURL: url)
        XCTAssertEqual(store.notes, [])
        try store.add("after damage")
        XCTAssertEqual(HistoryStore(fileURL: url).notes.map(\.text), ["after damage"])
    }

    func testOversizedFileFromOlderBuildIsTrimmedOnLoad() throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let notes = (1...9).map { Note(text: "n\($0)") }
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        try encoder.encode(notes).write(to: url)
        XCTAssertEqual(HistoryStore(fileURL: url).notes.count, 5)
        XCTAssertEqual(HistoryStore(fileURL: url).notes.map(\.text), ["n1", "n2", "n3", "n4", "n5"])
    }

    func testFileIsPrivate() throws {
        try HistoryStore(fileURL: url).add("secret")
        let attributes = try FileManager.default.attributesOfItem(atPath: url.path)
        XCTAssertEqual((attributes[.posixPermissions] as? NSNumber)?.intValue, 0o600)
    }
}
