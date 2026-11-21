"""Overview trailer score: a clean, peaceful, classical first part (piano arpeggio over a D major canon-style progression, a flute line,
strings, bells, birds), a silence, then a thriller part for the Shadow Pass (heartbeat, ticking, staccato string ostinato, tremolo cluster,
taiko, sirens, rain, hits on the cuts), then peace again for the title. Follows trailer_timeline_overview.json. Original, synthesised in code.
Pure standard library. Usage: python make_score3.py trailer_timeline_overview.json out.wav   (env VO_JSON / VO_DIR add the spoken lines)."""
import array, json, math, random, sys, wave
import vo_mix

SR = 44100
TAU = 2 * math.pi
sin, exp, cos = math.sin, math.exp, math.cos
random.seed(9)

tl = json.load(open(sys.argv[1] if len(sys.argv) > 1 else 'trailer_timeline_overview.json'))
FPS = tl['fps']; ev = tl['events']
DUR = tl['frames'] / FPS + 1.2
N = int(SR * DUR)


def bus(): return array.array('f', [0.0]) * N, array.array('f', [0.0]) * N
ML, MR = bus(); FL, FR = bus(); AL, AR = bus()
M, F, A = (ML, MR), (FL, FR), (AL, AR)
duck = array.array('f', [1.0]) * N


def smooth(x):
    x = 0.0 if x < 0 else 1.0 if x > 1 else x
    return x * x * (3 - 2 * x)


def add(b, start, dur, fn, gain=1.0, pan=0.0):
    L, R = b
    i0 = int(start * SR); n = int(dur * SR)
    a = (pan + 1) * math.pi / 4; gl = gain * cos(a); gr = gain * sin(a)
    for i in range(n):
        j = i0 + i
        if j >= N: break
        if j < 0: continue
        s = fn(i / SR)
        L[j] += s * gl; R[j] += s * gr


def midi(n): return 440.0 * 2 ** ((n - 69) / 12.0)


def set_duck(a, b, level, fade=0.15):
    i0 = int(a * SR); i1 = int(b * SR); f = int(fade * SR)
    for i in range(max(0, i0 - f), min(N, i1 + f)):
        if i < i0: k = (i - (i0 - f)) / f; v = 1 + (level - 1) * smooth(k)
        elif i > i1: k = (i - i1) / f; v = level + (1 - level) * smooth(k)
        else: v = level
        duck[i] = min(duck[i], v)


# ------------------------------------------------------------------------------------------------ timeline
sec = {}; cuts = []; titles = {}
for e in ev:
    if e['kind'] == 'section': sec[e['tag']] = e['t']
    elif e['kind'] == 'cut': cuts.append((e['t'], e['s'], e['tag']))
    elif e['kind'] == 'title': titles[e['tag']] = e['t']
    elif e['kind'] == 'end': END = e['t']
t_calm = 2.7; t_dusk = sec['dusk']; t_thr = sec['thriller']; t_climax = sec['climax']; t_fin = sec['finale']
t_black = next(t for (t, s, tag) in cuts if tag == 'black beat')
t_title = titles['CITY DRIVER']
print('calm', t_calm, 'dusk', t_dusk, 'black', round(t_black, 2), 'thriller', round(t_thr, 2), 'climax', round(t_climax, 2), 'finale', round(t_fin, 2), 'title', round(t_title, 2), 'end', round(END, 2)); sys.stdout.flush()


# ------------------------------------------------------------------------------------------------ instruments
def piano(t0, n, vol, pan=0.0, dur=3.2):
    f = midi(n)
    def p(t):
        return (sin(TAU * f * t) + 0.5 * sin(TAU * 2 * f * t) * exp(-t * 3.2) + 0.22 * sin(TAU * 3 * f * t) * exp(-t * 5.5) + 0.1 * sin(TAU * 4.01 * f * t) * exp(-t * 9)) * exp(-t * 1.5) * smooth(t / 0.004) * vol
    add(M, t0, dur, p, 1.0, pan)


def flute(t0, n, dur, vol, pan=0.0):
    f = midi(n); st = [0.0]
    def p(t):
        st[0] += 0.2 * (random.uniform(-1, 1) - st[0]); vib = 1 + 0.004 * sin(TAU * 5.2 * t) * smooth(t / 0.8)
        return (sin(TAU * f * vib * t) + 0.18 * sin(TAU * 2 * f * vib * t) + st[0] * 0.04) * smooth(t / 0.3) * smooth((dur - t) / 0.7) * vol
    add(M, t0, dur, p, 1.0, pan)


