import Foundation

/// The user's own fixes for words the model mishears, kept in a plain text
/// file with one rule per line: `Jim Ledger = GymLedger`.
///
/// Matching ignores case and only replaces whole words, so a rule for "cloud"
/// never touches "clouds". Longer phrases win over shorter ones.
public struct WordList: Equatable, Sendable {
    public struct Rule: Equatable, Sendable {
        public let spoken: String
        public let written: String
    }

    public let rules: [Rule]

    public static let template = """
        # FlowScribe word list. One rule per line: what the model types = what you want.
        # Matching ignores case and only replaces whole words. Saved changes apply
        # to your next dictation. Lines starting with # are ignored.
        #
        # Jim Ledger = GymLedger

        """

    public init(contents: String) {
        var rules: [Rule] = []
        for line in contents.split(whereSeparator: \.isNewline) {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            guard !trimmed.hasPrefix("#"), let equals = trimmed.firstIndex(of: "=") else { continue }
            let spoken = trimmed[..<equals].trimmingCharacters(in: .whitespaces)
            let written = trimmed[trimmed.index(after: equals)...].trimmingCharacters(in: .whitespaces)
            guard !spoken.isEmpty, !written.isEmpty else { continue }
            rules.append(Rule(spoken: spoken, written: written))
        }
        self.rules = rules.sorted { $0.spoken.count > $1.spoken.count }
    }

    /// A missing or unreadable file is an empty list, never an error.
    public static func load(from url: URL) -> WordList {
        WordList(contents: (try? String(contentsOf: url, encoding: .utf8)) ?? "")
    }

    public static func defaultURL() -> URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("FlowScribe", isDirectory: true)
            .appendingPathComponent("words.txt")
    }

    /// One pass over the text, so a replacement is never rewritten by another rule.
    public func apply(to text: String) -> String {
        guard !rules.isEmpty else { return text }
        // One capture group per rule, longest first; any run of spaces in a
        // rule matches any run of spaces in the text.
        let alternatives = rules.map { rule in
            "(" + rule.spoken.split(separator: " ")
                .map { NSRegularExpression.escapedPattern(for: String($0)) }
                .joined(separator: #"\s+"#) + ")"
        }
        let pattern = #"(?i)(?<![\p{L}\p{N}])(?:"# + alternatives.joined(separator: "|") + #")(?![\p{L}\p{N}])"#
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return text }

        let ns = text as NSString
        var result = ""
        var cursor = 0
        for match in regex.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            guard let index = (1...rules.count).first(where: { match.range(at: $0).location != NSNotFound })
            else { continue }
            result += ns.substring(with: NSRange(location: cursor, length: match.range.location - cursor))
            result += rules[index - 1].written
            cursor = match.range.location + match.range.length
        }
        return result + ns.substring(from: cursor)
    }
}
