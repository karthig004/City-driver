"""Hollywood-style score for the 114 s trailer v3, built on the real shot timings (Bridge/trailer_v3_timeline.json) and the resolved dialogue
(Bridge/dialogue_v3_resolved.json). Pure ffmpeg/lavfi driven from Python. Output: thriller_mix3.wav (intro card + film + ending cards).
Acts: I awe (0-9.5) / II drive + intrigue (9.5-39.9) / III dusk dread (39.9-51.7) / silence gap + night braam (52.3) / IV night chase (52.3-73.3) /
mini drop-out (73.3-75.0) / re-entry + riser / V climax quick cuts (79.97-90.9) / flicker stutters / final drop-out / logo boom."""
import json, subprocess, os, wave, array

HERE = os.path.dirname(os.path.abspath(__file__))
BR = os.path.join(HERE, '..')
VO_DIR = os.path.join(BR, 'trailer_tools', 'vo_v3')
DIALOGUE = os.path.join(BR, 'dialogue_v3_resolved.json')
E = 94.5667                  # footage length (2837 frames); ending cards start here
TM = E + 16.0
INTRO = 4.0
BOOM = E + 1.4; SOON = E + 7.4; FOLLOW = E + 11.4
GAP0, GAP1 = 51.65, 52.30            # silence before the night braam
MID0, MID1 = 73.30, 75.00            # short drop-out after "Who are you?"
DROP0, DROP1 = 90.90, 94.40          # final drop-out before the logo

inputs = []; chains = []; count = [0]
A = []; B = []


def src(name, expr, start, dur, post='anull', vol=1.0, grp=None, noise=None):
    grp = B if grp is None else grp
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
AMIN = '0.10*sin(2*PI*110*t)+0.08*sin(2*PI*164.8*t)+0.07*sin(2*PI*220*t+0.4*sin(2*PI*0.2*t))+0.05*sin(2*PI*261.6*t)+0.035*sin(2*PI*329.6*t)'
FMIN = '0.12*sin(2*PI*87.3*t)+0.10*sin(2*PI*103.8*t)+0.08*sin(2*PI*130.8*t)+0.07*sin(2*PI*174.6*t)+0.05*sin(2*PI*277.2*t)'
CLUSTER = ('(0.5*sin(2*PI*55*t)+0.35*sin(2*PI*110*t)+0.28*sin(2*PI*165*t)+0.22*sin(2*PI*220*t)+0.16*sin(2*PI*275*t)+0.12*sin(2*PI*330*t)'
           '+0.45*sin(2*PI*77.8*t)+0.30*sin(2*PI*155.6*t)+0.2*sin(2*PI*233.4*t)+0.35*sin(2*PI*82.4*t)+0.2*sin(2*PI*164.8*t))')


def bass(name, f_a, f_b, f_c, period, start, dur, vol, post):
    e = 'mod(floor(t/%s),8)' % period
    f = 'if(lt(%s,5),%s,if(lt(%s,7),%s,%s))' % (e, f_a, e, f_b, f_c)
    expr = '(0.9*sin(2*PI*%s*mod(t,%s))*exp(-6*mod(t,%s)))+0.3*sin(2*PI*2*%s*mod(t,%s))*exp(-10*mod(t,%s))' % (f, period, period, f, period, period)
    src(name, expr, start, dur, post, vol, A)


def taiko(name, period, start, dur, vol, post):
    m = 'mod(t,%s)' % period
    expr = '(sin(2*PI*(52+95*exp(-28*%s))*%s)*exp(-5.5*%s)*(1+0.5*lt(mod(floor(t/%s),2),1))+(random(0)*2-1)*exp(-70*%s)*0.35)' % (m, m, m, period, m)
    src(name, expr, start, dur, post, vol, A)


# ================= ACT I : awe (0 - 9.5, extended to the first car shots), no drums =================
src('pad1', AMIN, 0, 39.9, 'lowpass=f=900,tremolo=f=0.18:d=0.35,' + fio(39.9, 4.0, 1.0, 'qsin'), 1.0, A)
for i, (t0, f) in enumerate([(1.0, 440), (3.0, 659.3), (4.6, 523.3), (6.4, 493.9), (8.0, 659.3), (9.7, 440), (11.8, 523.3), (13.6, 659.3), (15.5, 440), (17.2, 493.9)]):
    src('note%d' % i, '0.5*sin(2*PI*%.1f*t)*exp(-2.6*t)+0.18*sin(2*PI*%.1f*t)*exp(-4*t)+0.06*sin(2*PI*%.1f*t)*exp(-6*t)' % (f, 2 * f, 3 * f), t0, 4.0, 'lowpass=f=5000,' + REV, 0.55, A)
