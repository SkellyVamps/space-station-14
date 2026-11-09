// Runs on the browser's audio thread: takes the microphone, brings it down to 16 kHz and packs it into 20 ms IMA ADPCM
// frames (the same format as Content.Shared/Voice/Codecs/ImaAdpcm.cs), which voice.js sends to the game server.

const OUT_RATE = 16000;
const FRAME_SAMPLES = 320;
const FRAME_BYTES = 3 + FRAME_SAMPLES / 2;

const INDEX_TABLE = [-1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8];
const STEP_TABLE = [
    7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97,
    107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
    876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871,
    5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
    27086, 29794, 32767,
];

// Low-pass biquad (RBJ cookbook), to stop frequencies above 8 kHz folding back when resampling.
class LowPass {
    constructor(rate, freq) {
        const w = 2 * Math.PI * freq / rate;
        const alpha = Math.sin(w) / (2 * Math.SQRT1_2);
        const cos = Math.cos(w);
        const a0 = 1 + alpha;
        this.b0 = (1 - cos) / 2 / a0;
        this.b1 = (1 - cos) / a0;
        this.b2 = this.b0;
        this.a1 = -2 * cos / a0;
        this.a2 = (1 - alpha) / a0;
        this.x1 = this.x2 = this.y1 = this.y2 = 0;
    }

    process(x) {
        const y = this.b0 * x + this.b1 * this.x1 + this.b2 * this.x2 - this.a1 * this.y1 - this.a2 * this.y2;
        this.x2 = this.x1;
        this.x1 = x;
        this.y2 = this.y1;
        this.y1 = y;
        return y;
    }
}

class VoiceCapture extends AudioWorkletProcessor {
    constructor() {
        super();
        this.step = sampleRate / OUT_RATE;
        this.filters = [new LowPass(sampleRate, 7000), new LowPass(sampleRate, 7000)];
        this.position = 0;
        this.previous = 0;
        this.frame = new Int16Array(FRAME_SAMPLES);
        this.count = 0;
        this.predictor = 0;
        this.index = 0;
        this.muted = false;
        this.port.onmessage = e => {
            if (e.data && typeof e.data.muted === "boolean")
                this.muted = e.data.muted;
        };
    }

    process(inputs) {
        const input = inputs[0] && inputs[0][0];
        if (!input)
            return true;

        for (let i = 0; i < input.length; i++) {
            let x = this.muted ? 0 : input[i];
            x = this.filters[1].process(this.filters[0].process(x));

            // Linear interpolation between this input sample and the last one, at every 16 kHz point that falls
            // between them.
            while (this.position <= 1) {
                const value = this.previous + (x - this.previous) * this.position;
                this.push(value);
                this.position += this.step;
            }

            this.position -= 1;
            this.previous = x;
        }

        return true;
    }

    push(value) {
        const s = Math.max(-1, Math.min(1, value));
        this.frame[this.count++] = s < 0 ? s * 32768 : s * 32767;
        if (this.count < FRAME_SAMPLES)
            return;

        this.count = 0;
        const packed = this.encode(this.frame);

        let sum = 0;
        for (let i = 0; i < FRAME_SAMPLES; i++)
            sum += this.frame[i] * this.frame[i];

        const level = Math.sqrt(sum / FRAME_SAMPLES) / 32768;
        this.port.postMessage({ frame: packed, level }, [packed.buffer]);
    }

    encode(samples) {
        const out = new Uint8Array(FRAME_BYTES);
        let predictor = this.predictor;
        let index = this.index;
        out[0] = predictor & 0xff;
        out[1] = (predictor >> 8) & 0xff;
        out[2] = index;

        for (let i = 0; i < FRAME_SAMPLES; i++) {
            const step = STEP_TABLE[index];
            let diff = samples[i] - predictor;
            let code = 0;
            if (diff < 0) {
                code = 8;
                diff = -diff;
            }

            let delta = step >> 3;
            if (diff >= step) {
                code |= 4;
                diff -= step;
                delta += step;
            }
            if (diff >= step >> 1) {
                code |= 2;
                diff -= step >> 1;
                delta += step >> 1;
            }
            if (diff >= step >> 2) {
                code |= 1;
                delta += step >> 2;
            }

            predictor = (code & 8) ? predictor - delta : predictor + delta;
            predictor = Math.max(-32768, Math.min(32767, predictor));
            index = Math.max(0, Math.min(STEP_TABLE.length - 1, index + INDEX_TABLE[code]));

            const at = 3 + (i >> 1);
            if ((i & 1) === 0)
                out[at] = code;
            else
                out[at] |= code << 4;
        }

        this.predictor = predictor;
        this.index = index;
        return out;
    }
}

registerProcessor("voice-capture", VoiceCapture);
