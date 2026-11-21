"""Builds the voice track for the trailer score: reads the synthesised lines (22.05 kHz mono WAVs), applies a small room or a radio effect,
places them on the timeline and returns (duck, left, right) float arrays for the score scripts' final mix. Pure standard library.
Environment: VO_JSON (dialogue json) and VO_DIR (folder with vo_<id>.wav). Without them the voice track is silent."""
import array, json, math, os, random, wave

random.seed(3)


def build(N, SR):
    duck = array.array('f', [1.0]) * N
    vl = array.array('f', [0.0]) * N
    vr = array.array('f', [0.0]) * N
    jpath, vdir = os.environ.get('VO_JSON'), os.environ.get('VO_DIR')
    if not jpath or not vdir or not os.path.exists(jpath):
        return duck, vl, vr
    doc = json.load(open(jpath, encoding='utf-8-sig'))
    for line in doc['lines']:
        wpath = os.path.join(vdir, 'vo_%s.wav' % line['id'])
        if not os.path.exists(wpath): continue
        with wave.open(wpath, 'rb') as w:
            src_sr = w.getframerate(); pcm = array.array('h'); pcm.frombytes(w.readframes(w.getnframes()))
        x = [s / 32768.0 for s in pcm]
        # resample to SR by linear interpolation
        ratio = SR / float(src_sr); n_out = int(len(x) * ratio)
        y = [0.0] * n_out
        for i in range(n_out):
            p = i / ratio; j = int(p); f = p - j
            a = x[j] if j < len(x) else 0.0; b = x[j + 1] if j + 1 < len(x) else a
            y[i] = a + (b - a) * f
        # trim leading / trailing silence of the synthesiser
        thr = 0.01
        s0 = next((i for i, v in enumerate(y) if abs(v) > thr), 0); s1 = len(y) - next((i for i, v in enumerate(reversed(y)) if abs(v) > thr), 0)
        y = y[max(0, s0 - 200):min(len(y), s1 + 400)]
        fx = line.get('fx', 'room'); voice = line.get('voice', 'david')
        if fx == 'radio':
            hp = lp = lp2 = 0.0; out = []
            for v in y:
                lp += 0.32 * (v - lp); band = lp - hp; hp += 0.045 * (lp - hp)          # roughly 350 Hz - 2.7 kHz
                lp2 += 0.6 * (band - lp2)
                out.append(math.tanh(lp2 * 3.2) * 0.62 + random.uniform(-1, 1) * 0.012)
            click = [random.uniform(-1, 1) * 0.25 * math.exp(-i / 250.0) for i in range(1100)]
            y = click + out + click[:700]
            gain = 1.15
        else:
            d1, d2, d3 = int(0.021 * SR), int(0.043 * SR), int(0.071 * SR)
            out = y + [0.0] * d3
            for i, v in enumerate(y):
                out[i + d1] += v * 0.20; out[i + d2] += v * 0.11; out[i + d3] += v * 0.06
            y = out; gain = 1.35
        pan = {'david': -0.12, 'zira': 0.12}.get(voice, 0.0)
        a = (pan + 1) * math.pi / 4; gl, gr = gain * math.cos(a) * 1.41, gain * math.sin(a) * 1.41
        t0 = int(line['t'] * SR)
        for i, v in enumerate(y):
            j = t0 + i
            if j >= N: break
            vl[j] += v * gl; vr[j] += v * gr
        # duck the music under the line
        fade = int(0.18 * SR); i0 = t0; i1 = min(N, t0 + len(y))
        for j in range(max(0, i0 - fade), min(N, i1 + fade)):
            if j < i0: k = (j - (i0 - fade)) / fade; lvl = 1 - 0.62 * k * k * (3 - 2 * k)
            elif j > i1: k = (j - i1) / fade; lvl = 0.38 + 0.62 * k * k * (3 - 2 * k)
            else: lvl = 0.38
            if lvl < duck[j]: duck[j] = lvl
    return duck, vl, vr
