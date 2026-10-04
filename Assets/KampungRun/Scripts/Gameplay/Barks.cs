using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Street chatter: a short line in a comic speech bubble over someone's head, said in their own babble
    /// voice (the way Hit &amp; Run's pedestrians quip as you pass, or Animal Crossing's villagers chat).
    /// Bubbles are pooled, follow the speaker, pop in and shrink away. Ambient lines (greetings, gossip,
    /// vendor calls) share one global pace so the street never turns into a wall of text; reactions
    /// (bumped, near-missed) jump the queue.
    /// </summary>
    public class Barks : MonoBehaviour
    {
        static Barks _i;
        static Barks I
        {
            get
            {
                if (_i == null) _i = new GameObject("Barks").AddComponent<Barks>();
                return _i;
            }
        }

        class Bubble
        {
            public GameObject go;
            public Transform box;
            public TextMesh text;
            public Transform follow;
            public Vector3 at;
            public float t, life, height;
        }

        readonly List<Bubble> _live = new List<Bubble>();
        readonly Stack<Bubble> _free = new Stack<Bubble>();
        static float _nextAmbient;
        const int MaxLive = 5;
        const float MaxDistance = 32f;

        /// <summary>Is the street quiet enough for another bit of ambient chatter?</summary>
        public static bool AmbientReady => Time.time >= _nextAmbient;

        /// <summary>
        /// Say a line over `who` (a bubble plus their voice). Ambient lines wait their turn and return false
        /// when they'd crowd the street or the speaker is too far from the camera to read.
        /// </summary>
        public static bool Say(Transform who, VoiceSynth.Profile voice, string voiceKey, string text, bool ambient,
            float height = 2.45f, float volume = 0.9f, bool excited = false)
        {
            if (who == null || string.IsNullOrEmpty(text)) return false;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - who.position).sqrMagnitude > MaxDistance * MaxDistance) return false;
            if (ambient)
            {
                if (Time.time < _nextAmbient) return false;
                _nextAmbient = Time.time + Random.Range(1.4f, 2.6f);
            }
            I.Show(who, who.position, text, height);
            if (voice != null) VoiceSynth.SayAt(voice, voiceKey, text, who.position + Vector3.up * height * 0.7f, volume, excited);
            ProcAudio.Play(ProcAudio.Bubble, who.position + Vector3.up * height, 0.12f, Random.Range(0.9f, 1.2f));
            return true;
        }

        /// <summary>Say a line in a named character's voice (the cast's profiles, or a stable made-up one).</summary>
        public static bool Say(Transform who, string speaker, string text, bool ambient, float height = 2.45f, float volume = 0.9f) =>
            Say(who, VoiceSynth.For(speaker), speaker, text, ambient, height, volume);

        void Show(Transform follow, Vector3 at, string text, float height)
        {
            // one bubble per speaker: a new line replaces their last one
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i].follow == follow && follow != null) Release(i);
            if (_live.Count >= MaxLive) Release(0);
            var b = _free.Count > 0 ? _free.Pop() : Create();
            b.go.SetActive(true);
            b.follow = follow;
            b.at = at;
            b.height = height;
            b.t = 0f;
            b.life = Mathf.Clamp(1.6f + text.Length * 0.055f, 1.8f, 4f);
            // size the board to the words (measured unrotated, then the bubble turns to the camera)
            b.go.transform.rotation = Quaternion.identity;
            b.go.transform.localScale = Vector3.one;
            b.text.text = Wrap(text, 22);
            var size = b.text.GetComponent<MeshRenderer>().bounds.size;
            b.box.localScale = new Vector3(size.x + 0.28f, size.y + 0.18f, 0.04f);
            _live.Add(b);
            Place(b);
        }

        Bubble Create()
        {
            var b = new Bubble { go = new GameObject("Bubble") };
            b.go.transform.SetParent(transform, false);
            var box = Shapes.Box("board", Vector3.zero, Vector3.one, Color.white, b.go.transform, false, 2.2f);
            box.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            box.layer = Layers.Pickup;
            b.box = box.transform;
            var tgo = new GameObject("words");
            tgo.transform.SetParent(b.go.transform, false);
            tgo.transform.localPosition = new Vector3(0, 0, -0.035f);     // just in front of the board
            b.text = tgo.AddComponent<TextMesh>();
            b.text.anchor = TextAnchor.MiddleCenter;
            b.text.alignment = TextAlignment.Center;
            b.text.characterSize = 0.075f;
            b.text.fontSize = 64;
            b.text.fontStyle = FontStyle.Bold;
            b.text.color = new Color(0.12f, 0.1f, 0.1f);
            b.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tgo.GetComponent<MeshRenderer>().sharedMaterial = b.text.font.material;
            tgo.layer = Layers.Pickup;
            return b;
        }

        /// <summary>Break a line into lines of about `max` characters at the spaces (a bubble, not a banner).</summary>
        static string Wrap(string text, int max)
        {
            if (text.Length <= max) return text;
            var sb = new System.Text.StringBuilder();
            int line = 0;
            foreach (var word in text.Split(' '))
            {
                if (line > 0 && line + 1 + word.Length > max) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(word);
                line += word.Length;
            }
            return sb.ToString();
        }

        void Release(int i)
        {
            var b = _live[i];
            _live.RemoveAt(i);
            b.follow = null;
            b.go.SetActive(false);
            _free.Push(b);
        }

        void Place(Bubble b)
        {
            var cam = Camera.main;
            var anchor = b.follow != null ? b.follow.position : b.at;
            var pos = anchor + Vector3.up * b.height;
            b.go.transform.position = pos;
            if (cam == null) return;
            var to = pos - cam.transform.position;
            b.go.transform.rotation = Quaternion.LookRotation(to);
            // pop in with a little overshoot, stay readable further away, shrink away at the end
            float pop = b.t < 0.14f ? Mathf.Lerp(0.2f, 1.15f, b.t / 0.14f) : b.t < 0.24f ? Mathf.Lerp(1.15f, 1f, (b.t - 0.14f) / 0.1f) : 1f;
            float fade = Mathf.Clamp01((b.life - b.t) / 0.18f);
            // a little bigger far off so it stays readable; close to the lens it shrinks away rather than
            // filling the screen (a speaker walking past the camera, or standing between it and you)
            float d = to.magnitude;
            float far = Mathf.Clamp(d / 9f, 1f, 2.3f);
            float near = Mathf.Clamp01((d - 2f) / 2f);              // (the hero, about 4 m off, stays full size)
            b.go.transform.localScale = Vector3.one * pop * fade * far * near;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var b = _live[i];
                b.t += dt;
                // the speaker vanished (recycled, destroyed): let the line finish where it was said
                if (b.follow != null && !b.follow.gameObject.activeInHierarchy) { b.at = b.follow.position; b.follow = null; }
                if (b.t >= b.life) { Release(i); continue; }
                Place(b);
            }
        }

        // ------------------------------------------------------------------ what people say
        // Short, local and a bit cheeky: a mix of Malay and Manglish, the way a KL street sounds.

        public static readonly string[] Greet =
        {
            "Assalamualaikum!", "Eh, {0}!", "Makan dah?", "Panas betul hari ni...", "Jalan-jalan ke?", "Apa khabar?",
            "Lama tak nampak!", "Nak ke mana tu?", "Hai!", "Kirim salam kat rumah!",
        };
        public static readonly string[] GreetKid = { "Hai pakcik!", "Nak main?", "Hehehe!", "Tengok ni!", "Kejar la!" };
        public static readonly string[] Bumped =
        {
            "HOI!", "Tengok jalan la!", "Aduh, kaki aku!", "Sabar la!", "Eh eh eh!", "Mata letak mana?", "Lain kali bagi salam!",
        };
        public static readonly string[] NearMiss =
        {
            "WOI! Bawa elok-elok!", "Gila ke?!", "Nak bunuh orang ke?!", "Lesen beli kat pasar malam ke?!", "MAK AI!", "Slow la sikit!",
        };
        public static readonly string[] Watch =
        {
            "Wah!", "Apa hal tu?", "Siapa nak bayar tu?", "Ish ish ish...", "Viral la ni!", "Ambik video, ambik video!", "Biar betul!",
        };
        public static readonly string[] Horned = { "Hon hon apa?!", "Sabar la!", "Ye, ye, aku tepi!", "Terkejut aku!" };
        public static readonly string[] Chatter =
        {
            "Hahaha!", "Betul tu!", "Eh, tau tak...", "Harga minyak naik lagi.", "Anak aku dapat 5A!", "Petang ni hujan kot.",
            "Jom minum teh tarik.", "Ish, biar betul!", "Lepas ni nak pergi pasar.", "Nasi lemak kat situ sedap.",
            "Bos aku tu, haih...", "Esok cuti kan?", "Jem teruk tadi!", "Hm hm...",
        };
        public static readonly string[] Cheer = { "Fuyoo!", "Terer!", "Power la!", "Wah, ganas!", "Hebat!" };

        /// <summary>A random line from a list, with {0} filled in (the player's name).</summary>
        public static string Pick(string[] lines, string name = null)
        {
            var s = lines[Random.Range(0, lines.Length)];
            return name != null ? string.Format(s, name) : s.Replace("{0}", "kawan");
        }
    }
}
