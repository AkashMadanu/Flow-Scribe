using NAudio.Wave;

namespace FlowScribe;

/// <summary>
/// Records the chosen microphone as 16 kHz mono float samples, the format the
/// speech model expects. The microphone is only open while recording.
/// </summary>
sealed class AudioRecorder
{
    public const int SampleRate = 16_000;
    /// Hard stop so a forgotten hands-free recording cannot grow forever.
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    /// Raised once, on a background thread, when a recording reaches MaxDuration.
    public event Action? LimitReached;

    readonly object _lock = new();
    readonly List<float> _samples = [];
    WaveInEvent? _wave;
    ManualResetEventSlim? _stopped;
    bool _limitReported;

    public bool IsRecording => _wave != null;

    /// Input devices as Windows lists them, index = device number.
    public static List<string> DeviceNames() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToList();

    /// <param name="deviceName">A name from DeviceNames(); null, or a device that
    /// is no longer connected, uses the system default.</param>
    public void Start(string? deviceName)
    {
        Stop();
        lock (_lock) { _samples.Clear(); _limitReported = false; }

        var device = deviceName == null ? -1 : DeviceNames().IndexOf(deviceName);
        // WaveInEvent posts RecordingStopped to the SynchronizationContext it was
        // created on; create it without one so Stop() can wait for it on the UI
        // thread without deadlocking.
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        WaveInEvent wave;
        try
        {
            wave = new WaveInEvent
            {
                DeviceNumber = device,  // -1 is WAVE_MAPPER, the system default
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 50,
            };
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }

        var stopped = new ManualResetEventSlim();
        var maxSamples = (int)(SampleRate * MaxDuration.TotalSeconds);
        wave.DataAvailable += (_, e) =>
        {
            var reachedLimit = false;
            lock (_lock)
            {
                if (_samples.Count < maxSamples)
                {
                    for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
                        _samples.Add(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                }
                else if (!_limitReported)
                {
                    _limitReported = true;
                    reachedLimit = true;
                }
            }
            if (reachedLimit) LimitReached?.Invoke();
        };
        wave.RecordingStopped += (_, _) => stopped.Set();
        wave.StartRecording();
        _wave = wave;
        _stopped = stopped;
    }

    /// Stops recording and returns everything captured.
    public float[] Stop()
    {
        if (_wave is { } wave)
        {
            wave.StopRecording();
            _stopped?.Wait(TimeSpan.FromMilliseconds(500));  // let the last buffer arrive
            wave.Dispose();
            _wave = null;
        }
        lock (_lock)
        {
            var captured = _samples.ToArray();
            _samples.Clear();
            return captured;
        }
    }
}
