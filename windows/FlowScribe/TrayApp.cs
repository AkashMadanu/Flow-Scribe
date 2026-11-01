using System.Diagnostics;
using System.Media;
using System.Reflection;
using FlowScribe.Core;

namespace FlowScribe;

/// <summary>
/// The whole app: a tray icon with a menu, the Right Alt hotkey, recording,
/// transcription and pasting. The Windows counterpart of the macOS AppDelegate.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    enum Status { Loading, Ready, Recording, Transcribing, Failed }

    readonly Control _ui = new();  // marshals work back to the UI thread
    readonly NotifyIcon _tray = new();
    readonly Hud _hud = new();
    readonly AudioRecorder _recorder = new();
    readonly HistoryStore _history = new(HistoryStore.DefaultPath());
    readonly Settings _settings = Settings.Load();
    readonly HotkeyStateMachine _keys;
    readonly KeyboardHook _hook;
    Transcriber? _transcriber;
    Status _status = Status.Loading;
    string _statusDetail = "";

    /// Dictations finish in the order they were spoken.
    Task _pipeline = Task.CompletedTask;
    (IntPtr Window, char End, DateTime At)? _lastInsert;
    /// A released dictation still recording its short tail.
    (DateTime ReleasedAt, IntPtr Window, bool StoppedAtLimit, System.Windows.Forms.Timer Timer)? _pendingFinish;

    /// Recordings shorter than this are treated as accidental.
    static readonly int MinimumSamples = AudioRecorder.SampleRate / 4;

    public TrayApp()
    {
        _ui.CreateControl();
        _ = _ui.Handle;

        using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("FlowScribe.AppIcon.ico"))
            _tray.Icon = icon != null ? new Icon(icon) : SystemIcons.Application;
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => FillMenu(_tray.ContextMenuStrip);
        // Left click opens the same menu as right click.
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_tray, null);
        };
        _tray.Visible = true;
        UpdateTray();

        _keys = new HotkeyStateMachine(Schedule);
        _keys.Fired += OnHotkey;
        _hook = new KeyboardHook(_ui);
        _hook.RightAltDown += _keys.KeyDown;
        _hook.RightAltUp += _keys.KeyUp;
        _hook.OtherKeyDown += _keys.OtherKeyDown;
        _recorder.LimitReached += () => _ui.BeginInvoke(RecordingLimitReached);

        _settings.RegisterStartupOnce();
        _ = LoadModelAsync();
    }

    IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = (int)delay.TotalMilliseconds };
        timer.Tick += (_, _) => { timer.Dispose(); action(); };
        timer.Start();
        return timer;
    }

    // MARK: Model

    async Task LoadModelAsync()
    {
        SetStatus(Status.Loading);
        try
        {
            var dir = SpeechModel.DefaultDirectory;
            if (!SpeechModel.IsComplete(dir))
            {
                var progress = new Progress<double>(p => SetStatus(Status.Loading, $"{p:P0}"));
                await SpeechModel.DownloadAsync(dir, progress);
            }
            _transcriber = await Task.Run(() =>
            {
                var t = new Transcriber(dir);
                t.Transcribe(new float[AudioRecorder.SampleRate]);  // warm up so the first dictation is fast
                return t;
            });
            SetStatus(Status.Ready);
        }
        catch (Exception e)
        {
            SetStatus(Status.Failed, $"Speech model failed to load: {e.Message}");
        }
    }

    // MARK: Dictation

    void OnHotkey(HotkeyEvent e)
    {
        switch (e)
        {
            case HotkeyEvent.Start: StartRecording(); break;
            case HotkeyEvent.Finish: FinishRecording(); break;
            case HotkeyEvent.Cancel: CancelRecording(); break;
        }
    }

    void StartRecording()
    {
        CompleteRecording();
        if (_recorder.IsRecording) return;
        if (_status == Status.Failed)
        {
            Fail(_statusDetail);
            _keys.Reset();
            return;
        }
        try
        {
            _recorder.Start(_settings.Microphone);
            SetStatus(Status.Recording);
            Sounds.Start();
            _hud.ShowMessage("Listening", sticky: true);
        }
        catch (Exception e)
        {
            Fail($"Microphone error: {e.Message}. Check Settings > Privacy & security > Microphone.");
            _keys.Reset();
        }
    }

    void CancelRecording()
    {
        if (!_recorder.IsRecording) return;
        _pendingFinish?.Timer.Dispose();
        _pendingFinish = null;
        _recorder.Stop();
        SetStatus(ReadyStatus());
        _hud.HidePill();
    }

    /// The recording hit the 10-minute limit: stop instead of quietly dropping
    /// what comes after, and type everything up to that point.
    void RecordingLimitReached()
    {
        if (!_recorder.IsRecording || _pendingFinish != null) return;
        _keys.Reset();
        Sounds.Error();
        FinishRecording(stoppedAtLimit: true);
    }

    void FinishRecording() => FinishRecording(stoppedAtLimit: false);

    void FinishRecording(bool stoppedAtLimit)
    {
        if (!_recorder.IsRecording || _pendingFinish != null) return;
        // Keep listening briefly: people often release the key while still
        // saying the last word, which otherwise gets clipped.
        var timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) => CompleteRecording();
        _pendingFinish = (DateTime.UtcNow, NativeMethods.GetForegroundWindow(), stoppedAtLimit, timer);
        timer.Start();
    }

    /// Stops the microphone after the tail and queues the audio. Also called
    /// early when a new dictation starts during the tail, so none is lost.
    void CompleteRecording()
    {
        if (_pendingFinish is not { } pending) return;
        _pendingFinish = null;
        pending.Timer.Dispose();
        var samples = _recorder.Stop();
        if (samples.Length < MinimumSamples)
        {
            SetStatus(ReadyStatus());
            _hud.HidePill();
            return;
        }
        Sounds.Stop();
        var previous = _pipeline;
        _pipeline = ProcessAsync(previous, samples, pending.Window, pending.ReleasedAt, pending.StoppedAtLimit);
    }

    async Task ProcessAsync(Task previous, float[] samples, IntPtr window, DateTime releasedAt, bool stoppedAtLimit)
    {
        await previous;
        // Wait for the model if the user dictates during the first download.
        if (_transcriber == null)
        {
            _hud.ShowMessage("Getting the speech model ready", sticky: true);
            while (_transcriber == null)
            {
                if (_status == Status.Failed) { Fail(_statusDetail); return; }
                await Task.Delay(200);
            }
        }
        SetStatus(Status.Transcribing);
        _hud.ShowMessage("Transcribing", sticky: true);

        string raw;
        var watch = Stopwatch.StartNew();
        try
        {
            var transcriber = _transcriber;
            raw = await Task.Run(() => transcriber.Transcribe(samples));
        }
        catch (Exception e)
        {
            SetStatus(Status.Ready);
            Fail($"Transcription failed: {e.Message}");
            return;
        }
        var modelMs = watch.ElapsedMilliseconds;
        SetStatus(_recorder.IsRecording ? Status.Recording : Status.Ready);

        // Read each time (a few bytes) so edits apply to the next dictation.
        var text = WordList.Load(WordList.DefaultPath()).Apply(TextCleaner.Clean(raw));
        if (text.Length == 0)
        {
            Sounds.Error();
            _hud.ShowMessage("No speech heard. Check your microphone input.");
            return;
        }

        // Save first, so the text is never lost whatever happens next.
        try { _history.Add(text); } catch (Exception e) { Log.Write($"history save failed: {e.Message}"); }

        if (_lastInsert is { } last && last.Window == window && DateTime.UtcNow - last.At < TimeSpan.FromMinutes(2))
            text = TextCleaner.JoinPrefix(last.End, text) + text;

        var outcome = await TextInserter.InsertAsync(text);
        Log.Write($"{samples.Length / (double)AudioRecorder.SampleRate:F1} s audio, model {modelMs} ms, " +
            $"release to paste {(DateTime.UtcNow - releasedAt).TotalMilliseconds:F0} ms, {MemoryMegabytes()} MB, {outcome}");

        switch (outcome)
        {
            case TextInserter.Outcome.Pasted:
                if (stoppedAtLimit)
                    _hud.ShowMessage($"Stopped at the {AudioRecorder.MaxDuration.TotalMinutes:F0}-minute limit. Everything before it was typed.", seconds: 6);
                else
                    _hud.HidePill();
                _lastInsert = (window, text[^1], DateTime.UtcNow);
                break;
            case TextInserter.Outcome.CopiedElevatedWindow:
                Sounds.Error();
                _hud.ShowMessage("Copied. That window runs as administrator, so press Ctrl+V.", seconds: 5);
                break;
            case TextInserter.Outcome.CopiedPasteFailed:
                Sounds.Error();
                _hud.ShowMessage("Copied, but Windows blocked typing. Press Ctrl+V.", seconds: 5);
                break;
            case TextInserter.Outcome.ClipboardBusy:
                Sounds.Error();
                _hud.ShowMessage("Another app is holding the clipboard. Your text is in the FlowScribe menu.", seconds: 6);
                break;
        }
    }

    Status ReadyStatus() => _status is Status.Failed or Status.Loading ? _status : Status.Ready;

    void Fail(string message)
    {
        Sounds.Error();
        _hud.ShowMessage(message, seconds: 6);
    }

    // MARK: Tray

    void SetStatus(Status status, string detail = "")
    {
        _status = status;
        _statusDetail = detail;
        UpdateTray();
    }

    string StatusText => _status switch
    {
        Status.Loading => _statusDetail.Length > 0
            ? $"Downloading speech model, {_statusDetail} (first launch only)"
            : "Loading speech model",
        Status.Ready => "Hold Right Alt to dictate. Double tap for hands free.",
        Status.Recording => "Listening",
        Status.Transcribing => "Transcribing",
        _ => _statusDetail,
    };

    void UpdateTray()
    {
        var text = "FlowScribe: " + StatusText;
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    void FillMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem(StatusText) { Enabled = false });
        if (_status == Status.Failed)
            menu.Items.Add("Try loading the model again", null, (_, _) => _ = LoadModelAsync());

        menu.Items.Add(new ToolStripSeparator());
        var notes = _history.Notes;
        menu.Items.Add(new ToolStripMenuItem(notes.Count == 0 ? "No transcriptions yet" : "Last 5 (click to copy)") { Enabled = false });
        foreach (var note in notes)
        {
            var title = note.Text.Replace("\n", " ");
            var item = new ToolStripMenuItem(title.Length > 70 ? title[..70] + "…" : title) { ToolTipText = note.Text };
            item.Click += async (_, _) => await TextInserter.CopyAsync(note.Text);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MicrophoneMenu());
        menu.Items.Add("Edit word list…", null, (_, _) => EditWordList());
        menu.Items.Add(new ToolStripMenuItem($"Memory in use: {MemoryMegabytes()} MB") { Enabled = false });
        menu.Items.Add("Quit FlowScribe", null, (_, _) => Quit());
    }

    ToolStripMenuItem MicrophoneMenu()
    {
        var devices = AudioRecorder.DeviceNames();
        var root = new ToolStripMenuItem("Microphone");
        // A chosen microphone that is unplugged falls back to the default, so show that.
        var usingDefault = _settings.Microphone == null || !devices.Contains(_settings.Microphone);
        var systemDefault = new ToolStripMenuItem("System default") { Checked = usingDefault };
        systemDefault.Click += (_, _) => { _settings.Microphone = null; _settings.Save(); };
        root.DropDownItems.Add(systemDefault);
        root.DropDownItems.Add(new ToolStripSeparator());
        foreach (var name in devices)
        {
            var item = new ToolStripMenuItem(name) { Checked = !usingDefault && name == _settings.Microphone };
            item.Click += (_, _) => { _settings.Microphone = name; _settings.Save(); };
            root.DropDownItems.Add(item);
        }
        return root;
    }

    static void EditWordList()
    {
        var path = WordList.DefaultPath();
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, WordList.Template.Replace("\n", "\r\n"));
        }
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    /// The process's working set: its memory currently in RAM. Task Manager's
    /// "Memory" column shows the private part of this, so it can read lower.
    static long MemoryMegabytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64 >> 20;
    }

    void Quit()
    {
        _hook.Dispose();
        _recorder.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _transcriber?.Dispose();
        ExitThread();
    }
}

/// Short sounds for start, stop and problems: Windows' own speech sounds when
/// present, otherwise standard system sounds.
static class Sounds
{
    static readonly string Media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    public static void Start() => Play("Speech On.wav", SystemSounds.Asterisk);
    public static void Stop() => Play("Speech Off.wav", SystemSounds.Asterisk);
    public static void Error() => Play("Windows Foreground.wav", SystemSounds.Hand);

    static void Play(string file, SystemSound fallback)
    {
        var path = Path.Combine(Media, file);
        try
        {
            if (File.Exists(path)) new SoundPlayer(path).Play();
            else fallback.Play();
        }
        catch (Exception) { fallback.Play(); }
    }
}

/// One line per dictation (timings only, never the text) in
/// %APPDATA%\FlowScribe\log.txt, kept under about 100 KB.
static class Log
{
    static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "log.txt");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            if (new FileInfo(FilePath) is { Exists: true, Length: > 100_000 }) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\r\n");
        }
        catch (Exception) { }
    }
}
