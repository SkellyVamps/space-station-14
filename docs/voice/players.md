# Voice chat: player guide

Voice chat is part of the game; there's nothing to install. People hear you the way their character would: from
where your character is standing, quieter with distance, muffled through doors, not at all through walls or vacuum.

## Setting up your microphone

1. Open **Options -> Voice**.
2. Pick your **Microphone** (or leave *System default*).
3. Press **Test microphone** and talk. You hear yourself exactly as others will, including your character's voice.
   Adjust **Microphone volume** until the level meter moves well into the bar when you talk normally.
4. Press **Test microphone** again to stop.

If your microphone gets unplugged, voice reconnects automatically when it's back. You can change microphones at any
time without restarting.

## Talking

* **Push to talk** (default): hold **N** to talk to people nearby, **M** to talk over your headset radio.
  Change keys in **Options -> Controls** ("Voice: push to talk").
* **Voice activation**: switch *Mode* in Options -> Voice. You transmit whenever you're louder than the threshold.
  Raise the threshold if background noise keeps triggering it.
* While you type in chat or any text box, push to talk is ignored.
* A green ring above a character means they're talking; yours turns gold when you're on the radio.
* Radio transmissions you can hear are listed at the top right with their channel.
* Choose which radio channel the radio key uses with the console command `voiceradio <channel>` (e.g.
  `voiceradio Engineering`); `voiceradio` alone resets to your headset's default.

Talking into your radio is also heard, a little more quietly, by people near you.

## Your character's voice

In the character editor, **Voice chat character voice** changes how *this character* sounds: pitch, vocal size,
brightness, roughness, breathiness. Press **Preview** to hear it with your microphone. Each character keeps their own
voice. Species add their own touch (a lizard's slight hiss, a Vox's sharpness...).

In game, masks, helmets and some equipment change how you sound, and some devices can disguise your voice.

## Muting people

Right click someone -> **Voice** -> **Mute voice**, or pick a volume (50%-200%). This follows the *player*, not just
the body, for as long as the server keeps running. Options -> Voice has overall voice and radio volume, and a switch to turn voice chat
off completely.

## Troubleshooting

| Problem | Try |
|---|---|
| "No microphone could be opened" | Check it's plugged in and selected. On Windows/macOS, allow the game (or the SS14 launcher) to use the microphone in the system privacy settings. Close other apps holding it exclusively. |
| Others can't hear me | Use *Test microphone*. Check the level meter moves. Make sure you're not in vacuum, behind a wall, or muted (Options -> Voice shows if an admin muted you). Dead characters can't talk. |
| I'm too quiet / clipping | Adjust *Microphone volume*. |
| Background noise gets through | Raise *Noise gate* or the voice activation threshold, or use push to talk. |
| Choppy voices | Raise *Network buffer*. |
| Echo | Use headphones. |
| Voice chat unavailable | The server may have voice disabled. |
