# FlowScribe

Free, private, on-device dictation for Apple Silicon Macs. One job: hold a key,
speak, let go, and the text appears where your cursor is. It keeps your last 5
transcriptions in the menu bar and deletes anything older.

## Use it

| Action | What happens |
|---|---|
| Hold **Right Option**, speak, release | Text is typed into the focused field |
| Double tap **Right Option** | Hands free: keeps listening until you tap it once more |
| **Escape** while recording | Cancels, nothing is typed |
| Say "new line" / "new paragraph" | Inserts a line break / blank line |
| Menu bar icon | Last 5 transcriptions; click one to copy it |

Punctuation, question marks and capitals come from the speech model itself.
Hesitations ("um", "uh") are removed. Pressing another key while holding Right
Option (an Option shortcut) cancels the dictation, so your shortcuts still work.

## Install (macOS 14+, Apple Silicon)

Needs the Xcode command line tools (`xcode-select --install`).

```bash
git clone <this repo> && cd FlowScribe
./scripts/build-app.sh
```

The script builds a release binary, signs it with a local certificate it
creates once, copies `FlowScribe.app` into `/Applications`, and opens it.
On first launch:

1. Allow **Microphone**.
2. Allow **Accessibility** (System Settings > Privacy & Security > Accessibility).
   This is what lets it paste into other apps.
3. Wait for the one-time model download (menu bar shows a download icon).

Run the tests with `swift test`.

## How it works

- **Speech model:** NVIDIA Parakeet Unified English 0.6B, int8, through
  [FluidAudio](https://github.com/FluidInference/FluidAudio) (Apache-2.0).
  It runs on the Neural Engine, not the CPU or GPU. FluidAudio's own benchmark
  on an M5 Pro reports 2.15% WER on LibriSpeech test-clean at about 123x real
  time, with punctuation and capitals, beating Parakeet TDT v3 on English.
- **Fully offline** after the first download. Audio never leaves the Mac and is
  never written to disk; only the last 5 texts are kept, in
  `~/Library/Application Support/FlowScribe/history.json` (readable only by you).
- **Microphone is open only while recording.**

## Why other dictation apps "go silent", and the fixes here

| Cause | Fix |
|---|---|
| Accessibility permission missing, or silently reset because an ad-hoc signed app was rebuilt | Checked on every paste; a stable local signing certificate keeps the permission across rebuilds; if it is off, the text is copied and you are told |
| Key monitors installed before Accessibility was granted stay deaf | Re-installed automatically once permission is granted |
| Cmd+V sent while the hotkey's modifier is still down (target sees Option+Cmd+V) | Waits until every modifier key is released, sends Cmd+V with explicit flags |
| Clipboard restored before slow apps (Electron, browsers) read it | Restores after 0.9 s, and only if nothing else changed it |
| Password field with Secure Input blocks synthetic keys | Detected; text is copied and you are told to press Cmd+V |
| Errors swallowed | Every failure plays a sound and shows a message; the text is saved to history before any paste attempt |

## Memory

The menu shows the app's live memory footprint (the same number Activity
Monitor shows). The target is under 500 MB. If you see more, open an issue
with the number: the smaller `phonon2` Parakeet build is the fallback.

## Known limits

- English only.
- Bluetooth headsets (AirPods) switch to a call profile when the mic opens,
  which can clip the first half second. The built-in mic does not.
- Cmd+V is sent as the key in the "V" position of a QWERTY keyboard; on
  non-QWERTY layouts pasting may not work yet.
