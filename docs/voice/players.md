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

## Using your browser (or the app) as your microphone

If the game can't use your microphone, or you'd rather pick it in your browser, go to **Options -> Voice** and press
**Use browser microphone**. This server's voice page opens in your browser, already linked to you: allow the
microphone and leave the tab open (you can minimise it). Everything else stays the same: talk with your usual push to
talk keys **in the game**, and the game still decides who hears you.

* **Copy link for the app** copies your personal link for the Echoes Voice app (or another device). Don't share it:
  anyone with it can talk as you until you leave the server.
* **Use game microphone** switches back. Closing the tab does too.
* The console command `voicelink` does the same (`voicelink open`, `copy`, `print`, `off`).

## Talking

* **Push to talk** (default): hold **N** to talk to people nearby, **M** to talk over your headset radio.
  Change keys in **Options -> Controls** ("Voice: push to talk").
* **Voice activation**: switch *Mode* in Options -> Voice. You transmit whenever you're louder than the threshold.
  Raise the threshold if background noise keeps triggering it.
* While you type in chat or any text box, push to talk is ignored.
* A small **level meter** (three bars that move with their voice) appears above anyone talking, yourself included.
  It's white for normal talking, blue-grey for a whisper, orange for a shout, the channel's colour when you talk on the
  radio or an intercom plays someone, and red when you try to talk but can't be heard. It only shows for people you
  can actually see: someone talking behind a wall or inside a locker is heard, but not marked.

### Whisper, talk, shout

The **Talk** button at the top of the voice panel switches between **Whisper** (only the people right next to you),
**Talk** and **Shout** (across a room). Walls and doors still muffle you. You can also bind a key to it in
Options -> Controls ("Voice: switch whisper / talk / shout"). It only changes your voice in person, not the radio.

### Muting your microphone

The **Mic on** button next to it mutes your microphone: it turns red and says **Muted**, and nothing is sent whatever
key you press (bindable as "Voice: mute / unmute microphone"). If an **admin** has muted you, the button flashes
**red and purple**; hover it to see for how long.

Talking into your radio is also heard, a little more quietly, by people near you.

### More radio keys

Options -> Voice -> *Extra radio keys* gives you up to three more radio keys, each set to its own channel (for
example key 2 = Security, key 3 = Engineering). Bind them in Options -> Controls ("Voice: push to talk (radio key 2)"
and so on). A key set to a channel your character doesn't have just talks normally.

## Intercoms and handheld radios

* An intercom or handheld radio with its **speaker** on plays radio voice out loud. Everyone nearby hears it from the
  device, through walls and doors like normal speech, with the radio sound, whether or not they have a headset too.
  The intercom you're talking into doesn't play your own voice back.
* To talk over an **intercom**, open it and **hold the "Hold to talk" button**. While it's held your voice goes out on
  the intercom's channel; no push to talk key needed. People next to you still hear you, a little quieter. Letting go,
  closing the window or walking away stops it. (The intercom's Mic. switch is for text chat only.)
* A **handheld radio** with its **microphone** on broadcasts what people say near it on its channel, just like text
  chat.

## Voice panel

A small **Voice** panel shows a card for everyone you hear talking: people nearby, the radio, intercoms, and yourself
at the bottom. Each card has their name, how you hear them (radio channel, "(speaker)" for an intercom, whisper or
shout) in its colour, and a moving waveform of their voice. Cards stay a moment after someone stops, then fade.
People nearby are only named if you can see them; otherwise they show as **Unknown**. It's there whenever you're in
game, including as an observer, and also holds the whisper / talk / shout and mute buttons.

* **F4**, or the **microphone button** in the top menu bar, shows or hides it (change the key in Options -> Controls,
  "Voice: show/hide voice panel"; the button shows the current key).
* **Drag** it anywhere on the screen; it stays where you put it. Options -> Voice can also hide it or reset its
  position.
* Press **+** to open its settings:
  * **Talk on** picks the channel the radio key transmits on (or your headset's default). It says **None** when you
    have nothing to talk into (no headset, observers, creatures).
  * The slider next to each channel sets its volume (middle is normal).
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
brightness, roughness, breathiness, nasality and tremble. Press **Preview** to hear it with your microphone. Each
character keeps their own voice. Species add their own touch (a lizard's slight hiss, a Vox's sharpness...).

* **Preset** starts from a ready-made voice (deep, gravelly, soft, squeaky, nasal, elderly...); adjust from there.
* **Copy from** copies the voice of another of your characters.
* **Copy code** / **Paste code** turn a voice into a short piece of text (`VOICE1:...`) you can paste into another
  character or send to a friend.

In game, how you sound also depends on:

* **Your species**: lizards hiss a little, moths flutter, Vox are sharp, Vulpkanin growl slightly, Gingerbread are
  light and crumbly, and so on. Humans are the baseline.
* **What's over your mouth**: gas and breath masks, surgical masks and gaiters, hard face masks (animal masks, Joy),
  the plague doctor's beak, sealed helmets, muzzles. A clown mask makes you a bit nasal. Pulling a mask down removes
  the effect.
* **How you're doing**: drunk (slurred), drowsy, shaking, out of breath, suffocating or freezing (shivering) all
  change your voice while they last.
* Some devices can disguise your voice.

## Voice-to-text

If the server has it on (Options -> Voice tells you), what you say over voice chat is converted to text for the
server's admin logs, the same way text chat is logged. Only the text is kept; your audio is never recorded.

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
| Background noise gets through | Turn up *Noise suppression*, raise *Noise gate* or the voice activation threshold, or use push to talk. |
| Hard to hear people over the game | Turn up *Lower game sounds while someone talks*. |
| Choppy voices | Raise *Network buffer*. |
| Echo | Use headphones. |
| Voice chat unavailable | The server may have voice disabled. |
