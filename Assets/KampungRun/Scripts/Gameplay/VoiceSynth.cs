using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Cartoon "babble" voices (Animal Crossing / Sims style): every line is turned into a
    /// string of syllables whose vowels come from the actual text, pushed through two formant
    /// resonators on top of a buzzy glottal source. Each character has their own pitch,
    /// pace, timbre and wobble, so you can tell who's talking with your eyes closed.
    ///
    /// Real recordings override this: put a clip at Resources/Voices/&lt;VoiceKey&gt;/&lt;line hash&gt;
    /// (hash = VoiceSynth.Hash(line text), speaker name without spaces or dots) and it is used instead.
    /// </summary>
    public static class VoiceSynth
    {
        public class Profile
        {
            public float pitch = 140f;      // Hz
            public float range = 0.25f;     // pitch variation between syllables
            public float rate = 9f;         // syllables per second
            public float formant = 1f;      // >1 brighter/smaller head, <1 darker/bigger
            public float buzz = 0.6f;       // 0 = smooth sine-ish, 1 = raspy saw
            public float vibrato;           // old/wobbly voices
            public float breath = 0.05f;    // noise mix
            public float volume = 0.8f;
            public float growl;             // low-frequency amplitude roughness
        }

        static readonly Dictionary<string, Profile> Profiles = new Dictionary<string, Profile>
        {
            ["Pak Mat"] = new Profile { pitch = 98, range = 0.18f, rate = 7.2f, formant = 0.86f, buzz = 0.75f, growl = 0.25f },
            ["Mak Som"] = new Profile { pitch = 225, range = 0.32f, rate = 10.5f, formant = 1.12f, buzz = 0.45f },
            ["Along"] = new Profile { pitch = 150, range = 0.22f, rate = 9f, formant = 1.0f, buzz = 0.55f },
            ["Adik"] = new Profile { pitch = 340, range = 0.4f, rate = 11.5f, formant = 1.32f, buzz = 0.35f },
            ["Tok Ketua"] = new Profile { pitch = 118, range = 0.15f, rate = 6.3f, formant = 0.92f, buzz = 0.6f, vibrato = 0.06f, breath = 0.12f },
            ["Anneh"] = new Profile { pitch = 150, range = 0.45f, rate = 11f, formant = 1.02f, buzz = 0.5f },
            ["Anneh Mamak"] = new Profile { pitch = 150, range = 0.45f, rate = 11f, formant = 1.02f, buzz = 0.5f },
            ["Aunty Pasar"] = new Profile { pitch = 255, range = 0.38f, rate = 11f, formant = 1.15f, buzz = 0.7f, volume = 0.9f },
            ["Inspektor Rosli"] = new Profile { pitch = 112, range = 0.1f, rate = 8f, formant = 0.9f, buzz = 0.65f },
            ["Prof. Kassim"] = new Profile { pitch = 150, range = 0.3f, rate = 12f, formant = 1.18f, buzz = 0.85f, breath = 0.08f },
            ["Datuk Mega"] = new Profile { pitch = 78, range = 0.22f, rate = 6f, formant = 0.8f, buzz = 0.9f, growl = 0.5f, volume = 0.95f },
            ["Joe Bintang"] = new Profile { pitch = 165, range = 0.3f, rate = 10f, formant = 1.05f, buzz = 0.6f },
            ["Mat Rempit"] = new Profile { pitch = 170, range = 0.35f, rate = 11f, formant = 1.05f, buzz = 0.65f },
            ["Pakcik Teksi"] = new Profile { pitch = 125, range = 0.25f, rate = 8.5f, formant = 0.95f, buzz = 0.55f, vibrato = 0.03f },
            ["Pemandu Bas"] = new Profile { pitch = 130, range = 0.2f, rate = 8f, formant = 0.95f, buzz = 0.6f },
            ["Aiman"] = new Profile { pitch = 158, range = 0.28f, rate = 10.5f, formant = 1.04f, buzz = 0.5f },
            ["Mei"] = new Profile { pitch = 240, range = 0.34f, rate = 11.5f, formant = 1.14f, buzz = 0.4f, volume = 0.85f },
            ["Ravi"] = new Profile { pitch = 105, range = 0.16f, rate = 7.8f, formant = 0.88f, buzz = 0.7f, growl = 0.15f },
        };

        const int Rate = 22050;
        static AudioSource _src;
        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public static Profile For(string speaker)
        {
            if (string.IsNullOrEmpty(speaker)) return null;
            if (Profiles.TryGetValue(speaker, out var p)) return p;
            // unknown speaker: a stable voice derived from the name
            int h = speaker.GetHashCode();
            var rng = new System.Random(h);
            return new Profile { pitch = 110 + (float)rng.NextDouble() * 150, rate = 8 + (float)rng.NextDouble() * 3, formant = 0.9f + (float)rng.NextDouble() * 0.3f };
        }

        /// <summary>Speak a line. Returns its length in seconds (0 if silent).</summary>
        public static float Speak(string speaker, string text)
        {
            Stop();
            var prof = For(speaker);
            if (prof == null || string.IsNullOrEmpty(text)) return 0f;
            string key = $"{speaker}|{text}";
            var clip = LoadRecorded(speaker, text);
            if (clip == null && !Cache.TryGetValue(key, out clip))
            {
                clip = Babble(prof, text);
                if (Cache.Count > 200) Cache.Clear();
                Cache[key] = clip;
            }
            if (_src == null)
            {
                var go = new GameObject("Voice");
                Object.DontDestroyOnLoad(go);
                _src = go.AddComponent<AudioSource>();
                _src.spatialBlend = 0f;
                _src.playOnAwake = false;
            }
            _src.clip = clip;
            _src.volume = prof.volume;
            _src.Play();
            return clip.length;
        }

        /// <summary>A short exclamation (bail-outs, getting hit) in a character's voice.</summary>
        public static void Bark(string speaker, string word, Vector3 pos)
        {
            var prof = For(speaker);
            if (prof == null) return;
            var fast = new Profile
            {
                pitch = prof.pitch * 1.25f, range = prof.range * 1.5f, rate = prof.rate * 1.2f, formant = prof.formant,
                buzz = prof.buzz, vibrato = prof.vibrato, breath = prof.breath, volume = prof.volume, growl = prof.growl,
            };
            var clip = Babble(fast, word + "!");
            AudioSource.PlayClipAtPoint(clip, pos, prof.volume);
        }

        public static void Stop()
        {
            if (_src != null && _src.isPlaying) _src.Stop();
        }

        static AudioClip LoadRecorded(string speaker, string text)
        {
            string hash = Hash(text);
            return Resources.Load<AudioClip>($"Voices/{speaker.Replace(".", "").Replace(" ", "")}/{hash}");
        }

        public static string Hash(string text)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in text) h = (h ^ c) * 16777619;
                return h.ToString("x8");
            }
        }

        // ------------------------------------------------------------------ synthesis
        struct Syl { public char vowel; public float dur, pitchMul, amp; public bool pause; }

        // formant pairs (F1, F2) in Hz for a neutral adult voice
        static Vector2 Formants(char v)
        {
            switch (v)
            {
                case 'a': return new Vector2(800, 1250);
                case 'e': return new Vector2(500, 1900);
                case 'i': return new Vector2(320, 2300);
                case 'o': return new Vector2(520, 900);
                case 'u': return new Vector2(350, 800);
                default: return new Vector2(600, 1500);
            }
        }

        static List<Syl> Syllables(Profile p, string text, System.Random rng)
        {
            var list = new List<Syl>();
            string t = text.ToLowerInvariant();
            bool question = text.TrimEnd().EndsWith("?");
            bool shout = text.Contains("!") || (text.Length > 3 && text.ToUpperInvariant() == text);
            float baseDur = 1f / p.rate;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '.' || c == ',' || c == ';' || c == ':' || c == '-')
                {
                    list.Add(new Syl { pause = true, dur = c == ',' ? baseDur * 1.2f : baseDur * 2.2f });
                    continue;
                }
                if ("aeiou".IndexOf(c) < 0) continue;
                // collapse vowel runs ("teh tarik" -> e, a, i)
                while (i + 1 < t.Length && "aeiou".IndexOf(t[i + 1]) >= 0) i++;
                list.Add(new Syl
                {
                    vowel = c,
                    dur = baseDur * (0.75f + (float)rng.NextDouble() * 0.5f),
                    pitchMul = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * p.range,
                    amp = 0.8f + (float)rng.NextDouble() * 0.2f,
                });
            }
            // cap very long lines so the voice doesn't drone on after the text finishes
            if (list.Count > 44) list.RemoveRange(44, list.Count - 44);
            // intonation: questions rise, statements fall, shouts are higher and louder
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                var s = list[i];
                float f = n > 1 ? i / (float)(n - 1) : 0f;
                s.pitchMul *= question ? Mathf.Lerp(1f, 1.3f, f * f) : Mathf.Lerp(1.05f, 0.88f, f);
                if (shout) { s.pitchMul *= 1.15f; s.amp *= 1.15f; }
                list[i] = s;
            }
            return list;
        }

        static AudioClip Babble(Profile p, string text)
        {
            var rng = new System.Random(text.GetHashCode());
            var syl = Syllables(p, text, rng);
            float total = 0.05f;
            foreach (var s in syl) total += s.dur;
            int count = Mathf.CeilToInt(total * Rate);
            var data = new float[count];

            // two resonators (biquad band-pass) for F1/F2
            float y1a = 0, y2a = 0, y1b = 0, y2b = 0;
            float phase = 0f;
            int idx = (int)(0.02f * Rate);
            float prevPitch = p.pitch;
            var nrng = new System.Random(7);
            foreach (var s in syl)
            {
                int len = Mathf.Max(1, (int)(s.dur * Rate));
                if (s.pause) { idx += len; continue; }
                var fm = Formants(s.vowel) * p.formant;
                float targetPitch = p.pitch * s.pitchMul;
                for (int k = 0; k < len && idx < count; k++, idx++)
                {
                    float tt = k / (float)len;
                    // consonant-ish onset: a short noisy burst, then the vowel
                    float env = Mathf.Clamp01(tt / 0.12f) * Mathf.Clamp01((1f - tt) / 0.25f);
                    float pitch = Mathf.Lerp(prevPitch, targetPitch, Mathf.Clamp01(tt * 6f));
                    pitch *= 1f + p.vibrato * Mathf.Sin(idx / (float)Rate * 6.2831f * 5.5f);
                    phase += pitch / Rate;
                    if (phase > 1f) phase -= 1f;
                    // glottal source: blend of soft pulse and raspy saw
                    float saw = 2f * phase - 1f;
                    float pulse = Mathf.Sin(phase * 6.2831f) + 0.5f * Mathf.Sin(phase * 12.566f);
                    float src = Mathf.Lerp(pulse * 0.6f, saw, p.buzz);
                    float noise = (float)nrng.NextDouble() * 2f - 1f;
                    src += noise * (p.breath + (tt < 0.1f ? 0.35f : 0f));
                    if (p.growl > 0) src *= 1f - p.growl * 0.5f * (1f + Mathf.Sin(idx / (float)Rate * 6.2831f * 28f));

                    float a = Resonate(src, fm.x, 90f, ref y1a, ref y2a);
                    float b = Resonate(src, fm.y, 120f, ref y1b, ref y2b);
                    data[idx] = (a + b * 0.6f) * env * s.amp * 0.35f;
                }
                prevPitch = targetPitch;
            }
            // soft clip
            for (int i = 0; i < count; i++) data[i] = (float)System.Math.Tanh(data[i] * 1.6f) * 0.8f;
            var clip = AudioClip.Create("voice", count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Two-pole resonator centred on f with bandwidth bw.</summary>
        static float Resonate(float x, float f, float bw, ref float y1, ref float y2)
        {
            float r = Mathf.Exp(-Mathf.PI * bw / Rate);
            float c = 2f * r * Mathf.Cos(2f * Mathf.PI * f / Rate);
            float y = (1f - r) * x + c * y1 - r * r * y2;
            y2 = y1;
            y1 = y;
            return y * 3f;
        }
    }
}
