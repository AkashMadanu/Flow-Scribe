using SherpaOnnx;

namespace FlowScribe.Core;

/// <summary>
/// Parakeet TDT 0.6B v2 on the CPU through sherpa-onnx. It outputs
/// punctuation and capitals itself. Thread safe: one transcription at a time.
/// </summary>
public sealed class Transcriber : IDisposable
{
    public const int SampleRate = 16_000;

    /// Silence appended after the recording. On the Mac's Parakeet model a
    /// recording that stops mid-word lost that word, and 300 ms of silence
    /// fixed it; the Speech tests check the same holds for this model.
    static readonly float[] TrailingSilence = new float[SampleRate * 300 / 1000];

    readonly OfflineRecognizer _recognizer;
    readonly object _lock = new();

    public Transcriber(string modelDirectory)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDirectory, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDirectory, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDirectory, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelDirectory, "tokens.txt");
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.Provider = "cpu";
        // Half the cores: fast, while leaving the machine responsive.
        config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        config.ModelConfig.Debug = 0;
        config.DecodingMethod = "greedy_search";
        _recognizer = new OfflineRecognizer(config);
    }

    /// 16 kHz mono samples in, text out.
    public string Transcribe(float[] samples)
    {
        var padded = new float[samples.Length + TrailingSilence.Length];
        samples.CopyTo(padded, 0);
        lock (_lock)
        {
            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(SampleRate, padded);
            _recognizer.Decode(stream);
            return stream.Result.Text.Trim();
        }
    }

    public void Dispose() => _recognizer.Dispose();
}
