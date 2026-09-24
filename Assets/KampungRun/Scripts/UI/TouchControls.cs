using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KampungRun
{
    /// <summary>Buttons on the touch overlay.</summary>
    public enum TouchButton { Jump, Punch, Kick, Interact, Sprint, Horn, Reset, Pause }

    /// <summary>
    /// Touch input state, merged into <see cref="GameInput"/>. Filled once per frame by
    /// <see cref="TouchControls"/> (which runs before everything else), so "pressed this
    /// frame" is exact for every reader.
    /// </summary>
    public static class TouchInput
    {
        public static bool Active;                  // touch overlay showing
        public static Vector2 Move;                 // left thumb joystick, -1..1
        public static Vector2 Look;                 // right-side drag this frame (already scaled like mouse look)
        public static bool Tap;                     // any new touch this frame (dialogue advance)
        public static Vector2 TapPos;
        static readonly bool[] Held = new bool[8];
        static readonly int[] DownFrame = new int[8];

        public static bool Pressed(TouchButton b) => Active && DownFrame[(int)b] == Time.frameCount;
        public static bool IsHeld(TouchButton b) => Active && Held[(int)b];

        internal static void Set(TouchButton b, bool held)
        {
            int i = (int)b;
            if (held && !Held[i]) DownFrame[i] = Time.frameCount;
            Held[i] = held;
        }

        internal static void ClearButtons()
        {
            for (int i = 0; i < Held.Length; i++) Held[i] = false;
        }
    }

    /// <summary>
    /// On-screen controls for phones / tablets (and ?touch=1 on desktop browsers):
    /// a floating joystick where the left thumb lands, drag-to-look on the right half, and
    /// a thumb cluster of big round buttons. Hidden while menus / dialogue are up (those take
    /// taps directly). Reads raw touches (and the mouse when forced on) itself, so there is
    /// no EventSystem to set up.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class TouchControls : MonoBehaviour
    {
        public static TouchControls I { get; private set; }

        class Btn
        {
            public TouchButton id;
            public RectTransform rt;
            public Image img;
            public Text label;
            public bool drivingOnly, footOnly;
            public float radius;
        }

        RectTransform _root, _stickBase, _stickKnob;
        readonly List<Btn> _buttons = new List<Btn>();
        int _stickFinger = -1, _lookFinger = -1;
        Vector2 _stickOrigin, _lookLast;
        Canvas _canvas;
        const float StickRadius = 110f;                        // canvas units (1600 x 900 reference)
        static readonly Color Face = new Color(1f, 0.98f, 0.92f, 0.62f);
        static readonly Color FacePressed = new Color(1f, 0.85f, 0.35f, 0.85f);
        static readonly Color Ink = new Color(0.13f, 0.1f, 0.1f, 0.9f);

        /// <summary>Tests / debugging: show the overlay on any platform.</summary>
        public static bool ForceOn;

        /// <summary>Test hook: push the virtual stick as if a thumb were holding it.</summary>
        public static Vector2 DebugStick;

        /// <summary>Show touch controls on phones/tablets, or anywhere with ?touch=1 in the URL.</summary>
        public static bool Wanted
        {
            get
            {
                if (ForceOn || Application.isMobilePlatform) return true;
                string url = Application.absoluteURL ?? "";
                return url.Contains("touch=1");
            }
        }

        public static void Create(Canvas canvas)
        {
            if (I != null) return;
            var go = new GameObject("TouchControls", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            I = go.AddComponent<TouchControls>();
            I.Build(canvas);
        }

        void Build(Canvas canvas)
        {
            _canvas = canvas;
            TouchInput.Active = Wanted;          // known before the first menu opens (hint text, controls page)
            _root = (RectTransform)transform;
            _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            _stickBase = Circle("StickBase", 2 * StickRadius, new Color(1f, 1f, 1f, 0.22f));
            _stickKnob = Circle("StickKnob", 96, new Color(1f, 0.98f, 0.92f, 0.7f));
            _stickKnob.SetParent(_stickBase, false);
            _stickBase.gameObject.SetActive(false);
            // right-thumb cluster (anchored bottom-right), top-row utility buttons
            Add(TouchButton.Jump, "LOMPAT", new Vector2(-150, 120), 78);
            Add(TouchButton.Punch, "TUMBUK", new Vector2(-300, 95), 66, footOnly: true);
            Add(TouchButton.Kick, "TENDANG", new Vector2(-150, 280), 66, footOnly: true);
            Add(TouchButton.Interact, "E", new Vector2(-330, 250), 56);
            Add(TouchButton.Sprint, "LARI", new Vector2(-450, 90), 50, footOnly: true);
            Add(TouchButton.Horn, "HON", new Vector2(-300, 95), 60, drivingOnly: true);
            Add(TouchButton.Reset, "BALIK", new Vector2(-150, 280), 56, drivingOnly: true);
            Add(TouchButton.Pause, "II", new Vector2(0, -52), 40, anchorTop: true);
        }

        RectTransform Circle(string name, float size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = RoundSprite();
            img.color = c;
            img.raycastTarget = false;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = Ink; ol.effectDistance = new Vector2(3, -3);
            return rt;
        }

        void Add(TouchButton id, string text, Vector2 pos, float radius, bool drivingOnly = false, bool footOnly = false,
            bool anchorTop = false)
        {
            var rt = Circle("Btn_" + id, radius * 2, Face);
            rt.anchorMin = rt.anchorMax = anchorTop ? new Vector2(0.5f, 1) : new Vector2(1, 0);   // pause: top centre, clear of the HUD panels
            rt.anchoredPosition = pos;
            var lgo = new GameObject("Label", typeof(RectTransform));
            var lrt = (RectTransform)lgo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var t = lgo.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = text.Length > 4 ? 20 : 28;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Ink;
            t.raycastTarget = false;
            _buttons.Add(new Btn { id = id, rt = rt, img = rt.GetComponent<Image>(), label = t, drivingOnly = drivingOnly,
                footOnly = footOnly, radius = radius });
        }

        static Sprite _round;
        static Sprite RoundSprite()
        {
            if (_round) return _round;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    byte a = (byte)(Mathf.Clamp01((1f - d) * 24f) * 255);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _round = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
            return _round;
        }

        // ------------------------------------------------------------------ per frame
        struct Pointer { public int id; public Vector2 pos; public bool began, ended; }
        readonly List<Pointer> _ptrs = new List<Pointer>();

        void GatherPointers()
        {
            _ptrs.Clear();
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                {
                    var ph = t.phase.ReadValue();
                    if (ph == UnityEngine.InputSystem.TouchPhase.None) continue;
                    bool ended = ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled;
                    _ptrs.Add(new Pointer { id = t.touchId.ReadValue(), pos = t.position.ReadValue(),
                        began = t.press.wasPressedThisFrame || ph == UnityEngine.InputSystem.TouchPhase.Began, ended = ended });
                }
            // mouse acts as one finger when no real touches (desktop ?touch=1, emulators)
            var ms = Mouse.current;
            if (_ptrs.Count == 0 && ms != null && (ms.leftButton.isPressed || ms.leftButton.wasReleasedThisFrame))
                _ptrs.Add(new Pointer { id = -99, pos = ms.position.ReadValue(), began = ms.leftButton.wasPressedThisFrame,
                    ended = ms.leftButton.wasReleasedThisFrame });
        }

        Vector2 ToCanvas(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var lp);
            return lp;
        }

        void Update()
        {
            TouchInput.Active = Wanted;
            bool show = TouchInput.Active && GameManager.I != null && GameManager.I.InWorld && !GameInput.Locked &&
                        !(HUD.I != null && HUD.I.MenuOpen);
            TouchInput.Move = Vector2.zero;
            TouchInput.Look = Vector2.zero;
            TouchInput.Tap = false;
            if (!TouchInput.Active)
            {
                // (the widgets are children: this object itself must stay active to keep polling)
                foreach (var b in _buttons) b.rt.gameObject.SetActive(false);
                _stickBase.gameObject.SetActive(false);
                return;
            }
            GatherPointers();
            foreach (var p in _ptrs) if (p.began) { TouchInput.Tap = true; TouchInput.TapPos = p.pos; }

            bool driving = PlayerController.I != null && PlayerController.I.Driving;
            foreach (var b in _buttons)
            {
                bool visible = show && !(b.drivingOnly && !driving) && !(b.footOnly && driving);
                b.rt.gameObject.SetActive(visible);
                if (b.id == TouchButton.Jump) b.label.text = driving ? "BREK" : "LOMPAT";   // handbrake in a car
            }
            if (!show)
            {
                TouchInput.ClearButtons();
                _stickFinger = _lookFinger = -1;
                _stickBase.gameObject.SetActive(false);
                return;
            }

            // buttons: held while any finger is on them
            var held = new bool[8];
            var claimed = new HashSet<int>();
            foreach (var p in _ptrs)
            {
                if (p.ended) continue;
                foreach (var b in _buttons)
                {
                    if (!b.rt.gameObject.activeSelf) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(b.rt, p.pos, null) &&
                        Vector2.Distance(ToCanvas(p.pos), (Vector2)_root.InverseTransformPoint(b.rt.position)) <= b.radius * 1.1f)
                    {
                        held[(int)b.id] = true;
                        claimed.Add(p.id);
                    }
                }
            }
            foreach (var b in _buttons)
            {
                TouchInput.Set(b.id, held[(int)b.id]);
                b.img.color = held[(int)b.id] ? FacePressed : Face;
            }

            // joystick: a finger that starts on the left half owns it until lifted
            float half = Screen.width * 0.45f;
            bool stickAlive = false, lookAlive = false;
            foreach (var p in _ptrs)
            {
                if (claimed.Contains(p.id)) continue;
                if (p.id == _stickFinger || (_stickFinger == -1 && p.began && p.pos.x < half))
                {
                    if (p.ended) { _stickFinger = -1; continue; }
                    if (_stickFinger != p.id) { _stickFinger = p.id; _stickOrigin = ToCanvas(p.pos); }
                    stickAlive = true;
                    var d = ToCanvas(p.pos) - _stickOrigin;
                    if (d.magnitude > StickRadius) { _stickOrigin += d - d.normalized * StickRadius; d = d.normalized * StickRadius; }
                    TouchInput.Move = d / StickRadius;
                    _stickBase.localPosition = _stickOrigin;
                    _stickKnob.localPosition = d;
                }
                else if (p.id == _lookFinger || (_lookFinger == -1 && p.began && p.pos.x >= half))
                {
                    if (p.ended) { _lookFinger = -1; continue; }
                    if (_lookFinger != p.id) { _lookFinger = p.id; _lookLast = p.pos; }
                    lookAlive = true;
                    var delta = p.pos - _lookLast;
                    _lookLast = p.pos;
                    TouchInput.Look = delta * (1600f / Mathf.Max(1, Screen.width)) * 0.16f;
                }
            }
            if (!stickAlive) _stickFinger = -1;
            if (!lookAlive) _lookFinger = -1;
            _stickBase.gameObject.SetActive(stickAlive);
            // deadzone + a touch of curve for fine steering
            if (DebugStick != Vector2.zero) TouchInput.Move = DebugStick;
            var m = TouchInput.Move;
            TouchInput.Move = m.magnitude < 0.12f ? Vector2.zero : m.normalized * Mathf.Pow((m.magnitude - 0.12f) / 0.88f, 1.2f);
        }

        /// <summary>For menus: the menu item index under a tap this frame, or -1.</summary>
        public static int TappedIndex(List<Text> items)
        {
            if (!TouchInput.Active || !TouchInput.Tap) return -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].gameObject.activeInHierarchy) continue;
                var rt = items[i].rectTransform;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, TouchInput.TapPos, null)) return i;
            }
            return -1;
        }
    }
}