for i, t0 in enumerate([1.0, 19.0]):   # caption hits: "ONE CITY." and "A THOUSAND SECRETS."
    src('sw%d' % i, '', t0 - 0.9, 0.9, 'bandpass=f=1800:w=2,afade=t=in:st=0:d=0.9:curve=exp', 0.28, A, noise='white')
    src('th%d' % i, '0.9*sin(2*PI*(42+40*exp(-9*t))*t)*exp(-2.4*t)', t0, 2.5, 'lowpass=f=140', 0.8, A)
src('wind', '', 0, 14.0, 'bandpass=f=420:w=1.4,tremolo=f=0.13:d=0.6,' + fio(14.0, 3, 3), 0.10, A, noise='pink')

# ================= ACT II : drive + intrigue (9.5 - 39.9) =================
d2 = 39.9 - 9.5
bass('bass2', '55', '65.4', '49', '0.3', 9.5, d2, 0.9, "lowpass=f=420,volume='0.25+0.75*min(1,t/14)':eval=frame," + fio(d2, 1.5, 0.3))
src('kick2', '0.9*sin(2*PI*(46+70*exp(-25*mod(t,0.6)))*mod(t,0.6))*exp(-7*mod(t,0.6))', 14.4, 39.9 - 14.4,
    "lowpass=f=160,volume='0.2+0.8*min(1,t/14)':eval=frame," + fio(39.9 - 14.4, 1.0, 0.2), 0.7, A)
src('tick2', '0.5*sin(2*PI*1700*mod(t,0.3))*exp(-80*mod(t,0.3))', 20.0, GAP0 - 20.0,
    "highpass=f=900,volume='0.2+0.8*min(1,t/20)':eval=frame," + fio(GAP0 - 20.0, 2.0, 0.1), 0.10, A)
src('str2', '0.07*sin(2*PI*220*t)+0.065*sin(2*PI*233.1*t)+0.05*sin(2*PI*311.1*t)+0.04*sin(2*PI*440*t)', 24.0, GAP0 - 24.0,
    'lowpass=f=1300,tremolo=f=5.5:d=0.35,' + REV + ',' + fio(GAP0 - 24.0, 8.0, 0.15), 0.9, A)
src('ris1n', '', 36.4, 3.5, 'highpass=f=1200,afade=t=in:st=0:d=3.5:curve=exp', 0.45, A, noise='white')
src('ris1t', '0.35*sin(2*PI*(220*t+55*t*t*t/3.5))', 36.4, 3.5, 'afade=t=in:st=0:d=3.5:curve=exp', 0.35, A)
src('imp1', '1.0*sin(2*PI*(38+60*exp(-6*t))*t)*exp(-1.8*t)', 39.9, 3.0, 'lowpass=f=150', 0.85, A)
src('imp1n', '(random(0)*2-1)*exp(-7*t)', 39.9, 1.2, 'lowpass=f=1200', 0.5, A)

# ================= ACT III : dusk dread (39.9 - 51.7) =================
d3 = GAP0 - 39.9
src('pad3', FMIN, 39.9, d3 + 0.2, 'lowpass=f=700,tremolo=f=0.3:d=0.4,' + fio(d3 + 0.2, 0.4, 0.2), 1.0, A)
src('bass3', '(0.85*sin(2*PI*if(lt(mod(floor(t/0.45),4),3),43.65,51.9)*mod(t,0.45))*exp(-5*mod(t,0.45)))', 39.9, d3, 'lowpass=f=300,' + fio(d3, 0.2, 0.1), 0.85, A)
src('ris2n', '', 48.9, GAP0 - 48.9, 'highpass=f=1500,afade=t=in:st=0:d=%.2f:curve=exp,afade=t=out:st=%.2f:d=0.1' % (GAP0 - 48.9, GAP0 - 48.9 - 0.1), 0.45, A, noise='white')

