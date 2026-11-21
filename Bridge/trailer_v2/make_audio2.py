"""Hollywood-style trailer score for CITY_DRIVER_UNDERCOVER_trailer_thriller (v2). Pure ffmpeg/lavfi driven from Python.
Timeline is the MAIN timeline (0 = first frame of the game footage); the intro card (INTRO s) is added at the very end by delaying the whole mix.
Acts: I awe (0-11.3) / II drive + intrigue (11.3-27.5) / III dread (27.5-32.9, silence gap, braam) / IV night chase (32.9-43.2) /
dropout (43.2-46.7) / V logo, coming soon, follow (46.7-62.7).  Output: thriller_mix2.wav"""
import json, subprocess, os, wave, array

HERE = os.path.dirname(os.path.abspath(__file__))
VO_DIR = os.path.join(HERE, '..', 'trailer_tools', 'vo_classic')
DIALOGUE = os.path.join(HERE, '..', 'trailer_tools', 'dialogue_classic.json')
TM = 62.7333          # main-timeline length (46.7333 game footage + 16 s ending)
INTRO = 4.0           # intro card length
E = 46.7333; BOOM = E + 1.4; SOON = E + 7.4; FOLLOW = E + 11.4
GAP0, GAP1 = 32.30, 32.93      # silence before the night braam
DROP0, DROP1 = 43.2, 46.6      # music dropout after "Who are you?"

inputs = []; chains = []; count = [0]
A = []   # music that is gated (silence gap + dropout)
B = []   # music that always plays (drone, heartbeat, risers, logo section)


def src(name, expr, start, dur, post='anull', vol=1.0, grp=B, noise=None):
    idx = count[0]; count[0] += 1
    if noise:
        inputs.extend(['-f', 'lavfi', '-t', '%.3f' % dur, '-i', 'anoisesrc=d=%.3f:c=%s:r=44100:a=0.8' % (dur, noise)])
    else:
        inputs.extend(['-f', 'lavfi', '-t', '%.3f' % dur, '-i', "aevalsrc='%s':s=44100:d=%.3f" % (expr, dur)])
    d = int(start * 1000)
    chains.append('[%d:a]%s,adelay=%d|%d,volume=%.3f,apad=whole_dur=%.3f,atrim=0:%.3f[%s]' % (idx, post, d, d, vol, TM, TM, name))
    grp.append(name)


def fio(dur, fi, fo, curve='tri'):
    return 'afade=t=in:st=0:d=%.2f:curve=%s,afade=t=out:st=%.2f:d=%.2f:curve=%s' % (fi, curve, max(0, dur - fo), fo, curve)


REV = 'aecho=0.8:0.88:70|140|260:0.35|0.25|0.18'

# ================= ACT I : awe (0 - 11.3), no drums, space =================
d = 11.6
src('pad1', '0.10*sin(2*PI*110*t)+0.08*sin(2*PI*164.8*t)+0.07*sin(2*PI*220*t+0.4*sin(2*PI*0.2*t))+0.05*sin(2*PI*261.6*t)+0.035*sin(2*PI*329.6*t)',
    0, TM - 15.0 - 0.0, 'lowpass=f=900,tremolo=f=0.18:d=0.35,' + fio(TM - 15.0, 4.0, 1.2, 'qsin'), 1.0, A)  # Am add9, runs to ~47.7 then handled by gates
# sparse piano / bell notes, reverb soaked (A4 E5 C5 B4 E5 A4)
for i, (t0, f) in enumerate([(1.0, 440), (3.0, 659.3), (4.6, 523.3), (6.4, 493.9), (8.0, 659.3), (9.7, 440)]):
    src('note%d' % i, '0.5*sin(2*PI*%.1f*t)*exp(-2.6*t)+0.18*sin(2*PI*%.1f*t)*exp(-4*t)+0.06*sin(2*PI*%.1f*t)*exp(-6*t)' % (f, 2 * f, 3 * f), t0, 4.0,
        'lowpass=f=5000,' + REV, 0.55, A)
