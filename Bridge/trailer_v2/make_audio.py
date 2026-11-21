"""Thriller score + dialogue mix for CITY_DRIVER_UNDERCOVER_trailer_thriller. Pure ffmpeg (lavfi) driven from Python; no extra packages.
Usage: python -I make_audio.py  -> writes thriller_mix.wav (stereo 44.1 kHz, 62.73 s)."""
import json, subprocess, os, wave, array

HERE = os.path.dirname(os.path.abspath(__file__))
VO_DIR = os.path.join(HERE, '..', 'trailer_tools', 'vo_classic')
DIALOGUE = os.path.join(HERE, '..', 'trailer_tools', 'dialogue_classic.json')
TOTAL = 62.7333
E = 46.7333          # ending starts (frame 1402)
BOOM = E + 1.4       # logo slam
SOON = E + 7.4       # coming soon frame
FOLLOW = E + 11.4    # follow frame

def lead(path):
    with wave.open(path, 'rb') as w:
        sr = w.getframerate(); pcm = array.array('h'); pcm.frombytes(w.readframes(w.getnframes()))
    for i, v in enumerate(pcm):
        if abs(v) > 330: return max(0.0, i / sr - 0.01)
    return 0.0


lines = json.load(open(DIALOGUE, encoding='utf-8-sig'))['lines']
inputs = []   # (ffmpeg input args)
count = [0]
chains = []   # filter chains
labels = []   # music labels to mix


def add_src(expr, dur, label, post, delay=0.0, vol=1.0):
    idx = count[0]; count[0] += 1
    inputs.extend(['-f', 'lavfi', '-t', '%.3f' % dur, '-i', expr])
    d = int(delay * 1000)
    chains.append('[%d:a]%s,adelay=%d|%d,volume=%.3f,apad=whole_dur=%.3f,atrim=0:%.3f[%s]' % (idx, post, d, d, vol, TOTAL, TOTAL, label))
    labels.append(label)


def ae(expr, dur):  # mono aevalsrc, 44.1 kHz
    return "aevalsrc='%s':s=44100:d=%.3f" % (expr, dur)

# 1. dark drone (A1 + beating partial + fifth), swelling over the whole trailer
add_src(ae('0.55*sin(2*PI*55*t)+0.4*sin(2*PI*55.7*t)+0.28*sin(2*PI*82.4*t+0.5*sin(2*PI*0.17*t))', TOTAL), TOTAL, 'drone',
        "lowpass=f=260,volume='min(1,0.35+0.65*t/46)':eval=frame,afade=t=out:st=%.2f:d=3.2" % (TOTAL - 3.2), vol=0.55)
# 2. dissonant pad (minor second + tritone): thriller tension
add_src(ae('0.10*sin(2*PI*110*t)+0.09*sin(2*PI*116.5*t)+0.07*sin(2*PI*155.6*t)+0.05*sin(2*PI*220*t)', TOTAL), TOTAL, 'pad',
        "lowpass=f=700,tremolo=f=0.22:d=0.45,afade=t=in:st=0:d=8,afade=t=out:st=%.2f:d=3.2" % (TOTAL - 3.2), vol=0.9)
# 3. heartbeat (lub-dub every 0.9 s), stops with the riser
HB = E + 1.35
add_src(ae('0.95*sin(2*PI*50*mod(t,0.9))*exp(-9*mod(t,0.9))+0.65*sin(2*PI*45*mod(t+0.64,0.9))*exp(-10*mod(t+0.64,0.9))', HB), HB, 'heart',
        "lowpass=f=140,afade=t=in:st=0:d=6,volume='0.5+0.5*min(1,t/40)':eval=frame,afade=t=out:st=%.2f:d=0.4" % (HB - 0.4), vol=0.8)
# 4. high ticking clock from 18 s (nervous eighth notes), getting louder
add_src(ae('0.5*sin(2*PI*1500*mod(t,0.45))*exp(-70*mod(t,0.45))', E - 18 + 1.2), E - 18 + 1.2, 'tick',
        "highpass=f=800,volume='0.15+0.85*min(1,t/26)':eval=frame,afade=t=in:st=0:d=3,afade=t=out:st=%.2f:d=0.3" % (E - 18 + 0.9), delay=18, vol=0.10)
