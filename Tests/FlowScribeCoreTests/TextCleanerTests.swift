import XCTest
@testable import FlowScribeCore

final class TextCleanerTests: XCTestCase {
    func testEmptyAndWhitespace() {
        XCTAssertEqual(TextCleaner.clean(""), "")
        XCTAssertEqual(TextCleaner.clean("   \n "), "")
    }

    func testOnlyFillersBecomesEmpty() {
        XCTAssertEqual(TextCleaner.clean("Um."), "")
        XCTAssertEqual(TextCleaner.clean("Um, uh"), "")
    }

    func testKeepsModelPunctuationAndQuestions() {
        XCTAssertEqual(TextCleaner.clean("Can you send it today? Thanks."), "Can you send it today? Thanks.")
    }

    func testRemovesFillersWithTheirCommas() {
        XCTAssertEqual(TextCleaner.clean("I think, um, we should go."), "I think we should go.")
        XCTAssertEqual(TextCleaner.clean("Um, I think so."), "I think so.")
        XCTAssertEqual(TextCleaner.clean("So, uh. Next point."), "So. Next point.")
        XCTAssertEqual(TextCleaner.clean("We went uh home."), "We went home.")
    }

    func testDoesNotTouchWordsContainingFillers() {
        XCTAssertEqual(TextCleaner.clean("The umbrella is under the hummus."), "The umbrella is under the hummus.")
        XCTAssertEqual(TextCleaner.clean("Ah, I see."), "Ah, I see.")
    }

    func testNewLineAndParagraph() {
        XCTAssertEqual(TextCleaner.clean("Hello. New paragraph. Next part."), "Hello.\n\nNext part.")
        XCTAssertEqual(TextCleaner.clean("First item, new line second item"), "First item\nSecond item")
        XCTAssertEqual(TextCleaner.clean("Dear Sam, new line thanks."), "Dear Sam\nThanks.")
    }

    func testDoesNotRecapitaliseInsideSentences() {
        XCTAssertEqual(TextCleaner.clean("Use e.g. this one, version 3.5 works."), "Use e.g. this one, version 3.5 works.")
    }

    func testCollapsesSpacesAndSpaceBeforePunctuation() {
        XCTAssertEqual(TextCleaner.clean("Hello   there , friend ."), "Hello there, friend.")
    }

    func testNumberedListGoesOnSeparateLines() {
        XCTAssertEqual(
            TextCleaner.clean("My tasks. Number one, buy milk. Number two, call mom. Number three, gym."),
            "My tasks.\n1. Buy milk.\n2. Call mom.\n3. Gym.")
        XCTAssertEqual(TextCleaner.clean("Number 1 eggs, number 2 bread."), "1. Eggs\n2. Bread.")
    }

    func testSentenceAfterLastItemStartsNewParagraph() {
        XCTAssertEqual(
            TextCleaner.clean("Number one, finish it. Number two, push the code. So I think this is working good."),
            "1. Finish it.\n2. Push the code.\n\nSo I think this is working good.")
    }

    func testNumberOneAloneIsNotAList() {
        XCTAssertEqual(TextCleaner.clean("This is my number one priority."), "This is my number one priority.")
        XCTAssertEqual(TextCleaner.clean("Call number two first."), "Call number two first.")
    }

    func testSpokenEmailAddress() {
        XCTAssertEqual(
            TextCleaner.clean("My email is sam.lee777 at gmail.com, thanks."),
            "My email is sam.lee777@gmail.com, thanks.")
        XCTAssertEqual(TextCleaner.clean("Write to sam.lee at outlook dot com"), "Write to sam.lee@outlook.com")
    }

    func testAtBeforeOrdinaryWebsiteIsLeftAlone() {
        XCTAssertEqual(TextCleaner.clean("Look at example.com today."), "Look at example.com today.")
    }

    func testJoinPrefix() {
        XCTAssertEqual(TextCleaner.joinPrefix(previousEnd: nil, next: "Hi"), "")
        XCTAssertEqual(TextCleaner.joinPrefix(previousEnd: ".", next: "Hi"), " ")
        XCTAssertEqual(TextCleaner.joinPrefix(previousEnd: " ", next: "Hi"), "")
        XCTAssertEqual(TextCleaner.joinPrefix(previousEnd: "\n", next: "Hi"), "")
        XCTAssertEqual(TextCleaner.joinPrefix(previousEnd: "o", next: ", then"), "")
    }
}
