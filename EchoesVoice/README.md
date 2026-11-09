# Echoes Voice

The standalone voice app for Space Station 14, Echoes of the Void. It sends your microphone to the game server you're
playing on, instead of the game's own microphone or the browser voice page. Your push to talk keys in the game still
decide when you're heard.

## Using it

1. Download the zip for your system from the server's voice page (Options > Voice > Use browser microphone shows it)
   or from this repository's releases, unzip and run it.
   * Windows may warn that the app isn't recognised: More info > Run anyway.
   * macOS: right click "Echoes Voice" > Open the first time (it isn't signed by Apple).
2. In the game: Options > Voice > **Copy link for the app**, then **Paste** and **Connect** in the app. Or press
   **Open in the app** on the voice page.
3. Keep the app open while you play (minimising is fine). It reconnects by itself.

The app only sends your voice while connected, and only to the server you linked. When a link from a website points at
a server you haven't used before, the app asks first.

`EchoesVoice --list-devices` lists the microphones it can see.

## Building

Not part of the game's build. `dotnet publish EchoesVoice -c Release -r win-x64 --self-contained
-p:PublishSingleFile=true` (or `linux-x64`, `osx-x64`, `osx-arm64`), or run the **Publish Echoes Voice app** workflow
with a tag like `voice-app-v1.0.0` to build all of them and make a release.

It compiles a few files straight from `Content.Shared/Voice` (the audio format and noise removal), so those must stay
free of game types.
