# FlowScribe for Windows (preview)

The Windows version of FlowScribe: hold **Right Alt**, speak, let go, and the
text is typed where your cursor is. Same rules as the Mac app: on-device speech
recognition, last 5 transcriptions, word list, no settings screens.

> **Status: not yet tested on a real Windows PC.** The build, the unit tests and
> the speech model are tested automatically on Windows (GitHub Actions). The
> parts that need a person at a keyboard (the hotkey, pasting into apps, the
> tray icon, the microphone) have not been tried yet. See the checklist below.

## Use it

| Action | What happens |
|---|---|
| Hold **Right Alt**, speak, release | Text is typed into the focused field |
| Double tap **Right Alt** | Hands free: keeps listening until you tap it once more |
| **Escape** while recording | Cancels, nothing is typed |
| Say "new line" / "new paragraph", "number one … number two …", "sam at gmail dot com" | Same as on the Mac |
| Tray icon (click it) | Last 5 transcriptions (click to copy), microphone, word list, memory |

Every dictation stays on the clipboard. It starts at login (turn that off in
Task Manager > Startup apps). Data lives in `%APPDATA%\FlowScribe`; the speech
model (about 660 MB, downloaded once) in `%LOCALAPPDATA%\FlowScribe\Models`.

## How it differs from the Mac app

- **Speech model:** NVIDIA Parakeet TDT 0.6B v2 (English, punctuation and
  capitals), int8, on the CPU through [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
  (Apache-2.0). Windows PCs have no Apple Neural Engine, so it runs on the CPU.
- **Memory is much higher than on the Mac:** the model sits in the app's own
  memory. Measured working set with the model loaded: about 1.8 GB (macOS
  arm64 run of the same engine); the Windows CI log reports the x64 number.
- **Hotkey:** Right Alt. On keyboard layouts where Right Alt is AltGr (German,
  French, …), holding it to dictate works, but AltGr characters typed while
  dictating cancel the dictation, the same as other shortcuts.

## Build

Needs the .NET 10 SDK. From this folder:

```
dotnet test FlowScribe.Core.Tests --filter "Category!=Speech"   # unit tests
dotnet publish FlowScribe/FlowScribe.csproj -c Release -o publish  # publish\FlowScribe.exe
```

The speech tests download the model and run it on recorded clips:
`set FLOWSCRIBE_MODEL_DIR=C:\some\folder` then
`dotnet test FlowScribe.Core.Tests --filter Category=Speech`.

## First test on a real PC

1. Run `FlowScribe.exe`. Windows SmartScreen may warn ("unrecognized app"):
   **More info > Run anyway** (the exe is not code-signed).
2. Wait for the tray tooltip to say "Hold Right Alt to dictate".
3. In Notepad, hold Right Alt, say "This is a test", release. Expected: the
   text appears and Notepad's menu bar does **not** get focus.
4. Same in a browser text box and in Slack or Teams.
5. Double tap Right Alt, speak 30 seconds, tap again.
6. Press Right Alt + another key (for example AltGr+E on a German layout):
   the character should type normally and nothing should be dictated.
7. Open an app as administrator and dictate into it: expected message
   "Copied. That window runs as administrator, so press Ctrl+V."
8. Tray menu: check Last 5, Microphone, Edit word list, Memory in use.
9. Timings are logged (without the text) in `%APPDATA%\FlowScribe\log.txt`.