def strings(t0, notes, dur, vol, attack=1.4, release=1.4):
    for k, n in enumerate(notes):
        f = midi(n); ph = random.random() * TAU; pan = (k - 1) * 0.35
        def p(t, f=f, ph=ph):
            v = sum(sin(TAU * f * (1 + 0.0025 * sin(TAU * 4.7 * t)) * h * t + ph * h) / (h ** 1.4) for h in (1, 2, 3, 4))
            return v * smooth(t / attack) * smooth((dur - t) / release) * vol
        add(M, t0, dur, p, 1.0, pan)


def cello(t0, n, dur, vol):
    f = midi(n)
    add(M, t0, dur, lambda t: (sin(TAU * f * t) + 0.5 * sin(TAU * 2 * f * t) + 0.25 * sin(TAU * 3 * f * t)) * smooth(t / 0.25) * smooth((dur - t) / 1.0) * vol, 1.0)


def bell(t0, n, vol, pan=0.0):
    f = midi(n)
    add(M, t0, 3.0, lambda t: (sin(TAU * f * t) + 0.35 * sin(TAU * f * 2.76 * t) * exp(-t * 3)) * exp(-t * 1.9) * vol, 1.0, pan)


def kick(b, t0, vol, lowf=42.0, decay=7.0):
    add(b, t0, 0.6, lambda t: sin(TAU * (lowf * t + 95 * (1 - exp(-t * 32)) / 32)) * exp(-t * decay) * vol, 1.0)


def snare(b, t0, vol):
    add(b, t0, 0.4, lambda t: (random.uniform(-1, 1) * exp(-t * 15) * 0.8 + sin(TAU * 185 * t) * exp(-t * 19) * 0.5) * vol, 1.0, 0.1)


def hat(b, t0, vol, pan=0.0):
    pv = [0.0]
    def f(t): x = random.uniform(-1, 1); y = x - pv[0]; pv[0] = x; return y * exp(-t * 75) * vol
    add(b, t0, 0.09, f, 1.0, pan)


def tick(b, t0, vol, pan=0.0):
    add(b, t0, 0.06, lambda t: (sin(TAU * 1750 * t) * exp(-t * 150) + random.uniform(-1, 1) * exp(-t * 220) * 0.4) * vol, 1.0, pan)


def swell(b, a, bb, gain=0.6):
    d = bb - a; s = [0.0]
    def f(t):
        u = min(1.0, t / d); al = 0.015 + 0.6 * u * u
        s[0] += al * (random.uniform(-1, 1) - s[0])
        return (s[0] * 0.6 + sin(TAU * (160 + 1900 * u * u) * t) * 0.18) * (u ** 2.0) * gain
    add(b, a, d + 0.02, f, 1.0)


def whoosh(b, t0, dur, vol, pan=0.0):
    s = [0.0]
    def f(t):
        u = t / dur; al = 0.05 + 0.5 * sin(math.pi * u)
        s[0] += al * (random.uniform(-1, 1) - s[0]); return s[0] * sin(math.pi * u) * vol
    add(b, t0, dur, f, 1.0, pan)


def boom(b, t0, vol, tail=3.4):
    def f(t):
        ph = TAU * (27 * t + 75 * (1 - exp(-t * 3.6)) / 3.6)
        brass = sum(sin(TAU * 73.4 * h * t + 0.2 * h) / h for h in (1, 2, 3, 4, 5, 6, 7))
        return vol * (0.62 * sin(ph) * exp(-t * 1.4) + 0.16 * brass * exp(-t * 2.0) * smooth(t / 0.03) + random.uniform(-1, 1) * 0.4 * exp(-t * 6))
    add(b, t0, tail, f, 1.0)