# ================= ACT IV : night chase (52.3 - 73.3) =================
src('braam', CLUSTER + '*min(1,t/0.06)*exp(-0.5*t)', GAP1, 6.5, 'lowpass=f=1400,' + REV, 0.9, B)
src('braamsub', '1.0*sin(2*PI*(34+80*exp(-5*t))*t)*exp(-0.9*t)', GAP1, 5.0, 'lowpass=f=150', 1.0, B)
src('braamn', '(random(0)*2-1)*exp(-3*t)', GAP1, 2.5, 'lowpass=f=1500,highpass=f=70', 0.9, B)
src('braamcym', '', GAP1 - 0.01, 3.5, 'highpass=f=4500,afade=t=in:st=0:d=0.02,afade=t=out:st=0.05:d=3.4:curve=exp', 0.22, B, noise='white')
d4 = MID0 - GAP1
taiko('taiko', '0.6', GAP1, d4, 0.95, "lowpass=f=700,volume='0.65+0.35*min(1,t/10)':eval=frame," + fio(d4, 0.1, 0.25))
src('taikogh', '0.55*sin(2*PI*(62+70*exp(-30*mod(t,0.6)))*mod(t,0.6))*exp(-9*mod(t,0.6))', GAP1 + 0.3, d4 - 0.3, 'lowpass=f=500,' + fio(d4 - 0.3, 0.1, 0.25), 0.5, A)
bass('bass4', '55', '65.4', '73.4', '0.25', GAP1 + 0.6, d4 - 0.6, 0.8, 'lowpass=f=450,' + fio(d4 - 0.6, 0.1, 0.25))
src('hat4', '(random(0)*2-1)*exp(-90*mod(t,0.125))', 58.0, MID0 - 58.0, "highpass=f=6000,volume='0.3+0.7*min(1,t/8)':eval=frame," + fio(MID0 - 58.0, 0.2, 0.2), 0.10, A)
src('pad4', '0.11*sin(2*PI*110*t)+0.10*sin(2*PI*116.5*t)+0.08*sin(2*PI*164.8*t)+0.07*sin(2*PI*220*t)+0.05*sin(2*PI*311.1*t)', GAP1, d4,
    'lowpass=f=1100,tremolo=f=6:d=0.3,' + fio(d4, 0.5, 0.25), 0.8, A)
for i, t0 in enumerate([60.67, 62.97]):     # "Hold on tight." and the second chase cut
    src('sting%d' % i, '1.0*sin(2*PI*(40+70*exp(-7*t))*t)*exp(-2.2*t)', t0, 2.4, 'lowpass=f=160', 0.8, A)
    src('stingn%d' % i, '(random(0)*2-1)*exp(-8*t)', t0, 1.0, 'lowpass=f=2500', 0.5, A)
src('ris3n', '', 69.6, 3.7, 'highpass=f=1000,afade=t=in:st=0:d=3.7:curve=exp,afade=t=out:st=3.6:d=0.1', 0.4, A, noise='white')

# ---- re-entry after the mini drop-out (75.0), riser into the climax (79.97)
src('reent', '1.0*sin(2*PI*(40+70*exp(-7*t))*t)*exp(-2.0*t)', MID1, 2.5, 'lowpass=f=160', 0.9, B)
taiko('taiko2', '0.45', MID1, 79.97 - MID1, 0.9, "lowpass=f=700,volume='0.5+0.5*min(1,t/4)':eval=frame," + fio(79.97 - MID1, 0.1, 0.1))
src('ris4n', '', 76.6, 79.97 - 76.6, 'highpass=f=900,afade=t=in:st=0:d=%.2f:curve=exp,afade=t=out:st=%.2f:d=0.05' % (79.97 - 76.6, 79.97 - 76.6 - 0.05), 0.5, B, noise='white')
src('ris4t', '0.35*sin(2*PI*(300*t+260*t*t*t/3.37))', 76.6, 79.97 - 76.6, 'afade=t=in:st=0:d=3.3:curve=exp,afade=t=out:st=3.3:d=0.05', 0.32, B)

