# Third-party code used by voice chat

| Library | Version | Where | Licence | Notes |
|---|---|---|---|---|
| [Whisper.net](https://github.com/sandrohanea/whisper.net) / whisper.cpp | 1.9.1 (NuGet) | `Content.Server` (voice-to-text) | MIT | Speech recognition models (OpenAI Whisper, MIT) are downloaded at runtime, not shipped. |
| [Avalonia](https://avaloniaui.net/) | 11.3 (NuGet) | `EchoesVoice` app | MIT | The app's window. |
| OpenAL Soft | via `Robust.Natives.OpenAL` | `EchoesVoice` app | LGPL-2.0 (dynamically loaded) | Microphone capture in the app, the same library the game uses for sound. |

Not used:

* **Figaro** (GPL-3.0) was only used as conceptual inspiration for what real-time voice effects are useful.
  No Figaro code was copied, ported or linked. All DSP in `Content.Shared/Voice/Dsp` was written from scratch from
  standard, public signal-processing techniques (RBJ biquad cookbook formulas, a textbook phase vocoder, a channel
  vocoder, standard compressor/gate/delay designs).
* No AI voice models or voice cloning of any kind. "Impersonation" copies a character's procedural DSP parameters.