# soft text hits for "ONE CITY." and "A THOUSAND SECRETS." (reverse swish + low thump)
for i, t0 in enumerate([2.4, 6.6]):
    src('sw%d' % i, '', t0 - 0.9, 0.9, 'bandpass=f=1800:w=2,afade=t=in:st=0:d=0.9:curve=exp', 0.28, A, noise='white')
    src('th%d' % i, '0.9*sin(2*PI*(42+40*exp(-9*t))*t)*exp(-2.4*t)', t0, 2.5, 'lowpass=f=140', 0.8, A)
src('wind', '', 0, 12.0, 'bandpass=f=420:w=1.4,tremolo=f=0.13:d=0.6,' + fio(12.0, 3, 2), 0.10, A, noise='pink')

# ================= ACT II : drive + intrigue (11.3 - 27.5) =================
P = 0.3  # 100 bpm eighth notes
dur2 = 27.5 - 11.3
src('bass2', '(0.8*sin(2*PI*if(lt(mod(floor(t/0.3),8),6),55,if(lt(mod(floor(t/0.3),8),7),65.4,49))*mod(t,0.3))*exp(-7*mod(t,0.3)))'
    '+0.25*sin(2*PI*2*if(lt(mod(floor(t/0.3),8),6),55,if(lt(mod(floor(t/0.3),8),7),65.4,49))*mod(t,0.3))*exp(-11*mod(t,0.3))', 11.3, dur2,
    "lowpass=f=420,volume='0.25+0.75*min(1,t/12)':eval=frame," + fio(dur2, 1.5, 0.3), 0.9, A)
src('kick2', '0.9*sin(2*PI*(46+70*exp(-25*mod(t,0.6)))*mod(t,0.6))*exp(-7*mod(t,0.6))', 14.4, 27.5 - 14.4,
    "lowpass=f=160,volume='0.2+0.8*min(1,t/10)':eval=frame," + fio(27.5 - 14.4, 1.0, 0.2), 0.7, A)
src('tick2', '0.5*sin(2*PI*1700*mod(t,0.3))*exp(-80*mod(t,0.3))', 18.0, 32.3 - 18.0,
    "highpass=f=900,volume='0.2+0.8*min(1,t/14)':eval=frame," + fio(32.3 - 18.0, 2.0, 0.1), 0.10, A)
# dissonant string layer creeping in (minor 2nd + tritone)
src('str2', '0.07*sin(2*PI*220*t)+0.065*sin(2*PI*233.1*t)+0.05*sin(2*PI*311.1*t)+0.04*sin(2*PI*440*t)', 17.0, 32.3 - 17.0,
    'lowpass=f=1300,tremolo=f=5.5:d=0.35,' + REV + ',' + fio(32.3 - 17.0, 6.0, 0.15), 0.9, A)
# riser into dusk, impact at 27.5
src('ris1n', '', 24.3, 3.2, 'highpass=f=1200,afade=t=in:st=0:d=3.2:curve=exp', 0.45, A, noise='white')
src('ris1t', '0.35*sin(2*PI*(220*t+55*t*t*t/3.2))', 24.3, 3.2, 'afade=t=in:st=0:d=3.2:curve=exp', 0.35, A)
src('imp1', '1.0*sin(2*PI*(38+60*exp(-6*t))*t)*exp(-1.8*t)', 27.5, 3.0, 'lowpass=f=150', 0.85, A)
src('imp1n', '(random(0)*2-1)*exp(-7*t)', 27.5, 1.2, 'lowpass=f=1200', 0.5, A)

# ================= ACT III : dread (27.5 - 32.3) =================
src('pad3', '0.12*sin(2*PI*87.3*t)+0.10*sin(2*PI*103.8*t)+0.08*sin(2*PI*130.8*t)+0.07*sin(2*PI*174.6*t)+0.05*sin(2*PI*277.2*t)', 27.5, 32.3 - 27.5 + 0.2,
    'lowpass=f=700,tremolo=f=0.3:d=0.4,' + fio(32.3 - 27.5 + 0.2, 0.4, 0.2), 1.0, A)
