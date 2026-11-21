"""Trailer score v2: follows Bridge/trailer_timeline.json (cuts, hits, sections, titles) written by the recorder.
Pure Python (no dependencies). Original synthesised music + sound design: day (warm plucks, pad, birds), dusk (heartbeat, riser),
night (rain, taiko, bass pulse, ostinato, sirens), climax (fast cuts with whooshes, slow-motion dives), the day/night flicker
stingers, a dropout and the two title hits. Output: trailer_score.wav (44.1 kHz stereo 16-bit)."""
import array, json, math, random, sys, wave

SR = 44100
TAU = 2 * math.pi
sin, exp, cos = math.sin, math.exp, math.cos
random.seed(5)

tl = json.load(open(sys.argv[1] if len(sys.argv) > 1 else 'trailer_timeline.json'))
FPS = tl['fps']; ev = tl['events']
DUR = tl['frames'] / FPS + 1.2
N = int(SR * DUR)


def bus():
    return array.array('f', [0.0]) * N, array.array('f', [0.0]) * N
ML, MR = bus(); FL, FR = bus(); AL, AR = bus()                      # music, fx, ambience
duck = array.array('f', [1.0]) * N                                  # gain on the music bus (slow-motion dips, dropout)


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
sec = {}
cuts = []; titles = {}
for e in ev:
    if e['kind'] == 'section': sec[e['tag']] = e['t']
    elif e['kind'] == 'cut': cuts.append((e['t'], e['s'], e['tag']))
    elif e['kind'] == 'title': titles[e['tag']] = e['t']
    elif e['kind'] == 'end': END = e['t']
t_day = 2.7; t_dusk = sec['dusk']; t_night = sec['night']; t_climax = sec['climax']; t_fin = sec['finale']
t_title_day = titles['CITY DRIVER']; t_title_night = titles['UNDERCOVER']
print('sections', sec, 'titles', titles, 'dur', round(DUR, 1)); sys.stdout.flush()

# ------------------------------------------------------------------------------------------------ ambience
print('ambience'); sys.stdout.flush()
st = [0.0]
def air(t):
    st[0] += 0.02 * (random.uniform(-1, 1) - st[0]); return st[0] * 0.55
add((AL, AR), 0, t_dusk + 2, lambda t: air(t) * smooth(t / 2.0) * smooth((t_dusk + 2 - t) / 2.0) * 0.35, 1.0)
for k in range(14):                                                 # birds in the morning
    t0 = 3.0 + random.random() * (t_dusk - 6.0); f0 = 3200 + random.random() * 1800; pan = random.uniform(-0.8, 0.8)
    for chirp in range(random.randint(2, 4)):
        def bird(t, f0=f0):
            f = f0 * (1 + 0.35 * sin(TAU * 9 * t)) + 600 * t
            return sin(TAU * f * t) * exp(-t * 22) * 0.018
        add((AL, AR), t0 + chirp * 0.13, 0.14, bird, 1.0, pan)
rst = [0.0, 0.0]
def rain(t):
    x = random.uniform(-1, 1); rst[0] += 0.28 * (x - rst[0]); rst[1] += 0.03 * (rst[0] - rst[1]); return (rst[0] - rst[1]) * 0.45
add((AL, AR), t_night - 0.2, t_fin - t_night + 2.0, lambda t: rain(t) * smooth(t / 0.6) * smooth((t_fin - t_night + 2.0 - t) / 1.5) * 0.30, 1.0)
for t0 in (t_night + 3.0, t_night + 8.5, t_night + 13.0):           # distant thunder
    tst = [0.0]
    def thunder(t, tst=tst):
        tst[0] += 0.006 * (random.uniform(-1, 1) - tst[0]); return tst[0] * 14 * exp(-t * 0.9) * smooth(t / 0.4)
    add((AL, AR), t0, 4.0, thunder, 0.5, random.uniform(-0.5, 0.5))

