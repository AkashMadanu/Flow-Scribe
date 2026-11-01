import Foundation

/// Tidies raw speech recognizer output before it is typed.
///
/// The model already adds punctuation, question marks and capitals, so this
/// deliberately does little: it removes hesitation sounds, turns a spoken
/// "new line" / "new paragraph" into a real line break, puts a spoken
/// "number one, number two" list on separate lines, joins spoken email
/// addresses, and fixes spacing and the capital letter after a break. It never re-capitalises the model's own
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
        text = numberedList(text)

        // Spoken email addresses: "sam at gmail.com" / "sam at gmail dot com".
        // Only well-known mail domains, so "look at example.com" is left alone.
        text = replace(
            text,
            #"(?i)\b([a-z0-9._%+-]+) at (gmail|googlemail|yahoo|outlook|hotmail|icloud|live|proton|protonmail|aol|zoho)(?:\.| dot )(com|in|me|net|co\.uk|co\.in)\b"#,
            with: "$1@$2.$3")

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

    static let numberWords = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"]

    /// "Number one, milk. Number two, eggs." becomes one item per line:
    /// "1. Milk.\n2. Eggs." Only a run that starts at one and reaches two is
    /// treated as a list, so "my number one priority" stays as spoken.
    static func numberedList(_ text: String) -> String {
        let pattern = #"(?i),?\s*\bnumber\s+("# + numberWords.joined(separator: "|") + #"|10|[1-9])\b[,.:]?\s*"#
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return text }
        let ns = text as NSString
        var expected = 1
        var items: [(range: NSRange, number: Int)] = []
        for match in regex.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            let word = ns.substring(with: match.range(at: 1)).lowercased()
            let number = Int(word) ?? (numberWords.firstIndex(of: word).map { $0 + 1 } ?? 0)
            if number == expected {
                items.append((match.range, number))
                expected += 1
            }
        }
        guard items.count >= 2 else { return text }

        var result = ""
        var cursor = 0
        for item in items {
            result += ns.substring(with: NSRange(location: cursor, length: item.range.location - cursor))
            result += "\n\(item.number). "
            cursor = item.range.location + item.range.length
            // Each item starts with a capital letter.
            let rest = ns.substring(from: cursor)
            if let first = rest.first, first.isLetter {
                result += first.uppercased()
                cursor += String(first).utf16.count
            }
        }
        // The list ends with the last item's first sentence; what follows
        // starts a new paragraph instead of running on in that item.
        let tail = ns.substring(from: cursor)
        return result + replaceFirst(tail, #"([.!?])\s+(?=\p{Lu})"#, with: "$1\n\n")
    }

    static func replaceFirst(_ text: String, _ pattern: String, with template: String) -> String {
        guard let regex = try? NSRegularExpression(pattern: pattern),
            let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text))
        else { return text }
        let replacement = regex.replacementString(for: match, in: text, offset: 0, template: template)
        return (text as NSString).replacingCharacters(in: match.range, with: replacement)
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