src('bass3', '(0.85*sin(2*PI*if(lt(mod(floor(t/0.45),4),3),43.65,51.9)*mod(t,0.45))*exp(-5*mod(t,0.45)))', 27.5, 32.3 - 27.5,
    'lowpass=f=300,' + fio(32.3 - 27.5, 0.2, 0.1), 0.85, A)
src('ris2n', '', 29.7, 2.6, 'highpass=f=1500,afade=t=in:st=0:d=2.6:curve=exp,afade=t=out:st=2.5:d=0.1', 0.45, A, noise='white')

# ================= ACT IV : night chase (32.93 - 43.2) =================
# the big braam at the cut to night (brass cluster + sub + metal)
src('braam', '(0.5*sin(2*PI*55*t)+0.35*sin(2*PI*110*t)+0.28*sin(2*PI*165*t)+0.22*sin(2*PI*220*t)+0.16*sin(2*PI*275*t)+0.12*sin(2*PI*330*t)'
    '+0.45*sin(2*PI*77.8*t)+0.30*sin(2*PI*155.6*t)+0.2*sin(2*PI*233.4*t)+0.35*sin(2*PI*82.4*t)+0.2*sin(2*PI*164.8*t))*min(1,t/0.06)*exp(-0.5*t)',
    GAP1, 6.5, 'lowpass=f=1400,' + REV, 0.9, B)
src('braamsub', '1.0*sin(2*PI*(34+80*exp(-5*t))*t)*exp(-0.9*t)', GAP1, 5.0, 'lowpass=f=150', 1.0, B)
src('braamn', '(random(0)*2-1)*exp(-3*t)', GAP1, 2.5, 'lowpass=f=1500,highpass=f=70', 0.9, B)
src('braamcym', '', GAP1 - 0.01, 3.5, 'highpass=f=4500,afade=t=in:st=0:d=0.02,afade=t=out:st=0.05:d=3.4:curve=exp', 0.22, B, noise='white')
dchase = DROP0 - GAP1
src('taiko', '(sin(2*PI*(52+95*exp(-28*mod(t,0.6)))*mod(t,0.6))*exp(-5.5*mod(t,0.6))*(1+0.5*lt(mod(floor(t/0.6),2),1))'
    '+(random(0)*2-1)*exp(-70*mod(t,0.6))*0.35)', GAP1, dchase,
    "lowpass=f=700,volume='0.65+0.35*min(1,t/8)':eval=frame," + fio(dchase, 0.1, 0.25), 0.95, A)
src('taikogh', '0.55*sin(2*PI*(62+70*exp(-30*mod(t,0.6)))*mod(t,0.6))*exp(-9*mod(t,0.6))', GAP1 + 0.3, dchase - 0.3,
    'lowpass=f=500,' + fio(dchase - 0.3, 0.1, 0.25), 0.5, A)
src('bass4', '(0.9*sin(2*PI*if(lt(mod(floor(t/0.25),8),5),55,if(lt(mod(floor(t/0.25),8),7),65.4,73.4))*mod(t,0.25))*exp(-6*mod(t,0.25)))'
    '+0.3*sin(2*PI*2*if(lt(mod(floor(t/0.25),8),5),55,if(lt(mod(floor(t/0.25),8),7),65.4,73.4))*mod(t,0.25))*exp(-10*mod(t,0.25))', GAP1 + 0.6, dchase - 0.6,
    'lowpass=f=450,' + fio(dchase - 0.6, 0.1, 0.25), 0.8, A)
src('hat4', '(random(0)*2-1)*exp(-90*mod(t,0.125))', 34.5, DROP0 - 34.5, "highpass=f=6000,volume='0.3+0.7*min(1,t/7)':eval=frame," + fio(DROP0 - 34.5, 0.2, 0.2), 0.10, A)
src('pad4', '0.11*sin(2*PI*110*t)+0.10*sin(2*PI*116.5*t)+0.08*sin(2*PI*164.8*t)+0.07*sin(2*PI*220*t)+0.05*sin(2*PI*311.1*t)', GAP1, dchase,
    'lowpass=f=1100,tremolo=f=6:d=0.3,' + fio(dchase, 0.5, 0.25), 0.8, A)
