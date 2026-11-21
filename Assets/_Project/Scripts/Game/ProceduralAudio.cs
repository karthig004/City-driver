using UnityEngine;

namespace CityDriver.Game
{
    /// <summary>
    /// All game audio is synthesised in code (no recorded assets, so there is no licence problem and nothing to download).
    /// Engine / tyre / wind loops are periodic so they loop without clicks; the radio stations are short 4-bar synthesised loops.
    /// These are simple synthetic sounds, not studio recordings.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int Rate = 22050;
        public static readonly string[] StationNames = { "Off", "Lo-Fi City", "Night Drive", "Metro Beats" };

        /// <summary>Raw mono samples. Built on a worker thread (pure maths, no Unity objects); turned into an AudioClip on the main thread.</summary>
        public sealed class AudioData
        {
            public string name; public float[] samples;
            public AudioClip ToClip()
            {
                var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
        }

        private static AudioData Make(string name, float[] data, bool normalise = true, float peak = 0.9f)
        {
            if (normalise)
            {
                float max = 1e-5f;
                for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
                float k = peak / max;
                for (int i = 0; i < data.Length; i++) data[i] *= k;
            }
            return new AudioData { name = name, samples = data };
        }

        /// <summary>Loudness of a clip (RMS of up to the first second), used by tests to prove it is not silent.</summary>
        public static float Rms(AudioClip clip)
        {
            if (clip == null) return 0f;
            int n = Mathf.Min(clip.samples, Rate);
            var d = new float[n * clip.channels];
            clip.GetData(d, 0);
            double sum = 0; for (int i = 0; i < d.Length; i++) sum += d[i] * d[i];
            return Mathf.Sqrt((float)(sum / Mathf.Max(1, d.Length)));
        }

        // ---------------------------------------------------------------------------------------- loops
        /// <summary>One second of engine at ~55 Hz firing rate: harmonic stack plus periodic rumble. Pitch-shift it with AudioSource.pitch.</summary>
        public static AudioData EngineLoop()
        {
            var d = new float[Rate];
            var rnd = new System.Random(7);
            for (int h = 1; h <= 14; h++)
            {
                float amp = 1f / Mathf.Pow(h, 0.85f) * (h % 2 == 0 ? 0.75f : 1f);
                float phase = (float)rnd.NextDouble() * Mathf.PI * 2f;
                int f = 55 * h;
                for (int i = 0; i < d.Length; i++) d[i] += amp * Mathf.Sin(2f * Mathf.PI * f * i / Rate + phase);
            }
            for (int k = 20; k < 380; k += 5)              // combustion roughness (integer Hz => seamless loop)
            {
                float phase = (float)rnd.NextDouble() * Mathf.PI * 2f;
                for (int i = 0; i < d.Length; i += 1) d[i] += 0.035f * Mathf.Sin(2f * Mathf.PI * k * i / Rate + phase);
            }
            for (int i = 0; i < d.Length; i++)             // gentle waveshaping for a grittier tone
                d[i] = (float)System.Math.Tanh(d[i] * 0.55f);
            return Make("Engine", d);
        }

        private static float[] BandNoise(int lo, int hi, int step, int seed)
        {
            var d = new float[Rate];
            var rnd = new System.Random(seed);
            for (int f = lo; f <= hi; f += step)
            {
                float phase = (float)rnd.NextDouble() * Mathf.PI * 2f;
                for (int i = 0; i < d.Length; i++) d[i] += Mathf.Sin(2f * Mathf.PI * f * i / Rate + phase);
            }
            return d;
        }

        public static AudioData TyreScreech() => Make("Screech", BandNoise(900, 2800, 37, 11), true, 0.8f);
        public static AudioData Wind() => Make("Wind", BandNoise(40, 700, 11, 23), true, 0.7f);
        public static AudioData CityAmbience() => Make("CityAmbience", BandNoise(30, 420, 7, 31), true, 0.6f);

        // ---------------------------------------------------------------------------------------- one shots
        public static AudioData Crash()
        {
            int n = Rate / 2;
            var d = new float[n];
            var rnd = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, env = Mathf.Exp(-t * 9f);
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                float thump = Mathf.Sin(2f * Mathf.PI * (70f - 40f * t) * t);
                d[i] = env * (0.7f * noise + 1.3f * thump);
            }
            return Make("Crash", d);
        }