# ------------------------------------------------------------------------------------------------ ambience
print('ambience'); sys.stdout.flush()
st = [0.0]
def air(t): st[0] += 0.02 * (random.uniform(-1, 1) - st[0]); return st[0] * 0.55
add(A, 0, t_dusk + 2, lambda t: air(t) * smooth(t / 2.0) * smooth((t_dusk + 2 - t) / 2.0) * 0.30, 1.0)
for k in range(16):
    t0 = 3.0 + random.random() * (t_dusk - 8.0); f0 = 3200 + random.random() * 1800; pan = random.uniform(-0.8, 0.8)
    for chirp in range(random.randint(2, 4)):
        add(A, t0 + chirp * 0.13, 0.14, lambda t, f0=f0: sin(TAU * (f0 * (1 + 0.35 * sin(TAU * 9 * t)) + 600 * t) * t) * exp(-t * 22) * 0.016, 1.0, pan)
rst = [0.0, 0.0]
def rain(t):
    x = random.uniform(-1, 1); rst[0] += 0.28 * (x - rst[0]); rst[1] += 0.03 * (rst[0] - rst[1]); return (rst[0] - rst[1]) * 0.45
add(A, t_thr - 0.2, t_fin - t_thr + 1.0, lambda t: rain(t) * smooth(t / 0.5) * smooth((t_fin - t_thr + 1.0 - t) / 0.6) * 0.30, 1.0)
for t0 in (t_thr + 5.0, t_thr + 12.5, t_thr + 18.0):
    tst = [0.0]
    def thunder(t, tst=tst):
        tst[0] += 0.006 * (random.uniform(-1, 1) - tst[0]); return tst[0] * 14 * exp(-t * 0.9) * smooth(t / 0.4)
    add(A, t0, 4.0, thunder, 0.5, random.uniform(-0.5, 0.5))
# birds again when peace returns
for k in range(7):
    t0 = t_fin + 0.8 + random.random() * (END - t_fin - 3.0); f0 = 3200 + random.random() * 1800; pan = random.uniform(-0.8, 0.8)
    for chirp in range(random.randint(2, 3)):
        add(A, t0 + chirp * 0.13, 0.14, lambda t, f0=f0: sin(TAU * (f0 * (1 + 0.35 * sin(TAU * 9 * t)) + 600 * t) * t) * exp(-t * 22) * 0.012, 1.0, pan)

# ------------------------------------------------------------------------------------------------ PART ONE: peaceful, classical
print('calm piano, flute, strings'); sys.stdout.flush()
CH = [(38, (62, 66, 69)), (45, (61, 64, 69)), (47, (62, 66, 71)), (42, (61, 66, 69)), (43, (59, 62, 67)), (38, (62, 66, 69)), (43, (59, 62, 67)), (45, (61, 64, 69))]
MEL = [78, 76, 74, 73, 71, 69, 71, 73]
CHD = 4.0


def minorize(notes): return tuple(n - 1 if n % 12 in (6, 1) else n for n in notes)


def play_progression(start, end, major=True, fade_in=True, melody_from=2):
    i = 0
    while True:
        cs = start + i * CHD
        if cs >= end - 0.5: break
        root, tri = CH[i % 8]
        dusk = cs >= t_dusk and major
        tri = minorize(tri) if dusk else tri
        grow = 0.45 + 0.55 * smooth((cs - start) / 14.0) if fade_in else 1.0
        # piano arpeggio
        pat = [tri[0], tri[1], tri[2], tri[1] + 12, tri[2], tri[1], tri[0] + 12, tri[1]]
        step = 0.5 if not dusk else 1.0
        for k in range(int(CHD / step)):
            tt = cs + k * step
            if tt >= end - 0.2: break
            piano(tt, pat[k % 8], 0.085 * grow, (k % 4 - 1.5) * 0.18)
        piano(cs, root + 12, 0.07 * grow, -0.3, 4.0)
        cello(cs, root, CHD - 0.1, 0.16 * grow * (0.7 if dusk else 1.0))
        if i >= 1: strings(cs - 0.3, tri, CHD + 0.9, 0.034 * grow)
        if i >= melody_from and not dusk and cs + CHD <= end + 0.5: flute(cs + 0.1, MEL[i % 8], CHD - 0.3, 0.075 * grow, 0.15)
        if i >= 3 and not dusk: bell(cs + 0.02, tri[2] + 24, 0.034, 0.4)
        i += 1


