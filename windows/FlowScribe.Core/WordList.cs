using System.Text;
using System.Text.RegularExpressions;

namespace FlowScribe.Core;

/// <summary>
/// The user's own fixes for words the model mishears, kept in a plain text
/// file with one rule per line: <c>Jim Ledger = Gym Ledger</c>.
/// Matching ignores case and only replaces whole words, so a rule for "cloud"
/// never touches "clouds". Longer phrases win over shorter ones.
/// </summary>
public sealed class WordList
{
    public sealed record Rule(string Spoken, string Written);

    public IReadOnlyList<Rule> Rules { get; }

    public const string Template = """
        # FlowScribe word list. One rule per line: what the model types = what you want.
        # Matching ignores case and only replaces whole words. Saved changes apply
        # to your next dictation. Lines starting with # are ignored.
        #
        # Jim Ledger = Gym Ledger

        """;

    public WordList(string contents)
    {
        var rules = new List<Rule>();
        foreach (var line in contents.Split('\n', '\r'))
        {
            var trimmed = line.Trim();
            var equals = trimmed.IndexOf('=');
            if (trimmed.StartsWith('#') || equals < 0) continue;
            var spoken = trimmed[..equals].Trim();
            var written = trimmed[(equals + 1)..].Trim();
            if (spoken.Length == 0 || written.Length == 0) continue;
            rules.Add(new Rule(spoken, written));
        }
        Rules = rules.OrderByDescending(r => r.Spoken.Length).ToList();
    }

    /// A missing or unreadable file is an empty list, never an error.
    public static WordList Load(string path)
    {
        try { return new WordList(File.ReadAllText(path)); }
        catch (Exception) { return new WordList(""); }
    }

    public static string DefaultPath() => Path.Combine(AppPaths.DataDirectory, "words.txt");

    /// One pass over the text, so a replacement is never rewritten by another rule.
    public string Apply(string text)
    {
        if (Rules.Count == 0) return text;
        // One capture group per rule, longest first; any run of spaces in a
        // rule matches any run of spaces in the text.
        var alternatives = Rules.Select(rule =>
            "(" + string.Join(@"\s+", rule.Spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + ")");
        var regex = new Regex(@"(?i)(?<![\p{L}\p{N}])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}])");

        var result = new StringBuilder();
        var cursor = 0;
        foreach (Match match in regex.Matches(text))
        {
            var index = Enumerable.Range(1, Rules.Count).FirstOrDefault(i => match.Groups[i].Success);
            if (index == 0) continue;
            result.Append(text, cursor, match.Index - cursor);
            result.Append(Rules[index - 1].Written);
            cursor = match.Index + match.Length;
        }
        return result.Append(text, cursor, text.Length - cursor).ToString();
    }
}
