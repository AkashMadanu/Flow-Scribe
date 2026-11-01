import Foundation

/// Tidies raw speech recognizer output before it is typed.
///
/// The model already adds punctuation, question marks and capitals, so this
/// deliberately does little: it removes hesitation sounds, turns a spoken
/// "new line" / "new paragraph" into a real line break, and fixes spacing and
/// the capital letter after a break. It never re-capitalises the model's own
/// sentences (that would break "e.g." and "3.5").
public enum TextCleaner {

    /// Hesitation sounds that are not real words in English dictation.
    /// "like", "ah" and "you know" are left alone: they are often meant.
    static let fillers = ["um", "umm", "uh", "uhh", "uhm", "erm", "er", "hmm", "mm", "mhm"]

    public static func clean(_ raw: String) -> String {
        var text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty { return "" }

        // 1. Fillers, with the comma before them and a comma right after them:
        //    "I think, um, we should" -> "I think we should".
        //    A full stop after a filler is kept: "So, uh. Next" -> "So. Next".
        let alternation = fillers.joined(separator: "|")
        text = replace(text, #"(?i)(?:,\s*)?\b(?:"# + alternation + #")\b,?"#, with: "")

        // 2. Spoken layout commands. A comma before the command is dropped;
        //    a full stop before it is kept, the punctuation after it is dropped.
        text = replace(text, #"(?i),?\s*\bnew paragraph\b[,.:]?\s*"#, with: "\n\n")
        text = replace(text, #"(?i),?\s*\bnew line\b[,.:]?\s*"#, with: "\n")

        // 3. Whitespace: single spaces, none before punctuation, none hugging
        //    line breaks, at most one blank line, nothing dangling at the start.
        text = replace(text, #"[ \t]+"#, with: " ")
        text = replace(text, #" +([,.!?;:])"#, with: "$1")
        text = replace(text, #" *\n *"#, with: "\n")
        text = replace(text, #"\n{3,}"#, with: "\n\n")
        text = replace(text, #"^[,.;:\s]+"#, with: "")
        text = replace(text, #"(?m)^[,;:] *"#, with: "")
        text = text.trimmingCharacters(in: .whitespacesAndNewlines)
        // Nothing but punctuation left (the user only said "um."): nothing to type.
        if !text.contains(where: { $0.isLetter || $0.isNumber }) { return "" }

        // 4. Capital letter at the very start and after each inserted break.
        return capitaliseLineStarts(text)
    }

    /// Decides whether a new dictation needs a leading space so two dictations
    /// in a row do not run together ("Hello.World"). `previousEnd` is the last
    /// character of the previous insertion into the same app shortly before,
    /// or nil when there was none.
    public static func joinPrefix(previousEnd: Character?, next: String) -> String {
        guard let previousEnd, let first = next.first else { return "" }
        if previousEnd.isWhitespace || first.isWhitespace { return "" }
        if ",.!?;:".contains(first) { return "" }
        return " "
    }

    // MARK: - Helpers

    static func capitaliseLineStarts(_ text: String) -> String {
        var result = ""
        result.reserveCapacity(text.count)
        var atLineStart = true
        for ch in text {
            if atLineStart, ch.isLetter {
                result.append(contentsOf: String(ch).uppercased())
                atLineStart = false
                continue
            }
            result.append(ch)
            if ch == "\n" {
                atLineStart = true
            } else if !ch.isWhitespace, !"\"'(".contains(ch) {
                atLineStart = false
            }
        }
        return result
    }

    static func replace(_ text: String, _ pattern: String, with template: String) -> String {
        guard let regex = try? NSRegularExpression(pattern: pattern) else {
            assertionFailure("Bad pattern: \(pattern)")
            return text
        }
        let range = NSRange(text.startIndex..., in: text)
        return regex.stringByReplacingMatches(in: text, range: range, withTemplate: template)
    }
}