play_progression(t_calm + 0.3, t_black - 0.6)
# dusk: low drone and a faint high shimmer that grows
add(M, t_dusk - 2.0, t_black - t_dusk + 2.2, lambda t: (sin(TAU * 36.71 * t) * 0.8 + sin(TAU * 73.4 * t) * 0.3) * smooth(t / 5.0) * smooth((t_black - t_dusk + 2.2 - t) / 0.8) * 0.17, 1.0)
add(M, t_dusk - 1.0, t_black - t_dusk + 1.0, lambda t: sin(TAU * 1244.5 * t) * (0.5 + 0.5 * sin(TAU * 6 * t)) * smooth(t / 5.0) * smooth((t_black - t_dusk + 1.0 - t) / 0.8) * 0.022, 1.0, 0.3)
swell(M, t_black - 1.6, t_black - 0.05, 0.25)
# silence: the black beat, with only a heartbeat and the first ticks
set_duck(t_black - 0.9, t_thr - 0.02, 0.0, 0.5)
for k in range(3): tick(F, t_black + 0.05 + k * 0.28, 0.18, -0.3 if k % 2 else 0.3)
kick(F, t_black + 0.20, 0.30, 52, 9); kick(F, t_black + 0.40, 0.22, 52, 9)

# ------------------------------------------------------------------------------------------------ PART TWO: Shadow Pass (thriller)
print('thriller'); sys.stdout.flush()
bpm = 112; beat = 60.0 / bpm; half = beat / 2
boom(F, t_thr, 1.0, 4.2)
add(M, t_thr, t_fin - t_thr, lambda t: (sin(TAU * 36.71 * t) * 0.9 + sin(TAU * 73.4 * t + 0.4 * sin(TAU * 0.13 * t)) * 0.35) * smooth(t / 1.2) * smooth((t_fin - t_thr - t) / 0.15) * 0.15, 1.0)
PAT = [38, 38, 38, 41, 38, 38, 43, 41]


def bass8(t0, n, vol):
    f = midi(n)
    add(M, t0, 0.3, lambda t: (sum(sin(TAU * f * h * t) / h * exp(-t * (5 + 5 * h)) for h in range(1, 7)) * 1.4 + sin(TAU * f * t) * 0.6) * exp(-t * 6.5) * vol, 1.0)


def stac(t0, n, vol, pan):
    f = midi(n)
    add(M, t0, 0.16, lambda t: (sin(TAU * f * t) + 0.55 * sin(TAU * 2 * f * t) + 0.3 * sin(TAU * 3 * f * t)) * exp(-t * 20) * smooth(t / 0.004) * vol, 1.0, pan)


t = t_thr + 0.4; k = 0
while t < t_fin - 0.15:
    clim = t >= t_climax
    bass8(t, PAT[k % 8], 0.15 if not clim else 0.19)
    if t > t_thr + 2.0:
        stac(t + 0.134, (62, 65, 69, 65)[k % 4] + (12 if clim and k % 8 > 3 else 0), 0.040 if not clim else 0.055, ((k % 4) - 1.5) * 0.4)
    t += half if not clim else half * 0.85; k += 1
b = t_thr; bi = 0
while b < t_fin - 0.2:
    clim = b >= t_climax; d = beat if not clim else beat * 0.78
    kick(F, b, 0.5 if not clim else 0.64, 42, 7)
    if clim and bi % 2 == 1: snare(F, b, 0.30)
    if b > t_thr + 3.0:
        for q in range(2 if not clim else 4): hat(F, b + q * d / (2 if not clim else 4), 0.055, -0.4 if q % 2 else 0.4)
    b += d; bi += 1
# tremolo strings with a dissonant cluster (D6 / Eb6 / A5) that grows
for nt, pan in ((86, -0.4), (87, 0.4), (81, 0.0)):
    f = midi(nt)
    def trem(t, f=f):
        d = t_fin - t_thr - 5.0
        return sin(TAU * f * t) * (0.55 + 0.45 * sin(TAU * 11 * t)) * smooth(t / 3.0) * smooth((d - t) / 0.2) * (0.012 + 0.04 * smooth(t / (d * 0.8)))
    add(M, t_thr + 5.0, t_fin - t_thr - 5.0, trem, 1.0, pan)