# 5. engine / road rumble under the drive (filtered noise)
add_src('anoisesrc=d=%.3f:c=brown:r=44100:a=0.8' % (E - 2), E - 2, 'rumble', "lowpass=f=180,afade=t=in:st=0:d=4,afade=t=out:st=%.2f:d=2.5" % (E - 4.5), delay=0, vol=0.35)
# 6. pre-boom riser: noise + rising tone
add_src('anoisesrc=d=1.5:c=white:r=44100:a=0.7', 1.5, 'rnoise', "highpass=f=1400,afade=t=in:st=0:d=1.4:curve=exp", delay=E, vol=0.55)
add_src(ae('0.4*sin(2*PI*(300*t+400*t*t*t))', 1.5), 1.5, 'rtone', "afade=t=in:st=0:d=1.4:curve=exp", delay=E, vol=0.35)
# 7. the big boom at the logo reveal (sub drop + impact noise + metallic ring)
add_src(ae('1.0*sin(2*PI*(36+70*exp(-5*t))*t)*exp(-1.1*t)', 4.5), 4.5, 'boomsub', 'lowpass=f=160', delay=BOOM, vol=1.0)
add_src(ae('(random(0)*2-1)*exp(-4.5*t)', 2.0), 2.0, 'boomnoise', 'lowpass=f=900,highpass=f=60', delay=BOOM, vol=0.8)
add_src(ae('0.5*sin(2*PI*220*t)*exp(-2.2*t)+0.3*sin(2*PI*331*t)*exp(-2.6*t)+0.2*sin(2*PI*553*t)*exp(-3*t)', 3.5), 3.5, 'boomring', 'lowpass=f=2500', delay=BOOM, vol=0.45)
# 8. lower, softer hits for the Coming Soon and Follow frames
add_src(ae('1.0*sin(2*PI*(40+40*exp(-6*t))*t)*exp(-1.6*t)', 3.0), 3.0, 'hit2', 'lowpass=f=150', delay=SOON, vol=0.7)
add_src(ae('0.5*sin(2*PI*330*t)*exp(-2.5*t)+0.25*sin(2*PI*495*t)*exp(-3*t)', 2.5), 2.5, 'ring2', 'lowpass=f=2200', delay=SOON, vol=0.25)
add_src(ae('1.0*sin(2*PI*(42+30*exp(-6*t))*t)*exp(-2*t)', 2.5), 2.5, 'hit3', 'lowpass=f=140', delay=FOLLOW, vol=0.45)

# music bus
chains.append('%samix=inputs=%d:normalize=0,alimiter=limit=0.9[music0]' % (''.join('[%s]' % l for l in labels), len(labels)))

# dialogue bus
vlabels = []
for ln in lines:
    wav = os.path.join(VO_DIR, 'vo_%s.wav' % ln['id'])
    idx = count[0]; count[0] += 1
    inputs.extend(['-i', wav])
    d = int(ln['t'] * 1000)
    if ln.get('fx') == 'radio':
        fx = 'highpass=f=380,lowpass=f=2800,acompressor=threshold=0.05:ratio=6,volume=2.2'
    else:
        fx = 'highpass=f=90,aecho=0.8:0.6:35|70:0.22|0.1,volume=1.7'
    lab = 'v%s' % ln['id']
    chains.append('[%d:a]aresample=44100,atrim=start=%.3f,asetpts=PTS-STARTPTS,%s,pan=stereo|c0=c0|c1=c0,adelay=%d|%d,apad=pad_dur=70[%s]' % (idx, lead(wav), fx, d, d, lab))
    vlabels.append(lab)
chains.append('%samix=inputs=%d:normalize=0[vo0]' % (''.join('[%s]' % l for l in vlabels), len(vlabels)))
chains.append('[vo0]asplit=3[vo][vosc][vot]')
# duck music under the dialogue, then mix, give the tail a fade
chains.append('[music0]pan=stereo|c0=c0|c1=c0,asplit=2[ms][mt]')
chains.append('[ms][vosc]sidechaincompress=threshold=0.02:ratio=6:attack=40:release=500[duck]')
chains.append('[duck][vo]amix=inputs=2:normalize=0,alimiter=limit=0.95,afade=t=out:st=%.2f:d=2.5[out]' % (TOTAL - 2.5))

opts = ['-ar', '44100', '-ac', '2', '-c:a', 'pcm_s16le']
cmd = ['ffmpeg', '-loglevel', 'error', '-y'] + inputs + ['-filter_complex', ';'.join(chains)]
for lab, fn in (('[out]', 'thriller_mix.wav'), ('[mt]', 'dbg_music.wav'), ('[vot]', 'dbg_vo.wav')):
    cmd += ['-map', lab, '-t', '%.3f' % TOTAL] + opts + [os.path.join(HERE, fn)]
subprocess.run(cmd, check=True)
print('ok')
