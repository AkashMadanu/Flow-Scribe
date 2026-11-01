import XCTest
@testable import FlowScribeCore

final class WordListTests: XCTestCase {
    func testParsesRulesAndSkipsCommentsAndBadLines() {
        let list = WordList(contents: "# comment\n\nJim Ledger = GymLedger\nno equals sign\n = empty\nCloud Code=Claude Code\n")
        XCTAssertEqual(list.rules.count, 2)
        XCTAssertTrue(list.rules.contains(.init(spoken: "Jim Ledger", written: "GymLedger")))
        XCTAssertTrue(list.rules.contains(.init(spoken: "Cloud Code", written: "Claude Code")))
    }

    func testTemplateHasNoActiveRules() {
        XCTAssertEqual(WordList(contents: WordList.template).rules, [])
    }

    func testReplacesWholeWordsIgnoringCase() {
        let list = WordList(contents: "Jim Ledger = GymLedger\nCloud Code = Claude Code")
        XCTAssertEqual(
            list.apply(to: "I built jim ledger with Cloud Code."),
            "I built GymLedger with Claude Code.")
        XCTAssertEqual(list.apply(to: "Jim   Ledger is live"), "GymLedger is live")
    }

    func testDoesNotReplaceInsideOtherWords() {
        let list = WordList(contents: "cloud = Claude")
        XCTAssertEqual(list.apply(to: "Clouds over the cloud."), "Clouds over the Claude.")
    }

    func testLongerPhraseWins() {
        let list = WordList(contents: "Cloud = Claude\nCloud storage = Cloud storage")
        XCTAssertEqual(list.apply(to: "Cloud storage and Cloud"), "Cloud storage and Claude")
    }

    func testWrittenTextIsLiteral() {
        let list = WordList(contents: "price = $1 (approx)")
        XCTAssertEqual(list.apply(to: "The price."), "The $1 (approx).")
    }

    func testMissingFileIsEmpty() {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        XCTAssertEqual(WordList.load(from: url).rules, [])
    }
}