swell(F, t_climax - 2.6, t_climax - 0.02, 0.6)
# cuts, hits, sirens, slow-motion dives
for (tc, s, tag) in cuts:
    if tc < t_thr - 0.05 or tc > t_fin + 0.3: continue
    if s >= 0.9 and tc > t_thr + 0.5: boom(F, tc, 0.8 * s)
    elif s >= 0.4: kick(F, tc, 0.5 * s)
    whoosh(F, max(0.0, tc - 0.16), 0.2, 0.09 + 0.05 * s, random.uniform(-0.5, 0.5))
    if 'slow' in tag:
        add(F, tc, 1.6, lambda t: sin(TAU * (420 * exp(-t * 1.6) + 70) * t) * exp(-t * 1.4) * 0.16, 1.0)
        set_duck(tc + 0.1, tc + 2.2, 0.5, 0.25)
    if 'chaser' in tag:
        ph = [0.0]
        def siren(t, ph=ph):
            f = 860 + 150 * sin(TAU * 0.9 * t); ph[0] += TAU * f / SR
            return sin(ph[0]) * 0.05 * smooth(t / 0.4) * smooth((2.2 - t) / 0.5)
        add(F, tc, 2.2, siren, 1.0, -0.5 if 'cam 2' not in tag else 0.5)
for tt in [t_thr + 0.9 + 0.5 * i for i in range(int((t_fin - t_thr) / 0.5))]:     # constant ticking under the chase
    if tt < t_fin - 0.3: tick(F, tt, 0.06, 0.4 if int(tt * 2) % 2 else -0.4)
# the thriller stops dead; one breath of silence, then peace
set_duck(t_fin - 0.12, t_fin + 0.28, 0.0, 0.05)

# ------------------------------------------------------------------------------------------------ PART THREE: peace returns
print('peace returns + title'); sys.stdout.flush()
play_progression(t_fin + 0.3, END + 0.8, major=True, fade_in=True, melody_from=0)
for k, n in enumerate((62, 66, 69, 73, 76, 78, 81, 85)):                                  # gentle harp-like rise when the title lands
    piano(t_title + 0.02 + k * 0.07, n + 12 if k > 4 else n, 0.09, -0.5 + k * 0.14, 4.5)
strings(t_title - 0.5, (50, 57, 62, 66, 69, 73), END - t_title + 1.8, 0.038, 1.2, 2.5)
bell(t_title + 0.6, 90, 0.04, 0.3); bell(t_title + 1.1, 93, 0.03, -0.3)

# ------------------------------------------------------------------------------------------------ mix
print('reverb + master'); sys.stdout.flush()
for i in range(N): ML[i] *= duck[i]; MR[i] *= duck[i]
SL = array.array('f', [a + b for a, b in zip(ML, FL)]); SRr = array.array('f', [a + b for a, b in zip(MR, FR)])


def reverb(buf, delays, fb, mix, damp=0.3):
    wet = array.array('f', [0.0]) * N
    for d in delays:
        line = array.array('f', [0.0]) * d; idx = 0; lp = 0.0
        for i in range(N):
            y = line[idx]; lp = y * (1 - damp) + lp * damp
            line[idx] = buf[i] + lp * fb
            wet[i] += y; idx += 1
            if idx == d: idx = 0
    g = mix / len(delays)
    return array.array('f', [buf[i] + wet[i] * g for i in range(N)])


L2 = reverb(SL, (1557, 1617, 1491, 1422, 1277, 1356, 2113), 0.86, 1.05)
R2 = reverb(SRr, (1580, 1640, 1517, 1447, 1297, 1379, 2137), 0.86, 1.05)
for i in range(N): L2[i] += AL[i]; R2[i] += AR[i]
VD, VL, VR = vo_mix.build(N, SR)
peak = max(max(abs(x) for x in L2), max(abs(x) for x in R2)); scale = 0.88 / peak
fi = int(0.6 * SR); fo = int(1.6 * SR)
data = array.array('h', [0]) * (2 * N)
for i in range(N):
    g = scale
    if i < fi: g *= i / fi
    if i > N - fo: g *= (N - i) / fo
    data[2 * i] = int(max(-1.0, min(1.0, L2[i] * g * VD[i] + VL[i])) * 32767); data[2 * i + 1] = int(max(-1.0, min(1.0, R2[i] * g * VD[i] + VR[i])) * 32767)
out = sys.argv[2] if len(sys.argv) > 2 else 'trailer_score_overview.wav'
with wave.open(out, 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
print('wrote', out, round(N / SR, 2), 's')
