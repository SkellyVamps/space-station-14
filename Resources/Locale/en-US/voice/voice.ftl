## Verbs

verb-categories-voice = Voice
voice-verb-mute = Mute voice
voice-verb-unmute = Unmute voice
voice-verb-volume = Voice volume { $percent }%
voice-verb-admin-mute = Voice mute (admin)
voice-verb-admin-unmute = Remove voice mute (admin)

## Commands

cmd-voicemute-desc = Prevents a player from using voice chat.
cmd-voicemute-help = Usage: voicemute <player> [minutes, 0 = indefinite] [reason...]
cmd-voicemute-invalid-minutes = Minutes must be a non-negative number.
cmd-voicemute-no-player = Unable to find a player named { $player }.
cmd-voicemute-success = { $player } is now voice muted.
cmd-voicemute-hint-player = <player>
cmd-voicemute-hint-minutes = [minutes]
cmd-voicemute-hint-reason = [reason]

cmd-voiceunmute-desc = Lifts a voice chat mute.
cmd-voiceunmute-help = Usage: voiceunmute <player>
cmd-voiceunmute-success = { $player } can use voice chat again.
cmd-voiceunmute-not-muted = { $player } is not voice muted.

cmd-voicemutes-desc = Lists players who are voice muted.
cmd-voicemutes-help = Usage: voicemutes
cmd-voicemutes-none = Nobody is voice muted.

cmd-voicestats-desc = Shows voice chat routing and performance statistics.
cmd-voicestats-help = Usage: voicestats

## Character editor

humanoid-profile-editor-character-voice-label = Voice chat character voice
humanoid-profile-editor-character-voice-pitch = Pitch
humanoid-profile-editor-character-voice-formant = Vocal size
humanoid-profile-editor-character-voice-brightness = Brightness
humanoid-profile-editor-character-voice-roughness = Roughness
humanoid-profile-editor-character-voice-breathiness = Breathiness
humanoid-profile-editor-character-voice-nasality = Nasality
humanoid-profile-editor-character-voice-tremble = Tremble
humanoid-profile-editor-character-voice-preset = Preset
humanoid-profile-editor-character-voice-preset-custom = Custom
humanoid-profile-editor-character-voice-copy-from = Copy from
humanoid-profile-editor-character-voice-copy-from-choose = Another character...
humanoid-profile-editor-character-voice-copy-code = Copy code
humanoid-profile-editor-character-voice-copy-code-tooltip = Copies this voice as text, to paste into another character or share with a friend.
humanoid-profile-editor-character-voice-paste-code = Paste code
humanoid-profile-editor-character-voice-paste-code-tooltip = Sets this character's voice from a code copied with "Copy code".
humanoid-profile-editor-character-voice-preview = Preview
humanoid-profile-editor-character-voice-preview-tooltip = Hear your microphone through this character's voice (including species). Nothing is sent to anyone.
humanoid-profile-editor-character-voice-randomize = Randomize
humanoid-profile-editor-character-voice-reset = Reset

## Voice impersonation

voice-impersonator-verb-record = Record voice
voice-impersonator-verb-record-message = Record this person's voice so the voice changer can imitate it.
voice-impersonator-recording = You start discreetly recording their voice...
voice-impersonator-recorded = Recorded { $name }'s voice. Set the voice changer's name to "{ $name }" to use it.
voice-impersonator-examine = It has voice recordings of: { $names }.

## Options

ui-options-tab-voice = Voice
ui-options-voice-header = Voice chat
ui-options-voice-enabled = Enable voice chat (speaking and hearing)
ui-options-voice-transcribe-notice = This server converts voice chat to text for its admin logs, like text chat is logged. Your audio itself is not recorded.
ui-options-voice-input-header = Microphone
ui-options-voice-input-device = Microphone
ui-options-voice-input-device-default = System default
ui-options-voice-input-device-missing = { $device } (not connected)
ui-options-voice-input-gain = Microphone volume
ui-options-voice-level = Input level
ui-options-voice-mic-test = Test microphone
ui-options-voice-mic-test-tooltip = Hear yourself through your current character's voice. Nothing is sent to the server.
ui-options-voice-transmit-header = Transmission
ui-options-voice-input-mode = Mode
ui-options-voice-mode-ptt = Push to talk
ui-options-voice-mode-vad = Voice activation
ui-options-voice-ptt-hint = Push to talk: { $key }. Radio: { $radioKey }. Change these in Controls.
ui-options-voice-unbound = unbound
ui-options-voice-activation-threshold = Voice activation threshold
ui-options-voice-activation-release = Voice activation hold time
ui-options-voice-noise-gate = Noise gate
ui-options-voice-noise-suppression = Noise suppression
ui-options-voice-noise-suppression-hint = Removes steady background noise (fans, hum, hiss). Check it with "Test microphone".
ui-options-voice-radio-keys-header = Extra radio keys
ui-options-voice-radio-keys-hint = Each key talks on its own channel. Bind them in Controls under "Voice".
ui-options-voice-radio-key2 = Radio key 2 channel
ui-options-voice-radio-key3 = Radio key 3 channel
ui-options-voice-radio-key4 = Radio key 4 channel
ui-options-voice-radio-key-unset = Not set
ui-options-voice-off = Off
ui-options-voice-output-header = Playback
ui-options-voice-output-volume = Voice volume
ui-options-voice-radio-volume = Radio voice volume
ui-options-voice-jitter-buffer = Network buffer (smoothness vs. delay)
ui-options-voice-ducking = Lower game sounds while someone talks
ui-options-voice-indicators = Show speaking indicators
ui-options-voice-radio-panel = Show radio panel
ui-options-voice-radio-panel-reset = Reset radio panel position
ui-options-voice-mute-hint = To mute or change the volume of a specific player, right click them and use the Voice menu.

