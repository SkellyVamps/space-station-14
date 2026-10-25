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
* A green ring above a character means they're talking; yours turns gold when you're on the radio. The ring only
  shows for people you can actually see: someone talking behind a wall or inside a locker is heard, but not marked.

Talking into your radio is also heard, a little more quietly, by people near you.

## Radio panel

A small **Radio** panel shows who is talking on the radio and on which channel. It's there whenever you're in game,
including as an observer.

* **F4** shows or hides it (change the key in Options -> Controls, "Voice: show/hide radio panel").
* **Drag** it anywhere on the screen; it stays where you put it. Options -> Voice can also hide it or reset its
  position.
* Press **+** to open its settings:
  * **Talk on** picks the channel the radio key transmits on (or your headset's default). It only shows when you
    have a radio you can talk into.
  * **Listen** lists the channels you can hear, the same ones you have in text chat (every channel as an observer).
    Untick a channel to stop hearing voice on it. Text messages on that channel still show in chat.

The console command `voiceradio <channel>` also picks the talk channel.

## Animals and other creatures that can't talk

If your character can't speak (a hamster, mouse, dog, monkey, zombie...), nobody hears your microphone. When you talk,
your character makes its usual noise every second or so instead. Whatever can't be understood in text chat ("Squeak!")
can't be understood in voice either. You still hear everyone else normally, and a creature made sentient regains
its voice.

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
