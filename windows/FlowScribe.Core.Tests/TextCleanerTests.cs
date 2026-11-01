using FlowScribe.Core;

namespace FlowScribe.Core.Tests;

// The same cases as Tests/FlowScribeCoreTests/TextCleanerTests.swift, so both
// apps clean text identically.
public class TextCleanerTests
{
    [Fact]
    public void EmptyAndWhitespace()
    {
        Assert.Equal("", TextCleaner.Clean(""));
        Assert.Equal("", TextCleaner.Clean("   \n "));
    }

    [Fact]
    public void OnlyFillersBecomesEmpty()
    {
        Assert.Equal("", TextCleaner.Clean("Um."));
        Assert.Equal("", TextCleaner.Clean("Um, uh"));
    }

    [Fact]
    public void KeepsModelPunctuationAndQuestions() =>
        Assert.Equal("Can you send it today? Thanks.", TextCleaner.Clean("Can you send it today? Thanks."));

    [Fact]
    public void RemovesFillersWithTheirCommas()
    {
        Assert.Equal("I think we should go.", TextCleaner.Clean("I think, um, we should go."));
        Assert.Equal("I think so.", TextCleaner.Clean("Um, I think so."));
        Assert.Equal("So. Next point.", TextCleaner.Clean("So, uh. Next point."));
        Assert.Equal("We went home.", TextCleaner.Clean("We went uh home."));
    }

    [Fact]
    public void DoesNotTouchWordsContainingFillers()
    {
        Assert.Equal("The umbrella is under the hummus.", TextCleaner.Clean("The umbrella is under the hummus."));
        Assert.Equal("Ah, I see.", TextCleaner.Clean("Ah, I see."));
    }

    [Fact]
    public void NewLineAndParagraph()
    {
        Assert.Equal("Hello.\n\nNext part.", TextCleaner.Clean("Hello. New paragraph. Next part."));
        Assert.Equal("First item\nSecond item", TextCleaner.Clean("First item, new line second item"));
        Assert.Equal("Dear Sam\nThanks.", TextCleaner.Clean("Dear Sam, new line thanks."));
    }

    [Fact]
    public void DoesNotRecapitaliseInsideSentences() =>
        Assert.Equal("Use e.g. this one, version 3.5 works.", TextCleaner.Clean("Use e.g. this one, version 3.5 works."));

    [Fact]
    public void CollapsesSpacesAndSpaceBeforePunctuation() =>
        Assert.Equal("Hello there, friend.", TextCleaner.Clean("Hello   there , friend ."));

    [Fact]
    public void NumberedListGoesOnSeparateLines()
    {
        Assert.Equal(
            "My tasks.\n1. Buy milk.\n2. Call mom.\n3. Gym.",
            TextCleaner.Clean("My tasks. Number one, buy milk. Number two, call mom. Number three, gym."));
        Assert.Equal("1. Eggs\n2. Bread.", TextCleaner.Clean("Number 1 eggs, number 2 bread."));
    }

    [Fact]
    public void SentenceAfterLastItemStartsNewParagraph() =>
        Assert.Equal(
            "1. Finish it.\n2. Push the code.\n\nSo I think this is working good.",
            TextCleaner.Clean("Number one, finish it. Number two, push the code. So I think this is working good."));

    [Fact]
    public void NumberOneAloneIsNotAList()
    {
        Assert.Equal("This is my number one priority.", TextCleaner.Clean("This is my number one priority."));
        Assert.Equal("Call number two first.", TextCleaner.Clean("Call number two first."));
    }

    [Fact]
    public void SpokenEmailAddress()
    {
        Assert.Equal(
            "My email is sam.lee777@gmail.com, thanks.",
            TextCleaner.Clean("My email is sam.lee777 at gmail.com, thanks."));
        Assert.Equal("Write to sam.lee@outlook.com", TextCleaner.Clean("Write to sam.lee at outlook dot com"));
    }

    [Fact]
    public void AtBeforeOrdinaryWebsiteIsLeftAlone() =>
        Assert.Equal("Look at example.com today.", TextCleaner.Clean("Look at example.com today."));

    [Fact]
    public void JoinPrefix()
    {
        Assert.Equal("", TextCleaner.JoinPrefix(null, "Hi"));
        Assert.Equal(" ", TextCleaner.JoinPrefix('.', "Hi"));
        Assert.Equal("", TextCleaner.JoinPrefix(' ', "Hi"));
        Assert.Equal("", TextCleaner.JoinPrefix('\n', "Hi"));
        Assert.Equal("", TextCleaner.JoinPrefix('o', ", then"));
    }
}
