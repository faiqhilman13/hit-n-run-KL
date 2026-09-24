using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Every sound in the game is synthesised at startup - no audio files needed.
    /// Swap any of these for real recordings later by assigning clips.
    /// </summary>
    public class ProcAudio : MonoBehaviour
    {
        public static AudioClip Coin, Crash, Smash, Clang, Horn, Fanfare, Punch, Siren, Engine, Blip, Fail, Whoosh, Aduh;
        static ProcAudio _i;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        const int Rate = 22050;

        public static void Init()
        {
            if (_i != null) return;
            _i = new GameObject("ProcAudio").AddComponent<ProcAudio>();
            DontDestroyOnLoad(_i.gameObject);
            var rng = new System.Random(3);
            float N() => (float)rng.NextDouble() * 2f - 1f;

            Coin = Make("coin", 0.18f, t => (t < 0.06f ? Sq(t, 1318) : Sq(t, 1760)) * 0.35f * Env(t, 0.18f));
            Blip = Make("blip", 0.08f, t => Sq(t, 880) * 0.3f * Env(t, 0.08f));
            Crash = Make("crash", 0.5f, t => (N() * 0.8f + Mathf.Sin(t * 90f * 6.28f) * 0.4f) * Mathf.Exp(-t * 7f));
            Smash = Make("smash", 0.35f, t => (N() * 0.6f + Mathf.Sin(t * 300f * 6.28f) * 0.3f * Mathf.Exp(-t * 20f)) * Mathf.Exp(-t * 10f));
            Clang = Make("clang", 0.8f, t => (Mathf.Sin(t * 523f * 6.28f) + Mathf.Sin(t * 1211f * 6.28f) * 0.6f + Mathf.Sin(t * 2117f * 6.28f) * 0.3f) * 0.3f * Mathf.Exp(-t * 5f));
            Punch = Make("punch", 0.16f, t => (N() * 0.7f + Mathf.Sin(t * 110f * 6.28f)) * Mathf.Exp(-t * 28f));
            Whoosh = Make("whoosh", 0.2f, t => N() * 0.25f * Mathf.Sin(t / 0.2f * Mathf.PI));
            // Proton horn: "pon pon" - two stacked square tones
            Horn = Make("horn", 0.55f, t =>
            {
                bool on = t < 0.22f || (t > 0.3f && t < 0.52f);
                return on ? (Sq(t, 392) * 0.5f + Sq(t, 494) * 0.4f) * 0.35f : 0f;
            });
            Fanfare = Make("fanfare", 0.9f, t =>
            {
                float[] notes = { 523, 659, 784, 1046 };
                int i = Mathf.Min(3, (int)(t / 0.15f));
                return Sq(t, notes[i]) * 0.25f * (i == 3 ? Env(t - 0.45f, 0.45f) : 1f);
            });
            Fail = Make("fail", 0.9f, t => Sq(t, Mathf.Lerp(330, 140, t / 0.9f)) * 0.3f * Env(t, 0.9f));
            Siren = Make("siren", 1.6f, t => Mathf.Sin(6.28f * (t < 0.8f ? 700f : 960f) * t) * 0.35f);
            Engine = Make("engine", 0.5f, t =>
            {
                float f = 55f;
                return (Mathf.Sin(t * f * 6.28f) * 0.5f + Sq(t, f * 2f) * 0.2f + Mathf.Sin(t * f * 0.5f * 6.28f) * 0.3f) * 0.5f;
            });
            Aduh = Make("aduh", 0.35f, t => Sq(t, Mathf.Lerp(620, 380, t / 0.35f)) * 0.2f * Env(t, 0.35f)); // cartoon yelp
        }

        static AudioSource _music;

        /// <summary>
        /// A little procedural "kampung pop" loop: kompang-style hand drums, a plucky bass
        /// and a pentatonic melody. Each level gets its own key, tempo and melody seed.
        /// </summary>
        public static void PlayMusic(int level)
        {
            if (_i == null) return;
            float bpm = level switch { 1 => 104f, 2 => 112f, 3 => 120f, 4 => 92f, _ => 98f };
            float root = level switch { 1 => 196f, 2 => 220f, 3 => 174.6f, 4 => 164.8f, _ => 196f };
            float beat = 60f / bpm;
            int bars = 8;
            float len = bars * 4 * beat;
            var rng = new System.Random(level * 31 + 7);
            int[] scale = { 0, 2, 4, 7, 9, 12, 14, 16 };
            // melody: one note per half beat, some rests, phrase repeats with variation
            int steps = bars * 8;
            var notes = new int[steps];
            for (int s = 0; s < steps; s++)
                notes[s] = (s % 16 < 8 || rng.NextDouble() < 0.5) && rng.NextDouble() < 0.72 ? scale[rng.Next(scale.Length)] : -1;
            for (int s = 16; s < steps; s++) if (s % 32 >= 16 && s % 4 != 3) notes[s] = notes[s - 16]; // call & answer
            int[] bassLine = { 0, 0, 7, 5, 0, 0, 9, 7 };
            var nrng = new System.Random(99);
            float N() => (float)nrng.NextDouble() * 2f - 1f;

            var clip = Make("music" + level, len, t =>
            {
                float b = t / beat;
                int beatIdx = (int)b;
                float inBeat = (b - beatIdx) * beat;
                // kompang: big hit on 1 and 3, small slaps on the off-beats
                float drum = 0f;
                if (beatIdx % 2 == 0) drum += (Mathf.Sin(inBeat * 110f * 6.283f) * 0.9f + N() * 0.3f) * Mathf.Exp(-inBeat * 18f);
                float half = (b * 2f) - Mathf.Floor(b * 2f);
                float halfT = half * beat * 0.5f;
                if (((int)(b * 2f)) % 2 == 1) drum += N() * 0.35f * Mathf.Exp(-halfT * 45f);
                // bass
                int bar = beatIdx / 4;
                float bf = root * 0.5f * Mathf.Pow(2f, bassLine[bar % bassLine.Length] / 12f);
                float bass = Mathf.Sin(t * bf * 6.283f) * 0.35f * Mathf.Exp(-inBeat * 3f);
                // melody (plucked, like a gambus)
                int step = (int)(b * 2f) % steps;
                float mel = 0f;
                if (notes[step] >= 0)
                {
                    float mf = root * Mathf.Pow(2f, notes[step] / 12f);
                    mel = (Mathf.Sin(t * mf * 6.283f) + 0.4f * Mathf.Sin(t * mf * 2f * 6.283f)) * 0.22f * Mathf.Exp(-halfT * 7f);
                }
                return (drum * 0.5f + bass + mel) * 0.6f;
            });
            if (_music == null)
            {
                _music = new GameObject("Music").AddComponent<AudioSource>();
                _music.transform.SetParent(_i.transform, false);
                _music.loop = true;
                _music.spatialBlend = 0f;
            }
            _music.clip = clip;
            _music.volume = 0.22f;
            _music.Play();
        }

        public static void StopMusic() { if (_music) _music.Stop(); }

        static float Sq(float t, float f) => Mathf.Sin(t * f * 6.2831853f) > 0 ? 1f : -1f;
        static float Env(float t, float len) => Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((len - t) / (len * 0.4f));

        static AudioClip Make(string name, float len, System.Func<float, float> f)
        {
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static void Play(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            if (_i == null || clip == null) return;
            var src = _i.GetSource();
            src.transform.position = pos;
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 0.6f;
            src.Play();
        }

        public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (_i == null || clip == null) return;
            var src = _i.GetSource();
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 0f;
            src.Play();
        }

        public static AudioSource Loop(AudioClip clip, Transform follow, float volume)
        {
            var go = new GameObject("Loop_" + clip.name);
            go.transform.SetParent(follow, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = volume;
            src.spatialBlend = 0.8f;
            src.minDistance = 4f;
            src.maxDistance = 80f;
            src.Play();
            return src;
        }

        AudioSource GetSource()
        {
            foreach (var s in _pool) if (!s.isPlaying) return s;
            var go = new GameObject("sfx");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.minDistance = 5f;
            src.maxDistance = 90f;
            _pool.Add(src);
            return src;
        }
    }
}