# ------------------------------------------------------------------------------------------------ day music: pad + plucks + bass
print('day music'); sys.stdout.flush()
DAY = [(50, 57, 61, 66), (47, 54, 59, 62), (43, 50, 54, 59), (45, 52, 56, 61)]     # Dmaj7-ish / Bm / Gmaj / A, midi (low voicings)
SEGD = 5.2
t = 0.5; k = 0
while t < t_dusk + 2.5:
    ch = DAY[k % 4]
    for nt in ch:
        f = midi(nt); ph = random.random() * TAU
        def pad(tt, f=f, ph=ph):
            a = smooth(tt / 1.8) * smooth((SEGD + 1.6 - tt) / 1.8)
            return a * (sin(TAU * f * tt + ph) + 0.35 * sin(TAU * f * 2.003 * tt) + 0.12 * sin(TAU * f * 3 * tt)) * 0.030
        add((ML, MR), t, SEGD + 1.6, pad, 1.0, random.uniform(-0.5, 0.5))
    t += SEGD; k += 1
# piano-like plucks (arpeggio), 8ths at 78 bpm, fade in with the first shot
step = 60.0 / 78 / 2
t = 3.0; k = 0; ARP = [0, 1, 2, 3, 2, 1, 3, 2]
while t < t_dusk - 0.1:
    ch = DAY[int((t - 0.5) // SEGD) % 4]
    nt = ch[ARP[k % 8] % 4] + 24
    f = midi(nt)
    def pluck(tt, f=f):
        return (sin(TAU * f * tt) + 0.4 * sin(TAU * f * 2 * tt) * exp(-tt * 6) + 0.15 * sin(TAU * f * 3 * tt) * exp(-tt * 9)) * exp(-tt * 3.6) * 0.115
    add((ML, MR), t, 1.4, pluck, smooth((t - 3.0) / 3.0), (k % 5 - 2) * 0.25)
    t += step; k += 1
t = 5.0
while t < t_dusk:                                                   # soft low note every 2 bars
    f = midi(DAY[int((t - 0.5) // SEGD) % 4][0] - 12)
    add((ML, MR), t, 3.0, lambda tt, f=f: sin(TAU * f * tt) * smooth(tt / 0.3) * exp(-tt * 0.9) * 0.20, 1.0)
    t += SEGD

# ------------------------------------------------------------------------------------------------ dusk: heartbeat + riser into the night
print('dusk'); sys.stdout.flush()
def kick(b, t0, vol, lowf=42.0, decay=7.0):
    def f(t): return sin(TAU * (lowf * t + 95 * (1 - exp(-t * 32)) / 32)) * exp(-t * decay) * vol
    add(b, t0, 0.6, f, 1.0)
def snare(b, t0, vol):
    add(b, t0, 0.4, lambda t: (random.uniform(-1, 1) * exp(-t * 15) * 0.8 + sin(TAU * 185 * t) * exp(-t * 19) * 0.5) * vol, 1.0, 0.1)
def hat(b, t0, vol, pan=0.0):
    pv = [0.0]
    def f(t): x = random.uniform(-1, 1); y = x - pv[0]; pv[0] = x; return y * exp(-t * 75) * vol
    add(b, t0, 0.09, f, 1.0, pan)
def swell(b, a, bb, gain=0.6, rising=True):
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

hb = t_dusk + 0.5
while hb < t_night - 0.3:                                           # heartbeat: lub-dub, accelerating
    kick((FL, FR), hb, 0.30, 52, 9); kick((FL, FR), hb + 0.19, 0.22, 52, 9)
    hb += max(0.55, 1.15 - 0.6 * (hb - t_dusk) / (t_night - t_dusk))
swell((FL, FR), t_night - 3.2, t_night - 0.02, 0.55)

# ------------------------------------------------------------------------------------------------ night: bass pulse, ostinato, drums
print('night music'); sys.stdout.flush()
NIGHT_PAT = [38, 38, 41, 38, 45, 38, 41, 36]
def bass(t0, note, vol):
    f = midi(note)
    def b(t):
        e = exp(-t * 6.5)
        return (sum(sin(TAU * f * h * t) / h * exp(-t * (5 + 5 * h)) for h in range(1, 7)) * 1.4 + sin(TAU * f * t) * 0.6) * e * vol
    add((ML, MR), t0, 0.3, b, 1.0)
def osti(t0, note, vol, pan):
    f = midi(note)
    add((ML, MR), t0, 0.16, lambda t: (sin(TAU * f * t) + 0.5 * sin(TAU * f * 2 * t) + 0.25 * sin(TAU * f * 3 * t)) * exp(-t * 20) * vol, 1.0, pan)
bpm = 104; step = 60.0 / bpm / 2
t = t_night; k = 0
while t < t_fin - 0.25:
    clim = t >= t_climax
    if clim and k % 1 == 0: pass
    vol = 0.13 if not clim else 0.17
    bass(t, NIGHT_PAT[k % 8], vol)
    if k % 2 == 1: osti(t, (74, 69, 65, 69)[(k // 2) % 4], 0.045 if not clim else 0.06, (k % 4 - 1.5) * 0.4)
    t += step if not clim else step * 0.78; k += 1
# drums
b = t_night; beat = 0
while b < t_fin - 0.3:
    clim = b >= t_climax; d = 0.5 if not clim else 0.39
    kick((FL, FR), b, 0.5 if not clim else 0.62)
    if beat % 2 == 1: snare((FL, FR), b, 0.26 if not clim else 0.34)
    sub = 4 if not clim else 8
    for q in range(sub): hat((FL, FR), b + q * d / sub, 0.065 if q % 2 == 0 else 0.04, -0.4 if q % 2 else 0.4)
    if clim and beat % 4 == 3: swell((FL, FR), b - 0.6, b + 0.01, 0.35)
    b += d; beat += 1
# strings pad in the night (D minor), swelling toward the climax
for nt, pan in ((38, 0), (50, -0.3), (57, 0.3), (62, -0.5), (65, 0.5)):
    f = midi(nt)
    def sp(t, f=f):
        d = t_fin - t_night + 1.0; v = sum(sin(TAU * f * h * t * (1 + 0.002 * sin(TAU * 4.5 * t))) / (h * h) for h in (1, 2, 3, 4))
        return v * 0.030 * smooth(t / 2.5) * smooth((d - t) / 1.5) * (0.5 + 0.6 * smooth((t + t_night - t_climax + 6) / 10))
    add((ML, MR), t_night, t_fin - t_night + 1.0, sp, 1.0, pan)

# ------------------------------------------------------------------------------------------------ sound design on cuts
print('cuts and hits'); sys.stdout.flush()
for (tc, s, tag) in cuts:
    if tc < 3.0: continue
    if s >= 0.9: boom((FL, FR), tc, 0.8 if tag.startswith('night match') else 0.5 * s)
    elif s >= 0.4 and tc >= t_night: kick((FL, FR), tc, 0.55 * s)
    if tc >= t_night: whoosh((FL, FR), max(0.0, tc - 0.16), 0.2, 0.09 + 0.05 * s, random.uniform(-0.5, 0.5))
    if tc >= t_climax and tc < t_fin and not tag.startswith('slow'): hat((FL, FR), tc, 0.14)
    if 'slow' in tag or 'wheel' in tag:
        add((FL, FR), tc, 1.6, lambda t: sin(TAU * (420 * exp(-t * 1.6) + 70) * t) * exp(-t * 1.4) * 0.16, 1.0)
        set_duck(tc + 0.1, tc + 2.2, 0.45, 0.25)
# sirens and tyre noise while the chasers are in shot
for (tc, s, tag) in cuts:
    if 'chaser' in tag:
        ph = [0.0]
        def siren(t, ph=ph):
            f = 860 + 150 * sin(TAU * 0.9 * t); ph[0] += TAU * f / SR
            return sin(ph[0]) * 0.05 * smooth(t / 0.4) * smooth((2.2 - t) / 0.5)
        add((FL, FR), tc, 2.2, siren, 1.0, -0.5 if 'cam 2' not in tag else 0.5)
        sc = [0.0]
        add((FL, FR), tc + 0.5, 1.0, lambda t, sc=sc: (sc.__setitem__(0, sc[0] + 0.5 * (random.uniform(-1, 1) - sc[0])), sc[0] * 0.10 * smooth(t / 0.1) * exp(-t * 3.0))[1], 1.0)
def engine(b, a, dur, f0, f1, vol):
    def e(t):
        u = t / dur; f = f0 + (f1 - f0) * u
        return sum(sin(TAU * f * h * t) / h for h in (1, 2, 3)) * (0.7 + 0.3 * sin(TAU * 7 * t)) * vol * smooth(t / 0.5) * smooth((dur - t) / 0.5)
    add(b, a, dur, e, 1.0)
engine((AL, AR), 3.2, t_dusk - 3.2, 52, 88, 0.025)
engine((AL, AR), t_night, t_fin - t_night, 70, 125, 0.035)

# ------------------------------------------------------------------------------------------------ flicker stingers, dropout, titles
print('finale'); sys.stdout.flush()
flick = [(tc, tag) for (tc, s, tag) in cuts if tag.startswith('flicker')]
for tc, tag in flick:
    if 'night' in tag:
        kick((FL, FR), tc, 0.8, 38, 6); add((FL, FR), tc, 0.5, lambda t: sin(TAU * 55 * t) * exp(-t * 7) * 0.4, 1.0)
    else:
        f = midi(88)
        add((FL, FR), tc, 0.9, lambda t, f=f: (sin(TAU * f * t) + 0.4 * sin(TAU * f * 2.76 * t)) * exp(-t * 5) * 0.09, 1.0, 0.2)
if flick: swell((FL, FR), flick[0][0] - 0.6, flick[-1][0] + 0.3, 0.55)
t_drop = t_title_night - 0.40
set_duck(t_drop, t_title_night - 0.02, 0.0, 0.05)                       # one beat of silence before the night title
# day title: bright major swell; night title: huge hit + dark chord; final: shimmer pad + tail
for nt, pan in ((50, -0.3), (57, 0.3), (62, -0.5), (66, 0.5), (69, 0.0), (74, 0.2)):
    f = midi(nt)
    END_ = tl['frames'] / FPS - t_title_day + 1.2
    add((ML, MR), t_title_day, END_,
        lambda t, f=f: sum(sin(TAU * f * (1 + 0.003 * sin(TAU * 5 * t)) * h * t) / h for h in (1, 2, 3, 4)) * 0.05 * smooth(t / 1.0) * smooth((END_ - t) / 2.4), 1.0, pan)
boom((FL, FR), t_title_night, 1.0, 4.5)
add((FL, FR), t_title_night, 3.0, lambda t: sum(sin(TAU * midi(n) * t) for n in (38, 41, 45)) * exp(-t * 0.9) * 0.10, 1.0)
for k in range(12):                                                    # shimmer
    f0 = midi(86 + random.choice([0, 3, 7, 12])); t0 = t_title_night + 0.4 + random.random() * 2.2
    add((FL, FR), t0, 1.6, lambda t, f0=f0: sin(TAU * f0 * t) * exp(-t * 2.2) * 0.028, 1.0, random.uniform(-0.8, 0.8))

# ------------------------------------------------------------------------------------------------ mix: reverb on music + fx, then master
print('reverb + master'); sys.stdout.flush()
def apply_duck(L, R):
    for i in range(N):
        d = duck[i]; L[i] *= d; R[i] *= d
apply_duck(ML, MR)
SL = array.array('f', [a + b for a, b in zip(ML, FL)]); SR_ = array.array('f', [a + b for a, b in zip(MR, FR)])


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
L2 = reverb(SL, (1557, 1617, 1491, 1422, 1277, 1356, 2113), 0.84, 0.95)
R2 = reverb(SR_, (1580, 1640, 1517, 1447, 1297, 1379, 2137), 0.84, 0.95)
for i in range(N): L2[i] += AL[i]; R2[i] += AR[i]
import vo_mix
VD, VL, VR = vo_mix.build(N, SR)
peak = max(max(abs(x) for x in L2), max(abs(x) for x in R2)); scale = 0.9 / peak
fi = int(0.5 * SR); fo = int(1.4 * SR)
data = array.array('h', [0]) * (2 * N)
for i in range(N):
    g = scale
    if i < fi: g *= i / fi
    if i > N - fo: g *= (N - i) / fo
    data[2 * i] = int(max(-1.0, min(1.0, L2[i] * g * VD[i] + VL[i])) * 32767); data[2 * i + 1] = int(max(-1.0, min(1.0, R2[i] * g * VD[i] + VR[i])) * 32767)
out = sys.argv[2] if len(sys.argv) > 2 else 'trailer_score.wav'
with wave.open(out, 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
print('wrote', out, round(N / SR, 2), 's')
