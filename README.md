# CodeSnap

**A lightweight screen-code OCR overlay for Windows.**

CodeSnap watches a small region of your screen (or the whole screen), recognizes codes such as `HJK874` or `IIE352`, shows them in an always-on-top floating window, and can type or paste the code into whatever input box your mouse is over with a single hotkey.

Typical uses: game room / invite codes, Douyin (TikTok) chat, Discord, WeChat, and any other app where you need to retype a short code from the screen.

## Features

- Real-time OCR using the built-in Windows OCR engine — offline, no cloud service
- Tiny always-on-top overlay that can be dragged anywhere
- `Ctrl+Alt+F8` — click at the current mouse position, then paste or type the recognized code (optionally press Enter)
- `Ctrl+Alt+F10` — drag to select the screen region to scan
- Three scan modes: fixed region (fastest), around the cursor, full screen
- Multi-frame majority voting to avoid flicker and false positives
- Automatically switches to a new code when the code on screen changes
- `Corrections` map for OCR-specific confusions (Windows OCR systematically misreads `II` as `N`/`H`/`u`)
- No installer, no background service, no telemetry

## Requirements

- Windows 10 or 11 (x64)
- **PowerShell 7.4+** (`pwsh.exe`); Windows PowerShell 5.1 is not supported
- Windows OCR language pack (English and/or Chinese). Verify with `Check-Ocr.bat`
- The two .NET / WinRT projection DLLs are bundled in `lib/`

> The application is a single C# source file compiled in memory by the PowerShell host at startup (about 1–2 seconds). No .NET SDK or Visual Studio is required.

## Quick start

1. Download or clone this repository.
2. Double-click **`Check-Ocr.bat`**. You should see `识别结果: [HJK874]`. If not, install the Windows OCR language feature first.
3. Double-click **`CodeSnap.bat`**. An overlay appears in the top-right corner.
4. Display the code on screen, press **`Ctrl+Alt+F10`** and drag a rectangle tightly around the code.
   The overlay shows the recognized code after 1–2 matching frames.
5. Move the mouse over the input box where the code should be entered and press **`Ctrl+Alt+F8`** (or click **发送 / Send** in the overlay).
   The overlay temporarily hides, clicks at the mouse position, and pastes or types the code.

## Hotkeys

All hotkeys use `Ctrl+Alt`:

| Hotkey | Action |
|---|---|
| `F6` | Save a diagnostic frame and OCR text to `debug/` |
| `F7` | Cycle scan mode: region → cursor → full screen |
| `F8` | Send the recognized code to the mouse position |
| `F9` | Mark / clear an auto-send target |
| `F10` | Select the scan region |
| `F11` | Pause / resume scanning |
| `F12` | Exit |

## Configuration

Settings live in `config.json` next to the scripts (created when settings are saved). A template is provided in [`config.example.json`](config.example.json).

| Key | Default | Description |
|---|---|---|
| `Mode` | `full` | `region`, `cursor`, or `full` |
| `RegionX/Y/W/H` | – | Fixed scan region in physical pixels |
| `Regex` | `^[A-Z0-9]{4,12}$` | Accepted code pattern |
| `StrictShape` | `false` | Require letters followed by digits |
| `MinLetters` / `MinDigits` | `2` / `2` | Minimum composition of a candidate |
| `Zoom` | `2` | Upscale small regions before OCR (1–4) |
| `StableFrames` | `2` | Matching frames required before a code is committed |
| `MissToClear` | `5` | Empty frames before the current code is cleared |
| `OutputType` | `true` | `true` = type keystrokes; `false` = clipboard + `Ctrl+V` |
| `AutoSend` | `false` | Automatically send when a new code is recognized |
| `AutoEnter` | `false` | Press Enter after typing or pasting |
| `ClickDelayMs` | `130` | Delay after clicking the target before typing |
| `TypeDelayMs` | `12` | Delay between typed characters |
| `ScanIntervalMs` | `10` | Idle delay between scan frames |
| `OverrideCode` | `""` | Debug: force a fixed result (leave empty) |
| `Corrections` | `{}` | Map OCR misreads to the correct code, e.g. `{"NE352":"IIE352"}` |

### Recommended for WeChat and other chat apps

```json
"OutputType": false,
"AutoEnter": true
```

Clipboard paste bypasses Chinese IME composition and is generally the most reliable way to insert an ASCII code into a chat input box. Enable `AutoEnter` only if the target app should send the message immediately after insertion.

## OCR limitations

Windows OCR has a known weakness with pairs of capital `I`: `IIE352` is often read as `NE352`, `HE352` or `UE352`. This is an engine limitation, not a capture problem — it reproduces even with large, cleanly rendered text. Use the `Corrections` map to normalize such results:

```json
"Corrections": {
  "NE352": "IIE352",
  "HE352": "IIE352",
  "UE352": "IIE352"
}
```

Other confusions (`0`/`O`, `1`/`I`, `2`/`Z`, …) can be handled the same way.

## Privacy

Everything runs locally. There is no network access, no telemetry and no cloud OCR.

`codesnap.log` and the `debug/` directory may contain text captured from your screen (including private chat messages). **Both are git-ignored — do not upload them.** `config.json` may contain window coordinates and is also ignored.

## Third-party components

`lib/Microsoft.Windows.SDK.NET.dll` and `lib/WinRT.Runtime.dll` are Microsoft / CsWinRT assemblies distributed under the MIT license. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

MIT — see [LICENSE](LICENSE).

---

Chinese documentation: [README.zh-CN.md](README.zh-CN.md)