ui-options-voice-status-server-disabled = Voice chat is disabled on this server.
ui-options-voice-status-admin-muted = You have been muted from voice chat by an administrator.
ui-options-voice-status-unsupported = Microphone capture isn't supported on this system.
ui-options-voice-status-unavailable = No microphone could be opened. Check it's plugged in and that the game is allowed to use it.
ui-options-voice-status-disconnected = Microphone disconnected. Reconnecting automatically...
ui-options-voice-status-capturing = Using { $device }.
ui-options-voice-status-idle = Microphone is closed until you talk.

## Client commands

cmd-voiceradio-desc = Chooses the radio channel used by the radio push-to-talk key.
cmd-voiceradio-help = Usage: voiceradio [channel]. Without a channel, resets to your headset's default channel.
cmd-voiceradio-no-entity = You aren't controlling anything that can talk.
cmd-voiceradio-default = Radio push to talk uses your default channel ({ $channel }).
cmd-voiceradio-available = Available channels: { $channels }
cmd-voiceradio-unavailable = You can't transmit on { $channel }.
cmd-voiceradio-set = Radio push to talk now transmits on { $channel }.
cmd-voiceradio-hint = <channel>

## Radio panel

voice-radio-panel-title = Voice
voice-radio-panel-settings-tooltip = Choose which radio channels you hear and talk on. Drag the panel to move it; F4 hides it.
voice-radio-panel-nobody = Nobody is talking
voice-radio-panel-speaker = [{ $channel }] { $name }
voice-radio-panel-unknown-speaker = Unknown
voice-radio-panel-listen-header = Listen
voice-radio-panel-listen-tooltip = Hear voice on { $channel }. Text messages on the channel are not affected.
voice-radio-panel-talk = Talk on
voice-radio-panel-talk-default = Default ({ $channel })
voice-radio-panel-talk-none = None (no radio)
voice-radio-panel-no-channels = No radio channels available

## Top menu bar

game-hud-open-voice-radio-panel-button-tooltip = Show or hide the radio panel (who is talking on the radio, which channels you hear and talk on).
voice-radio-panel-volume-tooltip = Volume of { $channel } (middle is normal).

## Voice presets (character editor)

voice-preset-neutral = Neutral
voice-preset-deep = Deep
voice-preset-booming = Booming
voice-preset-gravelly = Gravelly
voice-preset-hoarse = Hoarse
voice-preset-soft = Soft
voice-preset-whispery = Whispery
voice-preset-bright = Bright
voice-preset-youthful = Youthful
voice-preset-squeaky = Squeaky
voice-preset-nasal = Nasal
voice-preset-nervous = Nervous
voice-preset-elderly = Elderly

## Voice panel: who is talking, whisper / talk / shout, mute

voice-panel-you = You
voice-panel-unknown = Unknown
voice-panel-route-radio = Radio
voice-panel-route-loudspeaker = { $channel } (speaker)
voice-panel-route-intercom = { $channel } (intercom)
voice-panel-route-muted = Muted
voice-panel-route-admin-muted = Muted by admin
voice-panel-route-cant-speak = Can't speak

voice-range-whisper = Whisper
voice-range-talk = Talk
voice-range-shout = Shout
voice-panel-range-tooltip = How far your voice carries: whisper for the people next to you, shout across a room. Click to switch.

voice-panel-mute-off = Mic on
voice-panel-mute-off-tooltip = Your microphone is on. Click to mute it.
voice-panel-mute-on = Muted
voice-panel-mute-on-tooltip = You muted your microphone. Click to unmute it.
voice-panel-mute-admin = Muted
voice-panel-mute-admin-tooltip = An admin has muted your voice until they lift it.
voice-panel-mute-admin-tooltip-timed = An admin has muted your voice for { $minutes } more { $minutes ->
    [one] minute
   *[other] minutes
}.

## Voice webpage / app as the microphone

ui-options-voice-external-header = Browser or app microphone
ui-options-voice-external-status-off = You can use your web browser, or the Echoes Voice app, as your microphone instead of the game.
ui-options-voice-external-status-web = Your microphone is the voice webpage in your browser. Keep it open; your push to talk keys still work as usual.
ui-options-voice-external-status-app = Your microphone is the Echoes Voice app. Your push to talk keys still work as usual.
ui-options-voice-external-open = Use browser microphone
ui-options-voice-external-open-tooltip = Opens this server's voice page in your browser, already linked to you. Allow the microphone there.
ui-options-voice-external-copy = Copy link for the app
ui-options-voice-external-copy-tooltip = Copies your personal voice link. Paste it into the Echoes Voice app, or open it on another device. Don't share it: it lets someone talk as you.
ui-options-voice-external-copied = Link copied
ui-options-voice-external-disconnect = Use game microphone

cmd-voicelink-desc = Uses the voice webpage or the Echoes Voice app as your microphone.
cmd-voicelink-help = Usage: voicelink [open | copy | print | off]. open: open the voice page in your browser. copy: copy your link for the app. print: show your link. off: go back to the game's microphone.
cmd-voicelink-copied = Your voice link is on the clipboard. Don't share it: it lets someone talk as you.
