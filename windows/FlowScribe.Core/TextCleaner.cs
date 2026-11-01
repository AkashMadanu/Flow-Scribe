using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FlowScribe.Core;

/// <summary>
/// Tidies raw speech recognizer output before it is typed. A direct port of
/// the macOS TextCleaner, checked by the same test cases.
///
/// The model already adds punctuation, question marks and capitals, so this
/// deliberately does little: it removes hesitation sounds, turns a spoken
/// "new line" / "new paragraph" into a real line break, puts a spoken
/// "number one, number two" list on separate lines, joins spoken email
/// addresses, and fixes spacing and the capital letter after a break.
/// It never re-capitalises the model's own sentences.
/// </summary>
public static class TextCleaner
{
    /// Hesitation sounds that are not real words in English dictation.
    /// "like", "ah" and "you know" are left alone: they are often meant.
    static readonly string[] Fillers = ["um", "umm", "uh", "uhh", "uhm", "erm", "er", "hmm", "mm", "mhm"];

    static readonly string[] NumberWords = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

    public static string Clean(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0) return "";

        // 1. Fillers, with the comma before them and a comma right after them:
        //    "I think, um, we should" -> "I think we should".
        //    A full stop after a filler is kept: "So, uh. Next" -> "So. Next".
        text = Replace(text, @"(?i)(?:,\s*)?\b(?:" + string.Join("|", Fillers) + @")\b,?", "");

        // 2. Spoken layout commands. A comma before the command is dropped;
        //    a full stop before it is kept, the punctuation after it is dropped.
        text = Replace(text, @"(?i),?\s*\bnew paragraph\b[,.:]?\s*", "\n\n");
        text = Replace(text, @"(?i),?\s*\bnew line\b[,.:]?\s*", "\n");
        text = NumberedList(text);

        // Spoken email addresses: "sam at gmail.com" / "sam at gmail dot com".
        // Only well-known mail domains, so "look at example.com" is left alone.
        text = Replace(
            text,
            @"(?i)\b([a-z0-9._%+-]+) at (gmail|googlemail|yahoo|outlook|hotmail|icloud|live|proton|protonmail|aol|zoho)(?:\.| dot )(com|in|me|net|co\.uk|co\.in)\b",
            "$1@$2.$3");

        // 3. Whitespace: single spaces, none before punctuation, none hugging
        //    line breaks, at most one blank line, nothing dangling at the start.
        text = Replace(text, @"[ \t]+", " ");
        text = Replace(text, @" +([,.!?;:])", "$1");
        text = Replace(text, @" *\n *", "\n");
        text = Replace(text, @"\n{3,}", "\n\n");
        text = Replace(text, @"^[,.;:\s]+", "");
        text = Replace(text, @"(?m)^[,;:] *", "");
        text = text.Trim();
        // Nothing but punctuation left (the user only said "um."): nothing to type.
        if (!text.Any(char.IsLetterOrDigit)) return "";

        // 4. Capital letter at the very start and after each inserted break.
        return CapitaliseLineStarts(text);
    }

    /// Decides whether a new dictation needs a leading space so two dictations
    /// in a row do not run together ("Hello.World").
    public static string JoinPrefix(char? previousEnd, string next)
    {
        if (previousEnd is not char end || next.Length == 0) return "";
        var first = next[0];
        if (char.IsWhiteSpace(end) || char.IsWhiteSpace(first)) return "";
        if (",.!?;:".Contains(first)) return "";
        return " ";
    }

    /// "Number one, milk. Number two, eggs." becomes one item per line:
    /// "1. Milk.\n2. Eggs." Only a run that starts at one and reaches two is
    /// treated as a list, so "my number one priority" stays as spoken.
    static string NumberedList(string text)
    {
        var regex = new Regex(@"(?i),?\s*\bnumber\s+(" + string.Join("|", NumberWords) + @"|10|[1-9])\b[,.:]?\s*");
        var expected = 1;
        var items = new List<(int Index, int Length, int Number)>();
        foreach (Match match in regex.Matches(text))
        {
            var word = match.Groups[1].Value.ToLowerInvariant();
            var number = int.TryParse(word, out var n) ? n : Array.IndexOf(NumberWords, word) + 1;
            if (number == expected)
            {
                items.Add((match.Index, match.Length, number));
                expected++;
            }
        }
        if (items.Count < 2) return text;

        var result = new StringBuilder();
        var cursor = 0;
        foreach (var item in items)
        {
            result.Append(text, cursor, item.Index - cursor);
            result.Append('\n').Append(item.Number).Append(". ");
            cursor = item.Index + item.Length;
            // Each item starts with a capital letter.
            if (cursor < text.Length && char.IsLetter(text[cursor]))
            {
                result.Append(char.ToUpper(text[cursor], CultureInfo.InvariantCulture));
                cursor++;
            }
        }
        // The list ends with the last item's first sentence; what follows
        // starts a new paragraph instead of running on in that item.
        var tail = text[cursor..];
        result.Append(new Regex(@"([.!?])\s+(?=\p{Lu})").Replace(tail, "$1\n\n", 1));
        return result.ToString();
    }

    internal static string CapitaliseLineStarts(string text)
    {
        var result = new StringBuilder(text.Length);
        var atLineStart = true;
        foreach (var ch in text)
        {
            if (atLineStart && char.IsLetter(ch))
            {
                result.Append(char.ToUpper(ch, CultureInfo.InvariantCulture));
                atLineStart = false;
                continue;
            }
            result.Append(ch);
            if (ch == '\n')
                atLineStart = true;
            else if (!char.IsWhiteSpace(ch) && !"\"'(".Contains(ch))
                atLineStart = false;
        }
        return result.ToString();
    }

    static string Replace(string text, string pattern, string replacement) =>
        Regex.Replace(text, pattern, replacement);
}
