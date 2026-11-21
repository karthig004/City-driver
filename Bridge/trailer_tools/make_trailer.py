"""Muxes the recorded frame sequence (trailer/f_#####.jpg, 30 fps) and the synthesised score into one AVI (Motion-JPEG video + 16-bit PCM audio).
Pure standard library: no ffmpeg needed. Usage: python -I make_trailer.py <frames_dir> <score.wav> <out.avi>"""
import glob, os, struct, sys, wave

frames_dir, wav_path, out_path = sys.argv[1:4]
FPS = 30
files = sorted(glob.glob(os.path.join(frames_dir, 'f_*.jpg')))
if not files: raise SystemExit('no frames')
W, H = 1920, 1080
with wave.open(wav_path, 'rb') as w:
    ch, sw, sr = w.getnchannels(), w.getsampwidth(), w.getframerate()
    pcm = w.readframes(w.getnframes())
spf = sr // FPS                                     # audio samples per video frame (1470 at 44.1 kHz)
bytes_per_sample = ch * sw
need = len(files) * spf * bytes_per_sample
if len(pcm) < need: pcm += b'\x00' * (need - len(pcm))


def chunk(tag, data):
    pad = b'\x00' if len(data) % 2 else b''
    return tag + struct.pack('<I', len(data)) + data + pad


def lst(kind, payload): return b'LIST' + struct.pack('<I', len(payload) + 4) + kind + payload


sizes = [os.path.getsize(f) for f in files]
max_frame = max(sizes)
n = len(files)
avih = struct.pack('<IIIIIIIIIIIIII', int(1e6 / FPS), (max_frame + spf * bytes_per_sample) * FPS, 0, 0x10, n, 0, 2, max_frame + 8192, W, H, 0, 0, 0, 0)
def strh(kind, handler, scale, rate, length, bufsize, sample_size, rect):
    return struct.pack('<4s4sIHHIIIIIIIIhhhh', kind, handler, 0, 0, 0, 0, scale, rate, 0, length, bufsize, 0xFFFFFFFF, sample_size, *rect)
vstrh = strh(b'vids', b'MJPG', 1, FPS, n, max_frame, 0, (0, 0, W, H))
vstrf = struct.pack('<IiiHHIIiiII', 40, W, H, 1, 24, 0x47504A4D, W * H * 3, 0, 0, 0, 0)
astrh = strh(b'auds', bytes(4), 1, sr, n * spf, spf * bytes_per_sample, bytes_per_sample, (0, 0, 0, 0))
astrf = struct.pack('<HHIIHH', 1, ch, sr, sr * bytes_per_sample, bytes_per_sample, sw * 8)
hdrl = lst(b'hdrl', chunk(b'avih', avih)
           + lst(b'strl', chunk(b'strh', vstrh) + chunk(b'strf', vstrf))
           + lst(b'strl', chunk(b'strh', astrh) + chunk(b'strf', astrf)))

movi_parts = []; index = []; offset = 4                      # offsets in idx1 are relative to the 'movi' fourcc
with open(out_path + '.tmp', 'wb') as out:
    out.write(b'\x00' * 0)
    body_chunks = []
    movi = bytearray()
    for i, f in enumerate(files):
        data = open(f, 'rb').read()
        c = chunk(b'00dc', data); index.append((b'00dc', 0x10, offset, len(data))); movi += c; offset += len(c)
        a = pcm[i * spf * bytes_per_sample:(i + 1) * spf * bytes_per_sample]
        c = chunk(b'01wb', a); index.append((b'01wb', 0x10, offset, len(a))); movi += c; offset += len(c)
    idx = b''.join(struct.pack('<4sIII', t, fl, o, s) for t, fl, o, s in index)
    movi_list = lst(b'movi', bytes(movi))
    riff_payload = b'AVI ' + hdrl + movi_list + chunk(b'idx1', idx)
    out.write(b'RIFF' + struct.pack('<I', len(riff_payload)) + riff_payload)
os.replace(out_path + '.tmp', out_path)
print('wrote', out_path, os.path.getsize(out_path) // (1024 * 1024), 'MB,', n, 'frames,', round(n / FPS, 2), 's')
