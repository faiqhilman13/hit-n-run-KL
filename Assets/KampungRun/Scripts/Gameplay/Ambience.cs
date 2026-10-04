using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// The sound of the place, under everything else: a far-off traffic hum downtown, the babble of a crowd
    /// when there are people about, birdsong in the kampung and the parks by day, crickets and frogs at
    /// night, and now and then a rooster. All synthesised (like every other sound in the game) and mixed by
    /// where the camera is, so a busy street sounds busy and a kampung lane sounds like a kampung lane.
    /// </summary>
    public class Ambience : MonoBehaviour
    {
        static Ambience _i;
        AudioSource _city, _crowd, _birds, _night;
        float _check, _crowdWant, _cityWant, _birdWant, _nightWant, _roosterT = 20f, _green;
        static AudioClip _rooster;
        const int Rate = 22050;

        /// <summary>Start the beds (once).</summary>
        public static void Ensure(Transform parent)
        {
            if (_i != null) return;
            _i = new GameObject("Ambience").AddComponent<Ambience>();
            _i.transform.SetParent(parent, false);
        }

        void Start()
        {
            _city = Bed(CityHum());
            _crowd = Bed(Babble());
            _birds = Bed(Birdsong());
            _night = Bed(Crickets());
            _rooster = Rooster();
        }

        AudioSource Bed(AudioClip clip)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.Play();
            return src;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if ((_check -= dt) <= 0f)
            {
                _check = 0.5f;
                Listen();
            }
            float k = dt * 0.7f;
            if (_city) _city.volume = Mathf.MoveTowards(_city.volume, _cityWant, k * 0.3f);
            if (_crowd) _crowd.volume = Mathf.MoveTowards(_crowd.volume, _crowdWant, k * 0.5f);
            if (_birds) _birds.volume = Mathf.MoveTowards(_birds.volume, _birdWant, k * 0.4f);
            if (_night) _night.volume = Mathf.MoveTowards(_night.volume, _nightWant, k * 0.3f);

            // a rooster somewhere across the kampung, now and then
            if (_rooster != null && !VehicleVisuals.Night && _green > 0.9f && (_roosterT -= dt) <= 0f)
            {
                _roosterT = Random.Range(35f, 80f);
                var cam = Camera.main;
                if (cam != null)
                {
                    var off = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(25f, 45f);
                    ProcAudio.PlayAt(_rooster, cam.transform.position + off, 0.35f, Random.Range(0.92f, 1.08f), 0.7f, 120f);
                }
            }
        }

        /// <summary>Where are we, and what's around: sets the mix the beds ease towards.</summary>
        void Listen()
        {
            var pc = PlayerController.I;
            if (pc == null || GameManager.I == null) { _cityWant = 0.03f; _crowdWant = _birdWant = _nightWant = 0f; return; }
            var p = pc.Driving ? pc.Focus : pc.transform.position;
            _green = CityBuilder.Greenery(p);
            bool night = VehicleVisuals.Night;
            int n = 0;
            foreach (var ped in Pedestrian.All)
                if (ped != null && (ped.transform.position - p).sqrMagnitude < 24f * 24f) n++;
            // levels set against the music (clip RMS x volume, measured by OnFootCapture): the music sits at
            // about 0.02, a busy crowd just under it, the traffic hum and the birds well under
            // (the crowd is heard best on foot: in a car the engine covers it)
            _crowdWant = Mathf.Clamp01((n - 2) / 14f) * (pc.Driving ? 0.05f : 0.11f);
            _cityWant = (1f - _green) * (night ? 0.04f : 0.06f) + 0.012f;
            _birdWant = night ? 0f : _green * 0.15f;
            _nightWant = night ? 0.05f + _green * 0.1f : 0f;
        }

        // ------------------------------------------------------------------ synthesis
        /// <summary>A seamless loop: render a little past the end and fold the overhang back over the start.</summary>
        static AudioClip Loop(string name, float len, System.Func<float, float> f)
        {
            int n = Mathf.CeilToInt(len * Rate), xf = Rate / 2;
            var raw = new float[n + xf];
            for (int i = 0; i < raw.Length; i++) raw[i] = f(i / (float)Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = raw[i];
                if (i < xf)
                {
                    float a = i / (float)xf;
                    s = raw[i] * Mathf.Sqrt(a) + raw[n + i] * Mathf.Sqrt(1f - a);
                }
                data[i] = Mathf.Clamp(s, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip CityHum()
        {
            var rng = new System.Random(11);
            float b1 = 0f, b2 = 0f;
            const float len = 8f;
            return Loop("amb_city", len, t =>
            {
                float w = (float)rng.NextDouble() * 2f - 1f;
                b1 += (w - b1) * 0.02f;                  // deep rumble
                b2 += (w - b2) * 0.12f;                  // tyres and engines further off
                float swell = 0.65f + 0.35f * Mathf.Sin(t / len * 2f * Mathf.PI * 3f) * Mathf.Sin(t / len * 2f * Mathf.PI * 2f + 1f);
                return (b1 * 2.6f + b2 * 0.35f * swell) * 0.9f;
            });
        }

        static AudioClip Babble()
        {
            // a handful of people talking at once, a little way off: syllables of buzzing voice through
            // vowel formants that change every syllable
            var rng = new System.Random(5);
            const int V = 5;
            var f0 = new float[V];
            var rate = new float[V];
            var phase = new float[V];
            for (int v = 0; v < V; v++)
            {
                f0[v] = 105f + (float)rng.NextDouble() * 150f;
                rate[v] = 3.2f + (float)rng.NextDouble() * 2.4f;
                phase[v] = (float)rng.NextDouble() * 10f;
            }
            float lp = 0f;
            var nrng = new System.Random(6);
            // each voice's harmonic loudness is worked out once a syllable (the vowel), not every sample
            var amps = new float[V, 6];
            var syls = new int[V];
            var on = new bool[V];
            var ph = new float[V];                       // each voice's own phase (its pitch wanders)
            for (int v = 0; v < V; v++) syls[v] = int.MinValue;
            return Loop("amb_crowd", 7f, t =>
            {
                float sum = 0f;
                for (int v = 0; v < V; v++)
                {
                    float st = t * rate[v] + phase[v];
                    int syl = (int)st;
                    if (syl != syls[v])
                    {
                        syls[v] = syl;
                        uint h = (uint)(syl * 73856093 ^ v * 19349663);
                        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                        // phrases: speak for a few syllables, pause for breath
                        on[v] = (h & 7) >= 2;
                        float F1 = 320f + (h >> 4 & 15) * 32f, F2 = 950f + (h >> 8 & 15) * 85f;
                        for (int k = 0; k < 6; k++)
                        {
                            float fk = f0[v] * (k + 1);
                            amps[v, k] = Mathf.Exp(-Sq((fk - F1) / 260f)) + 0.55f * Mathf.Exp(-Sq((fk - F2) / 420f));
                        }
                    }
                    float pitch = f0[v] * (1f + 0.06f * Mathf.Sin(st * 1.7f));
                    ph[v] += pitch / Rate;
                    ph[v] -= Mathf.Floor(ph[v]);
                    if (!on[v]) continue;
                    float env = Mathf.Pow(Mathf.Sin((st - syl) * Mathf.PI), 1.5f);
                    float s = 0f;
                    for (int k = 0; k < 6; k++) s += Mathf.Sin(2f * Mathf.PI * (k + 1) * ph[v]) * amps[v, k];
                    sum += s * env;
                }
                lp += (sum - lp) * 0.3f;                   // muffled by distance
                return lp * 0.11f + ((float)nrng.NextDouble() * 2f - 1f) * 0.01f;
            });
        }

        static float Sq(float x) => x * x;

        static AudioClip Birdsong()
        {
            // a scatter of chirps and trills over a soft breeze
            var rng = new System.Random(21);
            const float len = 11f;
            int events = 26;
            var at = new float[events];
            var fa = new float[events];
            var fb = new float[events];
            var dur = new float[events];
            var reps = new int[events];
            var amp = new float[events];
            for (int e = 0; e < events; e++)
            {
                at[e] = (float)rng.NextDouble() * len;
                fa[e] = 2200f + (float)rng.NextDouble() * 2600f;
                fb[e] = fa[e] * (0.65f + (float)rng.NextDouble() * 0.8f);
                dur[e] = 0.04f + (float)rng.NextDouble() * 0.1f;
                reps[e] = 1 + rng.Next(5);
                amp[e] = 0.08f + (float)rng.NextDouble() * 0.22f;
            }
            var nrng = new System.Random(22);
            float breeze = 0f;
            return Loop("amb_birds", len, t =>
            {
                float s = 0f;
                for (int e = 0; e < events; e++)
                {
                    float local = t - at[e];
                    if (local < 0f || local > reps[e] * (dur[e] + 0.035f)) continue;
                    float gap = dur[e] + 0.035f;
                    int r = (int)(local / gap);
                    float u = local - r * gap;
                    if (u > dur[e]) continue;
                    float x = u / dur[e];
                    float f = Mathf.Lerp(fa[e], fb[e], x);
                    s += Mathf.Sin(2f * Mathf.PI * f * u) * Mathf.Sin(x * Mathf.PI) * amp[e];
                }
                breeze += (((float)nrng.NextDouble() * 2f - 1f) - breeze) * 0.03f;
                float gust = 0.6f + 0.4f * Mathf.Sin(t / len * 2f * Mathf.PI * 2f);
                return s + breeze * 0.6f * gust;
            });
        }

        static AudioClip Crickets()
        {
            const float len = 6f;
            var rng = new System.Random(31);
            return Loop("amb_night", len, t =>
            {
                float s = 0f;
                // three crickets: trains of quick pulses at slightly different pitches and rhythms
                for (int c = 0; c < 3; c++)
                {
                    float period = 0.62f + c * 0.13f;
                    float u = (t + c * 0.21f) % period;
                    if (u < 0.12f)
                    {
                        float pulse = (u * 33f) % 1f;
                        if (pulse < 0.55f) s += Mathf.Sin(2f * Mathf.PI * (4100f + c * 380f) * t) * Mathf.Sin(pulse / 0.55f * Mathf.PI) * (0.22f - c * 0.05f);
                    }
                }
                // frogs: a low "kwok" now and then
                for (int fr = 0; fr < 2; fr++)
                {
                    float period = 1.25f + fr * 0.55f;
                    float u = (t + fr * 0.4f) % period;
                    if (u < 0.09f) s += Mathf.Sin(2f * Mathf.PI * (260f + fr * 70f + u * 900f) * u) * Mathf.Sin(u / 0.09f * Mathf.PI) * 0.18f;
                }
                return s + ((float)rng.NextDouble() * 2f - 1f) * 0.006f;
            });
        }

        static AudioClip Rooster()
        {
            // "ku-ku-ru-yuuuk": four rough, rising notes and a long falling one
            var rng = new System.Random(41);
            float[] start = { 0f, 0.2f, 0.4f, 0.62f };
            float[] end = { 0.15f, 0.35f, 0.56f, 1.45f };
            float[] fs = { 560f, 700f, 760f, 900f };
            float[] fe = { 600f, 740f, 820f, 640f };
            float phase = 0f;
            return ProcAudio.MakeClip("rooster", 1.5f, t =>
            {
                for (int i = 0; i < 4; i++)
                {
                    if (t < start[i] || t > end[i]) continue;
                    float x = (t - start[i]) / (end[i] - start[i]);
                    float f = Mathf.Lerp(fs[i], fe[i], i == 3 ? x * x : x) * (1f + 0.025f * Mathf.Sin(t * 38f));
                    phase += f / 22050f;
                    float saw = 2f * (phase % 1f) - 1f;
                    float env = Mathf.Clamp01(x / 0.08f) * Mathf.Clamp01((1f - x) / 0.2f);
                    return (saw * 0.5f + Mathf.Sin(phase * 2f * Mathf.PI) * 0.4f + ((float)rng.NextDouble() * 2f - 1f) * 0.12f) * env * 0.5f;
                }
                return 0f;
            });
        }
    }
}
