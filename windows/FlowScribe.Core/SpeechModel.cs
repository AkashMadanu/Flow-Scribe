namespace FlowScribe.Core;

/// <summary>
/// NVIDIA Parakeet TDT 0.6B v2 (English, punctuation and capitals), int8,
/// converted for sherpa-onnx. Downloaded once from a pinned Hugging Face
/// commit, so the files can never change under the app.
/// </summary>
public static class SpeechModel
{
    const string Repo = "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
    const string Revision = "1ab9323565ddb038682214b292f588070a538ce2";

    /// File name and exact size; a file with any other size is incomplete.
    public static readonly (string Name, long Size)[] Files =
    [
        ("encoder.int8.onnx", 652_184_296),
        ("decoder.int8.onnx", 7_257_753),
        ("joiner.int8.onnx", 1_739_080),
        ("tokens.txt", 9_384),
    ];

    public static string DefaultDirectory => Path.Combine(AppPaths.ModelsDirectory, "parakeet-tdt-0.6b-v2-int8");

    public static long TotalBytes => Files.Sum(f => f.Size);

    public static bool IsComplete(string directory) =>
        Files.All(f => new FileInfo(Path.Combine(directory, f.Name)) is { Exists: true } info && info.Length == f.Size);

    /// Downloads whatever is missing or incomplete. Each file is written to
    /// a .part file and only renamed once it has the exact expected size, so an
    /// interrupted download is never mistaken for a finished one.
    public static async Task DownloadAsync(string directory, IProgress<double>? progress = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(directory);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        long done = 0;
        foreach (var (name, size) in Files)
        {
            var target = Path.Combine(directory, name);
            if (new FileInfo(target) is { Exists: true } existing && existing.Length == size)
            {
                done += size;
                continue;
            }
            var part = target + ".part";
            var url = $"https://huggingface.co/{Repo}/resolve/{Revision}/{name}";
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var file = File.Create(part);
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    progress?.Report((double)done / TotalBytes);
                }
            }
            if (new FileInfo(part).Length != size)
                throw new IOException($"{name} downloaded incompletely; it will be retried next time.");
            File.Move(part, target, overwrite: true);
        }
    }
}
