// The voice webpage: records the microphone (voice-worklet.js) and sends it to this game server, which plays it as
// the player's voice whenever their push to talk keys in the game say so. Linked to the player by the code after "#"
// in the address, which the game gives out.
"use strict";

(() => {
    const AUDIO_ADPCM = 1;
    const RECONNECT_MAX_MS = 15000;

    const $ = id => document.getElementById(id);
    const ui = {
        dot: $("status-dot"),
        text: $("status-text"),
        hint: $("status-hint"),
        start: $("start"),
        micCard: $("mic-card"),
        device: $("device"),
        meter: $("meter"),
        talking: $("talking"),
        mute: $("mute"),
        codeRow: $("code-row"),
        code: $("code"),
        copy: $("copy"),
        downloads: $("downloads"),
    };

    const code = decodeURIComponent(location.hash.replace(/^#/, "")).trim().toUpperCase();

    let socket = null;
    let socketReady = false;
    let reconnectDelay = 1000;
    let stopped = false;
    let sequence = 0;
    let muted = false;
    let context = null;
    let worklet = null;
    let stream = null;
    let shownLevel = 0;

    function setStatus(state, text, hint) {
        ui.dot.className = "dot" + (state ? " " + state : "");
        ui.text.textContent = text;
        ui.hint.textContent = hint || "";
    }

    // --- Connection to the game server ---

    function connect() {
        const scheme = location.protocol === "https:" ? "wss:" : "ws:";
        socket = new WebSocket(scheme + "//" + location.host + "/voice/ws");
        socket.binaryType = "arraybuffer";

        socket.onopen = () => {
            socket.send(JSON.stringify({ type: "hello", code, kind: "web" }));
        };

        socket.onmessage = e => {
            if (typeof e.data !== "string")
                return;

            let message;
            try {
                message = JSON.parse(e.data);
            } catch {
                return;
            }

            if (message.type === "welcome") {
                socketReady = true;
                reconnectDelay = 1000;
                setStatus("good", "Connected to the game",
                    context ? "" : "Allow the microphone to start talking.");
            } else if (message.type === "state") {
                const talking = !!message.talking;
                ui.talking.textContent = message.adminMuted ? "Muted by an admin" : talking ? "Talking" : "Not talking";
                ui.talking.className = "talking" + (talking ? " on" : "");
                if (message.name)
                    setStatus("good", "Connected to the game as " + message.name, ui.hint.textContent);
            }
        };

        socket.onclose = e => {
            socketReady = false;
            socket = null;

            // The server said why: don't keep retrying something that won't work.
            if (e.code >= 4001 && e.code <= 4005) {
                stopped = true;
                setStatus("bad", "Disconnected", e.reason || "The game server closed the connection.");
                stopMicrophone();
                return;
            }

            if (stopped)
                return;

            setStatus("bad", "Connection lost, reconnecting...",
                "Your voice goes back to the game's own microphone until this reconnects.");
            setTimeout(connect, reconnectDelay);
            reconnectDelay = Math.min(RECONNECT_MAX_MS, reconnectDelay * 2);
        };
    }

    // Keeps quiet connections from being closed by proxies in between.
    setInterval(() => {
        if (socketReady)
            socket.send('{"type":"ping"}');
    }, 20000);

    // --- Microphone ---

    async function startMicrophone(deviceId) {
        stopMicrophone();

        const constraints = {
            audio: {
                channelCount: 1,
                echoCancellation: true,
                noiseSuppression: true,
                autoGainControl: true,
            },
        };

        if (deviceId)
            constraints.audio.deviceId = { exact: deviceId };

        try {
            stream = await navigator.mediaDevices.getUserMedia(constraints);
        } catch (err) {
            setStatus("bad", "No microphone", err && err.name === "NotAllowedError"
                ? "The browser blocked the microphone. Allow it in the address bar, then reload."
                : "Couldn't open a microphone: " + (err && err.message ? err.message : err));
            ui.start.hidden = false;
            return;
        }

        context = new AudioContext({ latencyHint: "interactive" });
        await context.audioWorklet.addModule("/voice/voice-worklet.js");
        const source = context.createMediaStreamSource(stream);
        worklet = new AudioWorkletNode(context, "voice-capture", { numberOfInputs: 1, numberOfOutputs: 0 });
        worklet.port.postMessage({ muted });
        worklet.port.onmessage = e => onFrame(e.data.frame, e.data.level);
        source.connect(worklet);

        // Browsers only let audio start after a click on the page.
        try {
            await context.resume();
        } catch {
        }

        ui.start.hidden = context.state === "running";
        ui.start.textContent = "Start";
        ui.micCard.hidden = false;
        if (socketReady)
            ui.hint.textContent = "";

        await listDevices(stream.getAudioTracks()[0]?.getSettings().deviceId);
    }

    function stopMicrophone() {
        if (stream)
            stream.getTracks().forEach(t => t.stop());

        if (context)
            context.close();

        stream = null;
        context = null;
        worklet = null;
    }

    function onFrame(frame, level) {
        // Level meter, in decibels so normal speech fills most of it.
        const db = 20 * Math.log10(Math.max(level, 1e-5));
        const target = muted ? 0 : Math.max(0, Math.min(1, (db + 60) / 50));
        shownLevel = target > shownLevel ? target : shownLevel * 0.85 + target * 0.15;
        ui.meter.style.width = (shownLevel * 100).toFixed(0) + "%";

        if (!socketReady || socket.bufferedAmount > 16384)
            return;

        const message = new Uint8Array(3 + frame.length);
        message[0] = AUDIO_ADPCM;
        message[1] = sequence & 0xff;
        message[2] = (sequence >> 8) & 0xff;
        message.set(frame, 3);
        sequence = (sequence + 1) & 0xffff;
        socket.send(message);
    }

    async function listDevices(current) {
        const devices = (await navigator.mediaDevices.enumerateDevices()).filter(d => d.kind === "audioinput");
        ui.device.innerHTML = "";
        for (const device of devices) {
            const option = document.createElement("option");
            option.value = device.deviceId;
            option.textContent = device.label || "Microphone";
            option.selected = device.deviceId === current;
            ui.device.appendChild(option);
        }
    }

    ui.device.onchange = () => startMicrophone(ui.device.value);
    ui.start.onclick = () => {
        if (context && context.state !== "running")
            context.resume().then(() => ui.start.hidden = context.state === "running");
        else
            startMicrophone();
    };

    ui.mute.onclick = () => {
        muted = !muted;
        ui.mute.textContent = muted ? "Unmute" : "Mute";
        ui.mute.className = muted ? "on" : "";
        if (worklet)
            worklet.port.postMessage({ muted });
    };

    // --- The standalone app ---

    async function loadDownloads() {
        try {
            const result = await (await fetch("/voice/downloads", { cache: "no-store" })).json();
            const list = document.createElement("ul");
            for (const file of result.files || []) {
                const item = document.createElement("li");
                const link = document.createElement("a");
                link.href = file.url;
                link.textContent = file.name;
                link.setAttribute("download", file.name);
                item.appendChild(link);
                item.appendChild(document.createTextNode(" (" + (file.size / 1048576).toFixed(1) + " MB)"));
                list.appendChild(item);
            }

            ui.downloads.innerHTML = "";
            if (result.url) {
                const p = document.createElement("p");
                const link = document.createElement("a");
                link.href = result.url;
                link.rel = "noopener";
                link.textContent = "Download the Echoes Voice app";
                p.appendChild(link);
                ui.downloads.appendChild(p);
            }

            if (list.children.length > 0)
                ui.downloads.appendChild(list);

            if (!result.url && list.children.length === 0)
                ui.downloads.innerHTML = '<p class="hint">This server doesn\'t offer the app for download yet.</p>';
        } catch {
            ui.downloads.innerHTML = '<p class="hint">Couldn\'t load the downloads.</p>';
        }
    }

    ui.copy.onclick = async () => {
        try {
            await navigator.clipboard.writeText(code);
            ui.copy.textContent = "Copied";
            setTimeout(() => ui.copy.textContent = "Copy", 1500);
        } catch {
            // Selecting it lets them copy by hand.
            getSelection().selectAllChildren(ui.code);
        }
    };

    // --- Start ---

    loadDownloads();

    if (!code) {
        setStatus("bad", "Not linked to the game",
            "Open this page from the game: Options > Voice > Use browser microphone.");
        return;
    }

    ui.code.textContent = code;
    ui.codeRow.hidden = false;

    if (!window.isSecureContext || !navigator.mediaDevices || !window.AudioWorkletNode) {
        setStatus("bad", "This page needs a secure (https) address",
            "Browsers only allow microphones on https pages. Ask the server owner to set voice.external_public_url.");
        return;
    }

    setStatus("", "Connecting to the game...");
    connect();

    // Browsers only start a microphone after a click, unless permission was already given.
    ui.start.hidden = false;
    navigator.permissions?.query({ name: "microphone" }).then(p => {
        if (p.state === "granted")
            startMicrophone();
    }).catch(() => {});
})();