# ================= ACT V : climax quick cuts (79.97 - 90.9), flicker (90.03 - 91.23) =================
CL = 79.97; dcl = DROP0 - CL
src('cl_sub', '1.0*sin(2*PI*(36+70*exp(-5*t))*t)*exp(-1.1*t)', CL, 4.5, 'lowpass=f=160', 1.0, B)
src('cl_n', '(random(0)*2-1)*exp(-4.5*t)', CL, 2.0, 'lowpass=f=1000,highpass=f=60', 0.8, B)
src('cl_cym', '', CL - 0.01, 3.0, 'highpass=f=4500,afade=t=in:st=0:d=0.02,afade=t=out:st=0.04:d=2.9:curve=exp', 0.20, B, noise='white')
taiko('taiko3', '0.30', CL, dcl, 1.0, "lowpass=f=800," + fio(dcl, 0.05, 0.12))
bass('bass5', '55', '65.4', '73.4', '0.20', CL, dcl, 0.85, 'lowpass=f=500,' + fio(dcl, 0.05, 0.12))
src('cl_pad', FMIN + '+0.06*sin(2*PI*220*t)', CL, dcl, 'lowpass=f=1000,tremolo=f=6:d=0.3,' + fio(dcl, 0.3, 0.3), 0.8, A)
src('cl_brass', CLUSTER + '*min(1,t/0.08)*exp(-0.35*t)', 86.7, 4.2, 'lowpass=f=1300,' + REV, 0.6, A)     # slow-motion hero shot
for i, t0 in enumerate([90.03, 90.33, 90.60, 90.80, 90.97, 91.10]):   # day/night flicker stutter
    src('fl%d' % i, '0.9*sin(2*PI*(60+50*exp(-20*t))*t)*exp(-9*t)+(random(0)*2-1)*exp(-40*t)*0.3', t0, 0.6, 'lowpass=f=900', 0.8, B)

# ================= always-on layers =================
src('drone', '0.55*sin(2*PI*55*t)+0.4*sin(2*PI*55.7*t)+0.28*sin(2*PI*82.4*t+0.5*sin(2*PI*0.17*t))', 0, TM,
    "lowpass=f=260,volume='0.18+0.82*min(1,t/60)':eval=frame,afade=t=in:st=0:d=5,afade=t=out:st=%.2f:d=3.2" % (TM - 3.2), 0.5, B)
HB = E + 1.35
src('heart', '0.95*sin(2*PI*50*mod(t,0.8))*exp(-9*mod(t,0.8))+0.65*sin(2*PI*45*mod(t+0.56,0.8))*exp(-10*mod(t+0.56,0.8))', 39.9, HB - 39.9,
    'lowpass=f=140,afade=t=in:st=0:d=3,afade=t=out:st=%.2f:d=0.4' % (HB - 39.9 - 0.4), 0.7, B)
src('shim', '0.10*sin(2*PI*880*t)+0.08*sin(2*PI*1244.5*t)+0.06*sin(2*PI*1760*t)', 91.2, E + 1.4 - 91.2,
    'tremolo=f=7:d=0.5,' + REV + ",afade=t=in:st=0:d=%.2f:curve=exp,afade=t=out:st=%.2f:d=0.05" % (E + 1.4 - 91.2 - 0.05, E + 1.4 - 91.2 - 0.05), 0.55, B)
src('rn', '', E, 1.5, 'highpass=f=1400,afade=t=in:st=0:d=1.4:curve=exp,afade=t=out:st=1.4:d=0.05', 0.55, B, noise='white')
src('rt', '0.4*sin(2*PI*(300*t+400*t*t*t))', E, 1.5, 'afade=t=in:st=0:d=1.4:curve=exp,afade=t=out:st=1.4:d=0.05', 0.35, B)