# stingers on the two key dialogue beats and a riser into the dropout
for i, t0 in enumerate([37.6, 40.0]):
    src('sting%d' % i, '1.0*sin(2*PI*(40+70*exp(-7*t))*t)*exp(-2.2*t)', t0, 2.4, 'lowpass=f=160', 0.8, A)
    src('stingn%d' % i, '(random(0)*2-1)*exp(-8*t)', t0, 1.0, 'lowpass=f=2500', 0.5, A)
src('ris3n', '', 40.4, 2.8, 'highpass=f=1000,afade=t=in:st=0:d=2.8:curve=exp,afade=t=out:st=2.7:d=0.1', 0.4, A, noise='white')
src('ris3t', '0.35*sin(2*PI*(330*t+110*t*t*t/2.8))', 40.4, 2.8, 'afade=t=in:st=0:d=2.8:curve=exp,afade=t=out:st=2.7:d=0.1', 0.30, A)

# ================= always-on layers =================
src('drone', '0.55*sin(2*PI*55*t)+0.4*sin(2*PI*55.7*t)+0.28*sin(2*PI*82.4*t+0.5*sin(2*PI*0.17*t))', 0, TM,
    "lowpass=f=260,volume='0.18+0.82*min(1,t/40)':eval=frame,afade=t=in:st=0:d=5,afade=t=out:st=%.2f:d=3.2" % (TM - 3.2), 0.5, B)
HB = E + 1.35
src('heart', '0.95*sin(2*PI*50*mod(t,0.8))*exp(-9*mod(t,0.8))+0.65*sin(2*PI*45*mod(t+0.56,0.8))*exp(-10*mod(t+0.56,0.8))', 27.5, HB - 27.5,
    'lowpass=f=140,afade=t=in:st=0:d=3,afade=t=out:st=%.2f:d=0.4' % (HB - 27.5 - 0.4), 0.7, B)
# late riser: tonal shimmer swells through the dropout into the logo, then the classic noise rise
src('shim', '0.10*sin(2*PI*880*t)+0.08*sin(2*PI*1244.5*t)+0.06*sin(2*PI*1760*t)', 44.0, E + 1.4 - 44.0,
    'tremolo=f=7:d=0.5,' + REV + ",afade=t=in:st=0:d=%.2f:curve=exp,afade=t=out:st=%.2f:d=0.05" % (E + 1.4 - 44.0 - 0.05, E + 1.4 - 44.0 - 0.05), 0.55, B)
src('rn', '', E, 1.5, 'highpass=f=1400,afade=t=in:st=0:d=1.4:curve=exp,afade=t=out:st=1.4:d=0.05', 0.55, B, noise='white')
src('rt', '0.4*sin(2*PI*(300*t+400*t*t*t))', E, 1.5, 'afade=t=in:st=0:d=1.4:curve=exp,afade=t=out:st=1.4:d=0.05', 0.35, B)

# ================= ACT V : logo boom, coming soon, follow =================
src('boomsub', '1.0*sin(2*PI*(36+70*exp(-5*t))*t)*exp(-1.1*t)', BOOM, 4.5, 'lowpass=f=160', 1.0, B)
src('boomn', '(random(0)*2-1)*exp(-4.5*t)', BOOM, 2.0, 'lowpass=f=900,highpass=f=60', 0.8, B)
src('boomring', '0.5*sin(2*PI*220*t)*exp(-2.2*t)+0.3*sin(2*PI*331*t)*exp(-2.6*t)+0.2*sin(2*PI*553*t)*exp(-3*t)', BOOM, 3.5, 'lowpass=f=2500', 0.45, B)
src('boomcym', '', BOOM - 0.01, 3.0, 'highpass=f=4000,afade=t=in:st=0:d=0.02,afade=t=out:st=0.04:d=2.9:curve=exp', 0.20, B, noise='white')
# hopeful-dark sustained Am pad under the logo and the end cards
dend = TM - BOOM
src('padend', '0.10*sin(2*PI*110*t)+0.09*sin(2*PI*164.8*t)+0.08*sin(2*PI*220*t)+0.06*sin(2*PI*261.6*t)+0.05*sin(2*PI*329.6*t)', BOOM, dend,
    'lowpass=f=1000,tremolo=f=0.2:d=0.3,' + REV + ',' + fio(dend, 1.0, 3.0), 0.9, B)
