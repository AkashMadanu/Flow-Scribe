using System.Diagnostics;
using FlowScribe.Core;
using Xunit.Abstractions;

namespace FlowScribe.Core.Tests;

/// <summary>
/// Runs the real speech model on short recorded clips. Needs the model
/// (about 660 MB), so it only runs when asked:
///   FLOWSCRIBE_MODEL_DIR=&lt;dir&gt; dotnet test --filter Category=Speech
/// With the filter but no model directory, the tests fail rather than pass silently.
/// </summary>
[Trait("Category", "Speech")]
public sealed class SpeechTests(ITestOutputHelper output)
{
    static readonly Lazy<Transcriber> Model = new(() =>
    {
        var dir = Environment.GetEnvironmentVariable("FLOWSCRIBE_MODEL_DIR")
            ?? throw new InvalidOperationException("Set FLOWSCRIBE_MODEL_DIR to the model directory.");
        if (!SpeechModel.IsComplete(dir)) SpeechModel.DownloadAsync(dir).GetAwaiter().GetResult();
        return new Transcriber(dir);
    });

    static string Clip(string name) => Path.Combine(AppContext.BaseDirectory, "Audio", name);

    // Clips were made with macOS `say` at 16 kHz; trailing silence is trimmed
    // so each one ends exactly where the last word ends.
    [Theory]
    [InlineData("hope.wav", "I hope this is a good test")]
    [InlineData("browser.wav", "Just checking if it is working in the browser")]
    [InlineData("report.wav", "Please send the report to Sam tomorrow")]
    public void TranscribesWithPunctuationAndCapitals(string clip, string words)
    {
        var samples = Wav.ReadMono16k(Clip(clip));
        var watch = Stopwatch.StartNew();
        var text = Model.Value.Transcribe(samples);
        watch.Stop();
        output.WriteLine($"{clip}: \"{text}\" in {watch.ElapsedMilliseconds} ms " +
            $"({samples.Length / 16_000.0:F2} s audio), working set {Process.GetCurrentProcess().WorkingSet64 >> 20} MB");
        Assert.StartsWith(words, text.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        Assert.True(char.IsUpper(text[0]));
    }

    // The Mac fix: a recording that stops mid-word keeps that word because the
    // transcriber pads 300 ms of silence. Cut 120 ms into the last word.
    [Theory]
    [InlineData("hope.wav", "test")]
    [InlineData("browser.wav", "browser")]
    public void LastWordSurvivesAudioEndingMidWord(string clip, string lastWord)
    {
        var samples = Wav.ReadMono16k(Clip(clip));
        var cut = samples[..^(16_000 * 120 / 1000)];
        var text = Model.Value.Transcribe(cut);
        output.WriteLine($"{clip} cut 120 ms: \"{text}\"");
        Assert.EndsWith(lastWord, text.TrimEnd('.', '?', '!'), StringComparison.OrdinalIgnoreCase);
    }
}

static class Wav
{
    /// Reads a 16 kHz mono WAV of 16-bit PCM or 32-bit float samples, with
    /// trailing near-silence trimmed so it ends where the speech ends.
    public static float[] ReadMono16k(string path)
    {
        var samples = ReadSamples(path);
        var end = samples.Length;
        while (end > 0 && Math.Abs(samples[end - 1]) < 0.01f) end--;
        return samples[..end];
    }

    static float[] ReadSamples(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12);  // RIFF header
        short format = 0, bits = 0;
        while (true)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                format = reader.ReadInt16();
                var channels = reader.ReadInt16();
                var rate = reader.ReadInt32();
                reader.ReadBytes(6);
                bits = reader.ReadInt16();
                reader.ReadBytes(size - 16);
                if (channels != 1 || rate != 16_000) throw new InvalidDataException("Expected 16 kHz mono");
            }
            else if (id == "data")
            {
                var bytes = reader.ReadBytes(size);
                if (format == 3 && bits == 32)
                    return Enumerable.Range(0, bytes.Length / 4).Select(i => BitConverter.ToSingle(bytes, i * 4)).ToArray();
                if (format == 1 && bits == 16)
                    return Enumerable.Range(0, bytes.Length / 2).Select(i => BitConverter.ToInt16(bytes, i * 2) / 32768f).ToArray();
                throw new InvalidDataException($"Unsupported WAV format {format}/{bits}");
            }
            else
            {
                reader.ReadBytes(size);
            }
        }
    }
}
