using FlowScribe.Core;

namespace FlowScribe.Core.Tests;

// The same cases as Tests/FlowScribeCoreTests/WordListTests.swift.
public class WordListTests
{
    [Fact]
    public void ParsesRulesAndSkipsCommentsAndBadLines()
    {
        var list = new WordList("# comment\n\nJim Ledger = GymLedger\nno equals sign\n = empty\nCloud Code=Claude Code\n");
        Assert.Equal(2, list.Rules.Count);
        Assert.Contains(new WordList.Rule("Jim Ledger", "GymLedger"), list.Rules);
        Assert.Contains(new WordList.Rule("Cloud Code", "Claude Code"), list.Rules);
    }

    [Fact]
    public void TemplateHasNoActiveRules() => Assert.Empty(new WordList(WordList.Template).Rules);

    [Fact]
    public void ReplacesWholeWordsIgnoringCase()
    {
        var list = new WordList("Jim Ledger = GymLedger\nCloud Code = Claude Code");
        Assert.Equal("I built GymLedger with Claude Code.", list.Apply("I built jim ledger with Cloud Code."));
        Assert.Equal("GymLedger is live", list.Apply("Jim   Ledger is live"));
    }

    [Fact]
    public void DoesNotReplaceInsideOtherWords() =>
        Assert.Equal("Clouds over the Claude.", new WordList("cloud = Claude").Apply("Clouds over the cloud."));

    [Fact]
    public void LongerPhraseWins() =>
        Assert.Equal(
            "Cloud storage and Claude",
            new WordList("Cloud = Claude\nCloud storage = Cloud storage").Apply("Cloud storage and Cloud"));

    [Fact]
    public void WrittenTextIsLiteral() =>
        Assert.Equal("The $1 (approx).", new WordList("price = $1 (approx)").Apply("The price."));

    [Fact]
    public void MissingFileIsEmpty() =>
        Assert.Empty(WordList.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())).Rules);
}