src('hit2', '1.0*sin(2*PI*(40+40*exp(-6*t))*t)*exp(-1.6*t)', SOON, 3.0, 'lowpass=f=150', 0.7, B)
# rising Am motif (A4 C5 E5) + low "dun" on the Coming Soon frame
for i, (t0, f) in enumerate([(SOON + 0.35, 440), (SOON + 0.95, 523.3), (SOON + 1.55, 659.3)]):
    src('mot%d' % i, '0.5*sin(2*PI*%.1f*t)*exp(-2.2*t)+0.2*sin(2*PI*%.1f*t)*exp(-3.5*t)' % (f, 2 * f), t0, 3.5, 'lowpass=f=5000,' + REV, 0.5, B)
src('dun', '1.0*sin(2*PI*(45+25*exp(-6*t))*t)*exp(-1.5*t)', SOON + 2.6, 2.5, 'lowpass=f=140', 0.6, B)
src('hit3', '1.0*sin(2*PI*(42+30*exp(-6*t))*t)*exp(-2*t)', FOLLOW, 2.5, 'lowpass=f=140', 0.45, B)

# ================= mix buses =================
chains.append('%samix=inputs=%d:normalize=0[busA]' % (''.join('[%s]' % l for l in A), len(A)))
env = "if(lt(t,11.3),0.40,if(lt(t,27.5),0.40+0.32*(t-11.3)/16.2,if(lt(t,32.4),0.78,1.0)))"
gate = "volume='(%s)*if(between(t,%.2f,%.2f),0.0,if(between(t,%.2f,%.2f),0.0,1))*0.62':eval=frame" % (env, GAP0 + 0.04, GAP1 - 0.01, DROP0, DROP1)
chains.append('[busA]%s[busAg]' % gate)
chains.append('%samix=inputs=%d:normalize=0[busB0]' % (''.join('[%s]' % l for l in B), len(B)))
chains.append("[busB0]volume='if(between(t,%.2f,%.2f),0.0,1)*0.62':eval=frame[busB]" % (GAP0 + 0.04, GAP1 - 0.02))
chains.append('[busAg][busB]amix=inputs=2:normalize=0,alimiter=limit=0.9,pan=stereo|c0=c0|c1=c0[ms]')


# dialogue bus (same treatment as before)
def lead(path):
    with wave.open(path, 'rb') as w:
        sr = w.getframerate(); pcm = array.array('h'); pcm.frombytes(w.readframes(w.getnframes()))
    for i, v in enumerate(pcm):
        if abs(v) > 330: return max(0.0, i / sr - 0.01)
    return 0.0


lines = json.load(open(DIALOGUE, encoding='utf-8-sig'))['lines']
vl = []
for ln in lines:
    wav = os.path.join(VO_DIR, 'vo_%s.wav' % ln['id'])
    idx = count[0]; count[0] += 1
    inputs.extend(['-i', wav])
    dly = int(ln['t'] * 1000)
    if ln.get('fx') == 'radio':
        fx = 'highpass=f=380,lowpass=f=2800,acompressor=threshold=0.05:ratio=6,volume=2.2'
    else:
        fx = 'highpass=f=90,aecho=0.8:0.6:35|70:0.22|0.1,volume=1.7'
    lab = 'v' + ln['id']
    chains.append('[%d:a]aresample=44100,atrim=start=%.3f,asetpts=PTS-STARTPTS,%s,pan=stereo|c0=c0|c1=c0,adelay=%d|%d,apad=pad_dur=70[%s]' % (idx, lead(wav), fx, dly, dly, lab))
    vl.append(lab)
