# Third-party code used by voice chat

| Library | Version | Where | Licence | Notes |
|---|---|---|---|---|
| [Concentus](https://github.com/lostromb/concentus) | 2.2.2 (NuGet) | RobustToolbox `Robust.Shared` | BSD-3-Clause (Opus licence, © Xiph.Org Foundation, Skype Limited, CSIRO, Microsoft, et al.) | Pure managed C# port of the Opus codec. No native binaries are shipped. Wrapped by `Robust.Shared.Audio.Codecs`, not exposed to content directly. |
| OpenAL Soft (existing) | engine | RobustToolbox | LGPL-2.0 (dynamically loaded, already shipped with the engine) | Capture uses the standard `ALC_EXT_CAPTURE` API through the engine's existing OpenTK bindings. |

Not used:

* **Figaro** (GPL-3.0) was only used as conceptual inspiration for what real-time voice effects are useful.
  No Figaro code was copied, ported or linked. All DSP in `Content.Shared/Voice/Dsp` was written from scratch from
  standard, public signal-processing techniques (RBJ biquad cookbook formulas, a textbook phase vocoder, a channel
  vocoder, standard compressor/gate/delay designs).
* No AI voice models or voice cloning of any kind. "Impersonation" copies a character's procedural DSP parameters.
