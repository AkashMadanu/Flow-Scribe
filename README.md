# FlowScribe

Free, private, on-device dictation for Apple Silicon Macs. One job: hold a key,
speak, let go, and the text appears where your cursor is. Nothing you say leaves
your Mac.

## Download and install (macOS 14+, Apple Silicon M1 or newer)

1. Download **FlowScribe-…-macOS-AppleSilicon.zip** from the
   [latest release](https://github.com/AkashMadanu/Flow-Scribe/releases/latest)
   and double-click it.
2. Drag **FlowScribe.app** into your **Applications** folder.
3. Open it. macOS says it "could not verify" FlowScribe, because the app is not
   notarized by Apple. Click **Done**, then open **System Settings > Privacy &
   Security**, scroll down and click **Open Anyway** next to FlowScribe.
   You only do this once.
4. Allow **Microphone** when asked.
5. Allow **Accessibility**: System Settings > Privacy & Security > Accessibility,
   turn on FlowScribe. This is what lets it type into other apps.
6. Wait for the one-time speech model download (about 600 MB). The menu bar
   icon shows a download arrow until it is ready.

FlowScribe then starts by itself when you log in. You can turn that off in
System Settings > General > Login Items.

## Use it

| Action | What happens |
|---|---|
| Hold **Right Option**, speak, release | Text is typed into the focused field |
| Double tap **Right Option** | Hands free: keeps listening until you tap it once more |
| **Escape** while recording | Cancels, nothing is typed |
| Say "new line" / "new paragraph" | Inserts a line break / blank line |
| Say "number one … number two …" | Puts each item on its own line: "1.", "2." |
| Say "sam at gmail dot com" | Types `sam@gmail.com` (common mail domains) |
| Menu bar or Dock icon | Last 5 transcriptions (click to copy), microphone, word list |

- Punctuation, question marks and capitals come from the speech model itself.
  Hesitations ("um", "uh") are removed.
- Every dictation also stays on the clipboard, so you can paste it again, or
  paste it yourself when no text field had focus.
- Pressing another key while holding Right Option (an Option shortcut) cancels
  the dictation, so your shortcuts still work.
- Recordings stop at 10 minutes, with a sound and a message.

### Word list

Menu > **Edit word list…** opens a plain text file. One rule per line: what the
model types = what you want.

```
Jim Ledger = Gym Ledger
Cloud Code = Claude Code
```

Matching ignores case and only replaces whole words. Saved changes apply to the
next dictation.

## Privacy

- The speech model runs on your Mac's Neural Engine. It works fully offline
  after the first download.
- The microphone is open only while you dictate. Audio is never written to disk.
- Only the last 5 texts are kept, in
  `~/Library/Application Support/FlowScribe/history.json`, readable only by you.

## How it works

- **Speech model:** NVIDIA Parakeet Unified English 0.6B, int8, through
  [FluidAudio](https://github.com/FluidInference/FluidAudio) (Apache-2.0).
  FluidAudio's benchmark on an M5 Pro: 2.15% WER on LibriSpeech test-clean.
- **Speed** (measured on a MacBook Air M5): about 0.4 s from releasing the key
  to the text appearing for a short dictation; about 1 s for a 2-minute one.
- **Memory:** about 50–70 MB for the app (the "Memory in use" number in the
  menu, same as Activity Monitor), plus about 600 MB of model weights held for
  the Neural Engine, which macOS counts separately.

### Why dictation apps "go silent", and the fixes here

| Cause | Fix |
|---|---|
| Accessibility permission missing, or reset because the app was re-signed | Checked on every paste; if it is off, the text is copied and you are told |
| Key monitors installed before Accessibility was granted stay deaf | Re-installed automatically once permission is granted |
| Cmd+V sent while the hotkey's modifier is still down | Waits until every modifier key is released |
| Password field with Secure Input blocks synthetic keys | Detected; the text is copied and you are told to press Cmd+V |
| Last word dropped when the key is released mid-word | Records 250 ms past release and pads the audio with 300 ms of silence |
| Errors swallowed | Every failure plays a sound and shows a message; the text is saved to history before any paste attempt |

## Build from source

Needs the Xcode command line tools (`xcode-select --install`).

```bash
git clone https://github.com/AkashMadanu/Flow-Scribe.git && cd Flow-Scribe
./scripts/build-app.sh            # build, sign, install to /Applications, open
./scripts/build-app.sh --package  # build a release zip in build/ instead
swift test                        # unit tests
```

The script signs with a local certificate it creates once (macOS asks for your
password to trust it), so the Accessibility permission survives rebuilds.

## Known limits

- English only. Apple Silicon only (the model runs on the Neural Engine).
- Not notarized by Apple yet, hence the one-time "Open Anyway" step.
- Bluetooth headsets (AirPods) switch to a call profile when the mic opens,
  which can clip the first half second. The built-in mic does not.
- Cmd+V is sent as the key in the "V" position of a QWERTY keyboard; on
  non-QWERTY layouts pasting may not work yet.

## License

MIT. See [LICENSE](LICENSE).