        public static AudioData Click()
        {
            int n = Rate / 14;
            var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)Rate; d[i] = Mathf.Exp(-t * 90f) * Mathf.Sin(2f * Mathf.PI * 1250f * t); }
            return Make("Click", d, true, 0.6f);
        }

        public static AudioData Horn()
        {
            int n = (int)(Rate * 0.5f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, env = Mathf.Clamp01(t * 40f) * Mathf.Clamp01((0.5f - t) * 25f);
                d[i] = env * (Mathf.Sin(2f * Mathf.PI * 420f * t) + 0.8f * Mathf.Sin(2f * Mathf.PI * 530f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 840f * t));
            }
            return Make("Horn", d, true, 0.7f);
        }


        // ---------------------------------------------------------------------------------------- weather, pursuit and story sounds
        /// <summary>Soft rain: a low, warm hush (two-pole low-passed noise, nothing above ~2 kHz so it never hisses) with gentle round drops on top.
        /// The old version was a stack of 1.4-9.5 kHz tones, which sounded like a piercing hiss.</summary>
        public static AudioData Rain()
        {
            int n = Rate * 3, tail = 2400, total = n + tail;
            var d = new float[total];
            var rnd = new System.Random(41);
            float a = 0f, b2 = 0f;
            for (int i = 0; i < total; i++)
            {
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                a += (w - a) * 0.075f; b2 += (a - b2) * 0.16f;                       // ~600 Hz corner: warm, no sizzle
                float swell = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.33f * i / Rate);
                d[i] = b2 * 3.2f * swell;
            }
            for (int k = 0; k < 150; k++)                                            // soft drops: short round blips
            {
                int pos = rnd.Next(total); float amp = 0.05f + (float)rnd.NextDouble() * 0.16f, f = 380f + (float)rnd.NextDouble() * 520f;
                for (int j = 0; j < 520 && pos + j < total; j++) d[pos + j] += amp * Mathf.Exp(-j / 85f) * Mathf.Sin(2f * Mathf.PI * f * j / Rate) * Mathf.Min(1f, j / 30f);
            }
            for (int j = 0; j < tail; j++) { float t = j / (float)tail; d[j] = d[j] * t + d[n + j] * (1f - t); }   // crossfade the end into the start: a seamless loop
            var loop = new float[n]; System.Array.Copy(d, loop, n);
            return Make("Rain", loop, true, 0.5f);
        }

        public static AudioData Thunder()
        {
            int n = (int)(Rate * 2.6f);
            var d = new float[n];
            var rnd = new System.Random(77);
            for (int k = 0; k < 40; k++)
            {
                float f = 22f + (float)rnd.NextDouble() * 110f, phase = (float)rnd.NextDouble() * Mathf.PI * 2f, delay = (float)rnd.NextDouble() * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate - delay;
                    if (t < 0f) continue;
                    d[i] += Mathf.Sin(2f * Mathf.PI * f * t + phase) * Mathf.Min(1f, t * 8f) * Mathf.Exp(-t * 1.5f);
                }
            }
            for (int c = 0; c < 18; c++)                                    // crackle at the start
            {
                int s0 = (int)((float)rnd.NextDouble() * 0.4f * Rate);
                for (int i = 0; i < 400 && s0 + i < n; i++) d[s0 + i] += (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-i / 90f) * 0.9f;
            }
            for (int i = 1; i < n; i++) d[i] = d[i - 1] * 0.55f + d[i] * 0.45f;           // roll the highs off: a distant rumble, not a bang
            return Make("Thunder", d, true, 0.55f);
        }

        /// <summary>Two-second wail whose mean frequency is exactly 1000 Hz, so it loops without a click.</summary>
        public static AudioData Siren()
        {
            var d = new float[Rate * 2];
            double phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                double t = i / (double)Rate;
                double f = 1000.0 + 360.0 * System.Math.Sin(2.0 * System.Math.PI * t / 2.0);
                phase += 2.0 * System.Math.PI * f / Rate;
                d[i] = (float)(System.Math.Sin(phase) + 0.35 * System.Math.Sin(2 * phase) + 0.15 * System.Math.Sin(3 * phase));
            }
            return Make("Siren", d, true, 0.7f);
        }

        /// <summary>Eight-second suspense bed for Shadow Run: low drone with beating, slow tremolo and sparse pings.</summary>
        public static AudioData Suspense()
        {
            var d = new float[Rate * 8];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float trem = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
                float v = Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.8f * Mathf.Sin(2f * Mathf.PI * 55.5f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 82.5f * t)
                        + 0.35f * Mathf.Sin(2f * Mathf.PI * 110.25f * t) + 0.18f * Mathf.Sin(2f * Mathf.PI * 164.75f * t + 1.3f * Mathf.Sin(2f * Mathf.PI * 0.125f * t));
                d[i] = v * trem * 0.35f;
            }
            float[] pingAt = { 1.0f, 3.4f, 5.2f, 6.6f }; float[] pingHz = { 880f, 659.25f, 987.75f, 740f };
            for (int p = 0; p < pingAt.Length; p++) Tone(d, pingAt[p], pingHz[p], 1.2f, 0.12f, 3, 3.2f, 0.01f);
            for (int h = 0; h < 8; h++) Kick(d, h * 1.0f, 0.38f);                       // slow heartbeat pulse
            return Make("Suspense", d, true, 0.7f);
        }

        public static AudioData CardChime()
        {
            int n = (int)(Rate * 1.8f);
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double f = 300.0 + 1500.0 * System.Math.Pow(t / 1.8, 2.0);
                phase += 2.0 * System.Math.PI * f / Rate;
                double env = System.Math.Min(1.0, t * 12.0) * System.Math.Exp(-t * 1.2);
                d[i] = (float)(env * (System.Math.Sin(phase) + 0.4 * System.Math.Sin(phase * 2.01) + 0.2 * System.Math.Sin(phase * 3.02)));
            }
            Tone(d, 0f, 140f, 0.5f, 0.9f, 2, 6f, 0.002f);
            return Make("CardChime", d, true, 0.75f);
        }

        // ---------------------------------------------------------------------------------------- radio
        public static AudioData Station(int id)
        {
            switch (id)
            {
                case 1: return LoFi();
                case 2: return NightDrive();
                case 3: return MetroBeats();
                default: return null;
            }
        }

        private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        // adds a decaying additive tone; harmonics = number of overtones, decay in 1/s
        private static void Tone(float[] buf, float start, float freq, float dur, float amp, int harmonics, float decay, float attack = 0.004f)
        {
            int s0 = (int)(start * Rate), n = (int)(dur * Rate);
            for (int i = 0; i < n && s0 + i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / attack) * Mathf.Exp(-t * decay) * Mathf.Clamp01((dur - t) * 30f);
                float v = 0f;
                for (int h = 1; h <= harmonics; h++) v += Mathf.Sin(2f * Mathf.PI * freq * h * t) / (h * h * 0.6f + 0.4f);
                buf[s0 + i] += amp * env * v;
            }
        }

        private static void Kick(float[] buf, float start, float amp)
        {
            int s0 = (int)(start * Rate), n = (int)(0.28f * Rate);
            for (int i = 0; i < n && s0 + i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 48f + 90f * Mathf.Exp(-t * 28f);
                buf[s0 + i] += amp * Mathf.Exp(-t * 12f) * Mathf.Sin(2f * Mathf.PI * f * t);
            }
        }

        private static void Noise(float[] buf, float start, float dur, float amp, float decay, System.Random rnd, float highpass = 0.6f)
        {
            int s0 = (int)(start * Rate), n = (int)(dur * Rate);
            float last = 0f;
            for (int i = 0; i < n && s0 + i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2 - 1);
                float hp = x - last * highpass; last = x;
                buf[s0 + i] += amp * Mathf.Exp(-t * decay) * hp;
            }
        }

        private static AudioData LoFi()
        {
            const float bpm = 72f; float beat = 60f / bpm; int bars = 4;
            var buf = new float[(int)(beat * 4 * bars * Rate)];
            var rnd = new System.Random(101);
            int[][] chords = { new[] { 57, 60, 64, 67 }, new[] { 53, 57, 60, 64 }, new[] { 48, 55, 59, 64 }, new[] { 55, 59, 62, 69 } };   // Am7 Fmaj7 Cmaj7 G6
            int[] roots = { 45, 41, 36, 43 };
            for (int bar = 0; bar < bars; bar++)
            {
                float b0 = bar * 4 * beat;
                foreach (int n in chords[bar]) Tone(buf, b0, Midi(n), beat * 3.6f, 0.16f, 3, 1.1f, 0.02f);     // soft electric-piano pad
                Tone(buf, b0, Midi(roots[bar]), beat * 1.8f, 0.45f, 2, 1.4f);                                      // bass
                Tone(buf, b0 + 2.5f * beat, Midi(roots[bar] + 7), beat * 1.2f, 0.32f, 2, 2f);
                int[] mel = { 0, 2, 1, 3 };
                for (int k = 0; k < 4; k++) Tone(buf, b0 + (0.5f + k) * beat, Midi(chords[bar][mel[k]] + 12), beat * 0.9f, 0.13f, 2, 3.2f);
                Kick(buf, b0, 0.9f); Kick(buf, b0 + 2.5f * beat, 0.7f);
                Noise(buf, b0 + beat, 0.12f, 0.5f, 28f, rnd, 0.3f); Noise(buf, b0 + 3f * beat, 0.12f, 0.5f, 28f, rnd, 0.3f);   // snare-ish
                for (int h = 0; h < 8; h++) Noise(buf, b0 + h * beat * 0.5f, 0.04f, 0.13f, 70f, rnd, 0.95f);                    // hats
            }
            for (int i = 0; i < buf.Length; i++) buf[i] += 0.012f * (float)(rnd.NextDouble() * 2 - 1);                        // vinyl hiss
            return Make("Radio_LoFiCity", buf, true, 0.8f);
        }

        private static AudioData NightDrive()
        {
            const float bpm = 100f; float beat = 60f / bpm; int bars = 4;
            var buf = new float[(int)(beat * 4 * bars * Rate)];
            var rnd = new System.Random(202);
            int[][] chords = { new[] { 62, 65, 69 }, new[] { 58, 62, 65 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 } };   // Dm Bb F C
            int[] roots = { 38, 34, 41, 36 };
            for (int bar = 0; bar < bars; bar++)
            {
                float b0 = bar * 4 * beat;
                foreach (int n in chords[bar]) Tone(buf, b0, Midi(n), beat * 3.9f, 0.1f, 6, 0.35f, 0.08f);                  // synth pad
                for (int e = 0; e < 8; e++)                                                                              // 8th-note bass
                    Tone(buf, b0 + e * beat * 0.5f, Midi(roots[bar]), beat * 0.45f, 0.3f, 5, 5f);
                for (int s = 0; s < 16; s++)                                                                             // 16th arpeggio
                    Tone(buf, b0 + s * beat * 0.25f, Midi(chords[bar][s % 3] + 24), beat * 0.22f, 0.085f, 4, 9f);
                for (int k = 0; k < 4; k++) Kick(buf, b0 + k * beat, 0.95f);
                Noise(buf, b0 + beat, 0.16f, 0.55f, 22f, rnd, 0.4f); Noise(buf, b0 + 3f * beat, 0.16f, 0.55f, 22f, rnd, 0.4f);
                for (int h = 0; h < 8; h++) Noise(buf, b0 + (h + 0.5f) * beat * 0.5f, 0.05f, 0.16f, 60f, rnd, 0.95f);
            }
            return Make("Radio_NightDrive", buf, true, 0.8f);
        }

        // Indian-fusion flavoured: drone (Sa-Pa), tabla-like pitched drums and a plucked melody on a Mayamalavagowla-style scale.
        private static AudioData MetroBeats()
        {
            const float bpm = 96f; float beat = 60f / bpm; int bars = 4;
            var buf = new float[(int)(beat * 4 * bars * Rate)];
            var rnd = new System.Random(303);
            int sa = 48;                                                  // C
            int[] scale = { 0, 1, 4, 5, 7, 8, 11, 12 };                   // S R1 G3 M1 P D1 N3 S
            int[] phrase = { 0, 2, 4, 3, 2, 1, 0, 2, 4, 6, 5, 4, 3, 2, 1, 0 };
            for (int bar = 0; bar < bars; bar++)
            {
                float b0 = bar * 4 * beat;
                Tone(buf, b0, Midi(sa - 12), beat * 4f, 0.14f, 6, 0.2f, 0.2f);                      // drone Sa
                Tone(buf, b0, Midi(sa - 5), beat * 4f, 0.09f, 6, 0.2f, 0.2f);                       // drone Pa
                for (int k = 0; k < 16; k++)                                                        // plucked melody (sitar-ish: bright harmonics, fast decay)
                {
                    int idx = phrase[(k + bar * 4) % phrase.Length];
                    if ((k + bar) % 3 == 2) continue;                                               // leave gaps
                    Tone(buf, b0 + k * beat * 0.25f, Midi(sa + 12 + scale[idx]), beat * 0.5f, 0.17f, 9, 5.5f, 0.002f);
                }
                // tabla-like pattern: dha (low), na/tin (high pitched drum), ge (muted)
                float[] hits = { 0f, 0.75f, 1f, 1.5f, 2f, 2.5f, 3f, 3.5f };
                for (int hI = 0; hI < hits.Length; hI++)
                {
                    float t0 = b0 + hits[hI] * beat;
                    bool low = hI % 2 == 0;
                    Tone(buf, t0, low ? 98f : 310f, 0.22f, low ? 0.55f : 0.32f, 2, low ? 14f : 22f, 0.001f);
                    Noise(buf, t0, 0.03f, 0.2f, 120f, rnd, 0.5f);
                }
                Kick(buf, b0, 0.55f);
            }
            return Make("Radio_MetroBeats", buf, true, 0.8f);
        }
    }
}