chains.append('%samix=inputs=%d:normalize=0[vo0]' % (''.join('[%s]' % l for l in vl), len(vl)))
chains.append('[vo0]asplit=2[vo][vosc]')
chains.append('[ms][vosc]sidechaincompress=threshold=0.02:ratio=6:attack=40:release=500[duck]')
chains.append('[duck][vo]amix=inputs=2:normalize=0,alimiter=limit=0.95,afade=t=out:st=%.2f:d=2.5[main]' % (TM - 2.5))

# intro sound (0 - INTRO): rising swell, logo whoosh + shimmer chimes, soft landing
iw = INTRO * 1000
idx = count[0]; count[0] += 1
inputs.extend(['-f', 'lavfi', '-t', '%.3f' % INTRO, '-i', "aevalsrc='0.14*sin(2*PI*110*t)+0.12*sin(2*PI*164.8*t)+0.09*sin(2*PI*220*t)+0.07*sin(2*PI*329.6*t)':s=44100:d=%.3f" % INTRO])
chains.append('[%d:a]lowpass=f=1100,afade=t=in:st=0:d=1.8:curve=qsin,afade=t=out:st=2.6:d=1.4,volume=0.8[i1]' % idx)
idx = count[0]; count[0] += 1
inputs.extend(['-f', 'lavfi', '-t', '1.2', '-i', 'anoisesrc=d=1.2:c=white:r=44100:a=0.7'])
chains.append('[%d:a]bandpass=f=2500:w=2,afade=t=in:st=0:d=1.1:curve=exp,afade=t=out:st=1.1:d=0.1,volume=0.35,adelay=300|300,apad=whole_dur=%.3f,atrim=0:%.3f[i2]' % (idx, INTRO, INTRO))
chime = []
for k, (t0, f) in enumerate([(1.45, 880), (1.62, 1318.5), (1.8, 1760), (2.05, 2637)]):
    idx = count[0]; count[0] += 1
    inputs.extend(['-f', 'lavfi', '-t', '2.0', '-i', "aevalsrc='0.5*sin(2*PI*%.1f*t)*exp(-3*t)+0.2*sin(2*PI*%.1f*t)*exp(-5*t)':s=44100:d=2.0" % (f, 2.01 * f)])
    dly = int(t0 * 1000)
    chains.append('[%d:a]%s,adelay=%d|%d,volume=0.30,apad=whole_dur=%.3f,atrim=0:%.3f[c%d]' % (idx, REV, dly, dly, INTRO, INTRO, k))
    chime.append('[c%d]' % k)
idx = count[0]; count[0] += 1
inputs.extend(['-f', 'lavfi', '-t', '2.5', '-i', "aevalsrc='1.0*sin(2*PI*(40+40*exp(-6*t))*t)*exp(-2*t)':s=44100:d=2.5"])
chains.append('[%d:a]lowpass=f=150,adelay=1400|1400,volume=0.55,apad=whole_dur=%.3f,atrim=0:%.3f[i3]' % (idx, INTRO, INTRO))
chains.append('[i1][i2]%s[i3]amix=inputs=%d:normalize=0,pan=stereo|c0=c0|c1=c0,apad=whole_dur=%.3f,atrim=0:%.3f[intro]' % (''.join(chime), 3 + len(chime) + 0, INTRO, INTRO))
chains.append('[main]adelay=%d|%d[maind]' % (iw, iw))
chains.append('[intro][maind]amix=inputs=2:normalize=0,alimiter=limit=0.95[out]')

cmd = ['ffmpeg', '-loglevel', 'error', '-y'] + inputs + ['-filter_complex', ';'.join(chains), '-map', '[out]', '-t', '%.3f' % (TM + INTRO), '-ar', '44100', '-ac', '2', '-c:a', 'pcm_s16le',
                                                          os.path.join(HERE, 'thriller_mix2.wav')]
subprocess.run(cmd, check=True)
print('ok', len(A), len(B))