# ================= logo boom, coming soon, follow =================
src('boomsub', '1.0*sin(2*PI*(36+70*exp(-5*t))*t)*exp(-1.1*t)', BOOM, 4.5, 'lowpass=f=160', 1.0, B)
src('boomn', '(random(0)*2-1)*exp(-4.5*t)', BOOM, 2.0, 'lowpass=f=900,highpass=f=60', 0.8, B)
src('boomring', '0.5*sin(2*PI*220*t)*exp(-2.2*t)+0.3*sin(2*PI*331*t)*exp(-2.6*t)+0.2*sin(2*PI*553*t)*exp(-3*t)', BOOM, 3.5, 'lowpass=f=2500', 0.45, B)
src('boomcym', '', BOOM - 0.01, 3.0, 'highpass=f=4000,afade=t=in:st=0:d=0.02,afade=t=out:st=0.04:d=2.9:curve=exp', 0.20, B, noise='white')
dend = TM - BOOM
src('padend', AMIN.replace('0.4*sin(2*PI*0.2*t)', '0'), BOOM, dend, 'lowpass=f=1000,tremolo=f=0.2:d=0.3,' + REV + ',' + fio(dend, 1.0, 3.0), 0.9, B)
src('hit2', '1.0*sin(2*PI*(40+40*exp(-6*t))*t)*exp(-1.6*t)', SOON, 3.0, 'lowpass=f=150', 0.7, B)
for i, (t0, f) in enumerate([(SOON + 0.35, 440), (SOON + 0.95, 523.3), (SOON + 1.55, 659.3)]):
    src('mot%d' % i, '0.5*sin(2*PI*%.1f*t)*exp(-2.2*t)+0.2*sin(2*PI*%.1f*t)*exp(-3.5*t)' % (f, 2 * f), t0, 3.5, 'lowpass=f=5000,' + REV, 0.5, B)
src('dun', '1.0*sin(2*PI*(45+25*exp(-6*t))*t)*exp(-1.5*t)', SOON + 2.6, 2.5, 'lowpass=f=140', 0.6, B)
src('hit3', '1.0*sin(2*PI*(42+30*exp(-6*t))*t)*exp(-2*t)', FOLLOW, 2.5, 'lowpass=f=140', 0.45, B)

# ================= buses =================
chains.append('%samix=inputs=%d:normalize=0[busA]' % (''.join('[%s]' % l for l in A), len(A)))
env = "if(lt(t,9.5),0.40,if(lt(t,39.9),0.40+0.35*(t-9.5)/30.4,if(lt(t,52.0),0.78,1.0)))"
gate = "volume='(%s)*if(between(t,%.2f,%.2f),0.0,if(between(t,%.2f,%.2f),0.0,if(between(t,%.2f,%.2f),0.0,1)))*0.62':eval=frame" % (
    env, GAP0 + 0.04, GAP1 - 0.01, MID0, MID1, DROP0, DROP1)
chains.append('[busA]%s[busAg]' % gate)
chains.append('%samix=inputs=%d:normalize=0[busB0]' % (''.join('[%s]' % l for l in B), len(B)))
chains.append("[busB0]volume='if(between(t,%.2f,%.2f),0.0,1)*0.62':eval=frame[busB]" % (GAP0 + 0.04, GAP1 - 0.02))
chains.append('[busAg][busB]amix=inputs=2:normalize=0,alimiter=limit=0.9,pan=stereo|c0=c0|c1=c0[ms]')


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
    chains.append('[%d:a]aresample=44100,atrim=start=%.3f,asetpts=PTS-STARTPTS,%s,pan=stereo|c0=c0|c1=c0,adelay=%d|%d,apad=pad_dur=130[%s]' % (idx, lead(wav), fx, dly, dly, lab))
    vl.append(lab)
chains.append('%samix=inputs=%d:normalize=0[vo0]' % (''.join('[%s]' % l for l in vl), len(vl)))
chains.append('[vo0]asplit=2[vo][vosc]')
chains.append('[ms][vosc]sidechaincompress=threshold=0.02:ratio=6:attack=40:release=500[duck]')
chains.append('[duck][vo]amix=inputs=2:normalize=0,alimiter=limit=0.95,afade=t=out:st=%.2f:d=2.5[main]' % (TM - 2.5))

# intro card sound
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
chains.append('[i1][i2]%s[i3]amix=inputs=%d:normalize=0,pan=stereo|c0=c0|c1=c0,apad=whole_dur=%.3f,atrim=0:%.3f[intro]' % (''.join(chime), 3 + len(chime), INTRO, INTRO))
iw = int(INTRO * 1000)
chains.append('[main]adelay=%d|%d[maind]' % (iw, iw))
chains.append('[intro][maind]amix=inputs=2:normalize=0,alimiter=limit=0.95[out]')

cmd = ['ffmpeg', '-loglevel', 'error', '-y'] + inputs + ['-filter_complex', ';'.join(chains), '-map', '[out]', '-t', '%.3f' % (TM + INTRO), '-ar', '44100', '-ac', '2', '-c:a', 'pcm_s16le',
                                                          os.path.join(HERE, 'thriller_mix3.wav')]
subprocess.run(cmd, check=True)
print('ok', len(A), len(B))
