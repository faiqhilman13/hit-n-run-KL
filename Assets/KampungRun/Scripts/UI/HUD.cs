using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KampungRun
{
    public struct Line
    {
        public string speaker, text;
        public Line(string speaker, string text) { this.speaker = speaker; this.text = text; }
    }

    /// <summary>Conversation boxes. Input is locked while one is showing.</summary>
    public static class Dialogue
    {
        public static bool Showing => HUD.I != null && HUD.I.DialogueOpen;
        public static void Say(Line[] lines, Action onDone = null)
        {
            if (lines == null || lines.Length == 0 || HUD.I == null) { onDone?.Invoke(); return; }
            HUD.I.ShowDialogue(lines, onDone);
        }
    }

    /// <summary>
    /// All screen UI, built in code with the look of a newspaper strip: paper-coloured
    /// panels, ink borders, chunky lettering.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        public static HUD I { get; private set; }

        static readonly Color Paper = new Color(0.98f, 0.95f, 0.86f, 0.96f);
        static readonly Color Ink = new Color(0.08f, 0.07f, 0.06f, 1f);
        static readonly Color Red = new Color(0.82f, 0.22f, 0.18f, 1f);
        static readonly Color Gold = new Color(0.95f, 0.75f, 0.2f, 1f);

        Font _font;
        Canvas _canvas;
        RectTransform _root;
        Text _coins, _collect, _objective, _timer, _progress, _prompt, _speed, _district, _toast, _bigTitle, _bigSub;
        Image _heatFill, _bustFill, _heatPanel, _carHealthFill, _targetFill;
        GameObject _targetBar, _speedPanel, _objPanel, _bigPanel, _arrowGO;
        RectTransform _arrow;
        Text _arrowDist;
        float _toastTime, _bigTime, _districtTime;
        string _lastDistrict;

        // dialogue
        GameObject _dlgPanel;
        Text _dlgName, _dlgText, _dlgHint;
        Line[] _lines;
        int _lineIdx;
        float _typed;
        Action _dlgDone;
        public bool DialogueOpen => _dlgPanel != null && _dlgPanel.activeSelf;
        int _dlgOpenFrame, _menuOpenFrame;
        float _typeRate = 60f;

        // menu
        GameObject _menuPanel;
        Text _menuTitle, _menuHint, _footnote;

        /// <summary>A small line in the bottom corner (map data credit on the title screen); null clears it.</summary>
        public void Footnote(string text) { if (_footnote) _footnote.text = text ?? ""; }
        readonly List<Text> _menuItems = new List<Text>();
        List<string> _menuOptions;
        Action<int> _menuPick;
        Action _menuCancel;
        int _menuSel;
        public bool MenuOpen => _menuPanel != null && _menuPanel.activeSelf;

        void Awake()
        {
            I = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Canvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            _root = go.GetComponent<RectTransform>();
            Build();
            TouchControls.Create(_canvas);
        }

        // ------------------------------------------------------------------ builders
        RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, bool border = true)
        {
            var rt = Rect(name, parent, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (border)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Ink;
                o.effectDistance = new Vector2(3, -3);
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0, 0, 0, 0.35f);
                s.effectDistance = new Vector2(6, -6);
            }
            return img;
        }

        Text Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, bool outline = false)
        {
            var rt = Rect(name, parent, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Ink;
                o.effectDistance = new Vector2(2.5f, -2.5f);
            }
            return t;
        }

        Image Bar(string name, Transform parent, Vector2 pos, Vector2 size, Color fill, out Image fillImg, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0, 1);
            var bg = Panel(name, parent, a, new Vector2(0, 1), pos, size, new Color(0.85f, 0.82f, 0.74f), true);
            var f = Rect("Fill", bg.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, size);
            f.anchorMin = new Vector2(0, 0);
            f.anchorMax = new Vector2(0, 1);
            f.sizeDelta = new Vector2(size.x, 0);
            fillImg = f.gameObject.AddComponent<Image>();
            fillImg.color = fill;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.sprite = WhiteSprite();
            return bg;
        }

        static Sprite _triangle;

        /// <summary>An upward ink-bordered triangle (the built-in font has no arrow glyphs).</summary>
        static Sprite TriangleSprite()
        {
            if (_triangle != null) return _triangle;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // distance inside a triangle with apex at the top centre
                    float fx = (x + 0.5f) / n, fy = (y + 0.5f) / n;
                    float half = (1f - fy) * 0.5f;          // half-width at this height
                    float edge = Mathf.Min(half - Mathf.Abs(fx - 0.5f), fy - 0.06f);
                    byte a = (byte)(Mathf.Clamp01(edge * n * 0.5f + 0.5f) * 255);
                    bool ink = edge < 0.07f;
                    byte v = ink ? (byte)25 : (byte)255;
                    px[y * n + x] = new Color32(v, v, v, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _triangle = Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return _triangle;
        }

        static Sprite _white;
        static Sprite WhiteSprite()
        {
            if (_white == null)
            {
                var tex = new Texture2D(4, 4);
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px);
                tex.Apply();
                _white = Sprite.Create(tex, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
            return _white;
        }

        void Build()
        {
            // (paper overlay retired for the Simpsons-hybrid look)

            // Saman meter (top-left)
            _heatPanel = Panel("Saman", _root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(300, 70), Paper);
            Label("Lbl", _heatPanel.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -4), new Vector2(200, 28), 20, Ink, TextAnchor.UpperLeft).text = "METER SAMAN";
            Bar("HeatBar", _heatPanel.transform, new Vector2(12, -32), new Vector2(276, 18), Red, out _heatFill);
            Bar("BustBar", _heatPanel.transform, new Vector2(12, -54), new Vector2(276, 8), Ink, out _bustFill);

            // Money + collectibles (top-right)
            var money = Panel("Money", _root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -20), new Vector2(260, 92), Paper);
            _coins = Label("Coins", money.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(250, 46), 36, Ink);
            _collect = Label("Collect", money.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(250, 36), 17, Ink);

            // Objective (top-centre)
            var obj = Panel("Objective", _root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(560, 84), Paper);
            _objPanel = obj.gameObject;
            _objective = Label("Text", obj.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-40, -8), new Vector2(460, 44), 22, Ink);
            _progress = Label("Progress", obj.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-40, 6), new Vector2(460, 28), 18, Red);
            _timer = Label("Timer", obj.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(100, 60), 34, Red);
            _targetBar = Bar("Target", obj.transform, new Vector2(40, -88), new Vector2(480, 14), Red, out _targetFill, new Vector2(0, 1)).gameObject;

            // compass arrow under the objective
            var ar = Rect("Arrow", _root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(60, 60));
            _arrowGO = ar.gameObject;
            _arrow = ar;
            var at = ar.gameObject.AddComponent<Image>();
            at.sprite = TriangleSprite();
            at.color = Gold;
            at.raycastTarget = false;
            _arrowDist = Label("Dist", _root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -192), new Vector2(160, 30), 20, Ink);

            BuildMinimap();

            // prompt, district, toast
            _prompt = Label("Prompt", _root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(900, 40), 26, Paper, TextAnchor.MiddleCenter, true);
            _district = Label("District", _root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 272), new Vector2(600, 50), 34, Paper, TextAnchor.LowerLeft, true);
            _toast = Label("Toast", _root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -210), new Vector2(900, 40), 26, Gold, TextAnchor.MiddleCenter, true);
            _footnote = Label("Footnote", _root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 8), new Vector2(700, 24), 15, Ink, TextAnchor.LowerRight, false);
            _footnote.text = "";

            // speedo + car health (bottom-right)
            var sp = Panel("Speedo", _root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(220, 86), Paper);
            _speedPanel = sp.gameObject;
            _speed = Label("Speed", sp.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(210, 50), 34, Ink);
            Bar("Health", sp.transform, new Vector2(12, -58), new Vector2(196, 14), new Color(0.4f, 0.65f, 0.35f), out _carHealthFill);

            // big centre message
            var big = Panel("Big", _root, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 150), Paper);
            _bigPanel = big.gameObject;
            _bigTitle = Label("Title", big.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(740, 80), 54, Red, TextAnchor.MiddleCenter);
            _bigSub = Label("Sub", big.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(740, 50), 28, Ink);
            _bigPanel.SetActive(false);

            // dialogue box
            var dlg = Panel("Dialogue", _root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1100, 170), Paper);
            _dlgPanel = dlg.gameObject;
            var namePanel = Panel("NameTag", dlg.transform, new Vector2(0, 1), new Vector2(0, 0), new Vector2(20, -8), new Vector2(260, 44), Gold);
            _dlgName = Label("Name", namePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250, 40), 26, Ink);
            _dlgText = Label("Text", dlg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(1040, 120), 28, Ink, TextAnchor.MiddleLeft);
            _dlgHint = Label("Hint", dlg.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 6), new Vector2(200, 28), 18, Red, TextAnchor.LowerRight);
            _dlgHint.text = "[E] ▶";
            _dlgPanel.SetActive(false);

            // menu
            var menu = Panel("Menu", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 520), Paper);
            _menuPanel = menu.gameObject;
            _menuTitle = Label("Title", menu.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(620, 60), 38, Red);
            for (int i = 0; i < 9; i++)
                _menuItems.Add(Label("Item" + i, menu.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90 - i * 44), new Vector2(600, 42), 26, Ink));
            _menuHint = Label("Hint", menu.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(620, 30), 17, Ink);
            _menuHint.text = "W/S pilih  -  E/Enter ok  -  Esc batal";
            _menuPanel.SetActive(false);
        }

        Camera _mapCam;
        GameObject _mapPanel;
        RectTransform _mapPlayer;

        /// <summary>H&amp;R-style radar: a top-down orthographic view that turns with the camera.</summary>
        void BuildMinimap()
        {
            var panel = Panel("Minimap", _root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(236, 236), Paper);
            _mapPanel = panel.gameObject;
            var rt = new RenderTexture(256, 256, 16) { name = "MinimapRT" };
            var img = Rect("Map", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(224, 224));
            var raw = img.gameObject.AddComponent<RawImage>();
            raw.texture = rt;
            raw.raycastTarget = false;
            var arrowRt = Rect("Me", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
            var arrow = arrowRt.gameObject.AddComponent<Image>();
            arrow.sprite = TriangleSprite();
            arrow.color = Red;

            _mapPlayer = arrowRt;
            Label("N", panel.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(40, 24), 16, Ink).text = "";

            var camGo = new GameObject("MinimapCamera");
            camGo.transform.SetParent(transform, false);
            _mapCam = camGo.AddComponent<Camera>();
            _mapCam.orthographic = true;
            _mapCam.orthographicSize = 75f;
            _mapCam.targetTexture = rt;
            _mapCam.clearFlags = CameraClearFlags.SolidColor;
            _mapCam.backgroundColor = new Color(0.9f, 0.87f, 0.78f);
            _mapCam.farClipPlane = 1000f;
            _mapCam.cullingMask = ~((1 << Layers.Character) | (1 << Layers.Pickup));
            _mapCam.depth = -10;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_mapCam);
            data.renderShadows = false;
            data.renderPostProcessing = false;
        }

        void UpdateMinimap(PlayerController p)
        {
            bool on = p != null;
            _mapPanel.SetActive(on);
            _mapCam.enabled = on;
            if (!on) return;
            var cam = Camera.main;
            float yaw = cam ? cam.transform.eulerAngles.y : 0f;
            _mapCam.transform.SetPositionAndRotation(new Vector3(p.Focus.x, 600f, p.Focus.z), Quaternion.Euler(90f, yaw, 0f));   // above Merdeka 118
            _mapCam.orthographicSize = p.Driving ? 150f : 85f;
            float heading = p.Driving ? p.vehicle.transform.eulerAngles.y : p.transform.eulerAngles.y;
            _mapPlayer.localRotation = Quaternion.Euler(0, 0, -(heading - yaw));
        }

        void PaperOverlay()
        {
            // subtle paper fibres over everything: the whole game is "printed"
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            var rng = new System.Random(5);
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % n, y = i / n;
                float fib = Mathf.PerlinNoise(x * 0.05f, y * 0.9f) * 0.6f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.4f;
                float speck = rng.NextDouble() < 0.004 ? 1f : 0f;
                byte a = (byte)(Mathf.Clamp01(fib * 0.12f + speck * 0.25f) * 255);
                byte v = (byte)(speck > 0 ? 60 : 235);
                px[i] = new Color32(v, (byte)(v - 8), (byte)(v - 25), a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            var rt = Rect("Paper", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.uvRect = new UnityEngine.Rect(0, 0, 6, 3.4f);
            raw.raycastTarget = false;
        }

        // ------------------------------------------------------------------ API
        public void Prompt(string text)
        {
            if (_prompt) _prompt.text = text ?? "";
        }

        public void Toast(string text)
        {
            _toast.text = text;
            _toastTime = 2.5f;
        }

        public void BigMessage(string title, string sub)
        {
            _bigTitle.text = title;
            _bigSub.text = sub;
            _bigPanel.SetActive(true);
            _bigTime = 3f;
            _bigPanel.transform.localScale = Vector3.one * 1.4f;
        }

        public void ShowDialogue(Line[] lines, Action onDone)
        {
            _lines = lines;
            _lineIdx = 0;
            _typed = 0;
            _dlgDone = onDone;
            _dlgPanel.SetActive(true);
            _dlgOpenFrame = Time.frameCount;
            GameInput.Locked = true;
            ShowLine();
        }

        void ShowLine()
        {
            var l = _lines[_lineIdx];
            _dlgName.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(l.speaker));
            _dlgName.text = l.speaker;
            _typed = 0;
            // speak it; type the text out at the same pace as the voice
            float dur = VoiceSynth.Speak(l.speaker, l.text);
            int len = (l.text ?? "").Length;
            _typeRate = dur > 0.3f ? Mathf.Clamp(len / (dur * 0.95f), 18f, 90f) : 60f;
            if (dur <= 0f) ProcAudio.Play2D(ProcAudio.Blip, 0.2f, 1.5f);
        }

        /// <summary>Tests: click through every open dialogue (including ones opened by callbacks).</summary>
        public void DebugSkipDialogue()
        {
            VoiceSynth.Stop();
            for (int guard = 0; guard < 50 && DialogueOpen; guard++)
            {
                _dlgPanel.SetActive(false);
                GameInput.Locked = MenuOpen;
                var done = _dlgDone;
                _dlgDone = null;
                done?.Invoke();
            }
        }

        public void Menu(string title, List<string> options, Action<int> onPick, Action onCancel = null)
        {
            _menuTitle.text = title;
            _menuOptions = options;
            _menuPick = onPick;
            _menuCancel = onCancel;
            _menuSel = 0;
            _menuPanel.SetActive(true);
            _menuOpenFrame = Time.frameCount;
            GameInput.Locked = true;
            RefreshMenu();
        }

        public void CloseMenu()
        {
            _menuPanel.SetActive(false);
            GameInput.Locked = DialogueOpen;
        }

        void RefreshMenu()
        {
            for (int i = 0; i < _menuItems.Count; i++)
            {
                bool on = i < _menuOptions.Count;
                _menuItems[i].gameObject.SetActive(on);
                if (!on) continue;
                _menuItems[i].text = (i == _menuSel ? "▶ " : "   ") + _menuOptions[i];
                _menuItems[i].color = i == _menuSel ? Red : Ink;
            }
            if (_menuHint) _menuHint.text = TouchInput.Active ? "Ketik pilihan" : "W/S pilih  -  E/Enter ok  -  Esc batal";
        }

        // ------------------------------------------------------------------ per-frame
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateMenu();
            UpdateDialogue(dt);

            var gm = GameManager.I;
            bool playing = gm != null && gm.InWorld;
            _heatPanel.gameObject.SetActive(playing);
            // touch layout: the thumb buttons own the bottom-right, so the speedo sits bottom-centre
            var spRt = (RectTransform)_speedPanel.transform;
            bool touch = TouchInput.Active;
            spRt.anchorMin = spRt.anchorMax = spRt.pivot = touch ? new Vector2(0.5f, 0) : new Vector2(1, 0);
            spRt.anchoredPosition = touch ? new Vector2(0, 18) : new Vector2(-24, 24);
            _coins.transform.parent.gameObject.SetActive(playing);
            _district.gameObject.SetActive(playing);
            if (!playing) { _objPanel.SetActive(false); _arrowGO.SetActive(false); _arrowDist.text = ""; _speedPanel.SetActive(false); UpdateMinimap(null); return; }

            // money + collectibles
            _coins.text = $"RM {GameState.Coins}";
            int lvl = GameState.Level;
            _collect.text = $"Kad {GameState.CardsInLevel(lvl)}/{GameData.CardsPerLevel}   Burung {GameState.CamerasInLevel(lvl)}/{GameData.CamerasPerLevel}   {GameState.Percent}%";

            // saman
            var sm = SamanMeter.I;
            if (sm)
            {
                _heatFill.fillAmount = sm.heat / 100f;
                _bustFill.fillAmount = sm.BustProgress;
                _heatFill.color = sm.Wanted ? (Mathf.Repeat(Time.time * 3f, 1f) < 0.5f ? Red : new Color(0.2f, 0.3f, 0.75f)) : Red;
            }

            // objective
            var mm = gm.Missions;
            var step = mm != null ? mm.Step : null;
            bool hasObj = step != null && !string.IsNullOrEmpty(step.text);
            _objPanel.SetActive(hasObj);
            Vector3? target = step?.Target;
            if (hasObj)
            {
                _objective.text = step.text;
                _progress.text = step.Progress ?? "";
                _timer.text = step.timeLimit > 0 ? $"{Mathf.CeilToInt(Mathf.Max(0, mm.TimeLeft))}" : "";
                _timer.color = mm.TimeLeft < 10 ? Red : Ink;
                var th = step.TargetHealth;
                _targetBar.SetActive(th.HasValue);
                if (th.HasValue) _targetFill.fillAmount = th.Value;
            }

            // compass arrow
            var p = PlayerController.I;
            _arrowGO.SetActive(target.HasValue && p != null);
            if (target.HasValue && p != null && Camera.main)
            {
                var to = target.Value - p.Focus;
                to.y = 0;
                float camYaw = Camera.main.transform.eulerAngles.y;
                float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - camYaw;
                _arrow.localRotation = Quaternion.Euler(0, 0, -ang);
                _arrowDist.text = $"{Mathf.RoundToInt(to.magnitude)}m";
            }
            else _arrowDist.text = "";

            UpdateMinimap(p);

            // speedo
            bool driving = p != null && p.Driving;
            _speedPanel.SetActive(driving);
            if (driving)
            {
                _speed.text = $"{Mathf.RoundToInt(p.vehicle.SpeedKmh)} km/j";
                _carHealthFill.fillAmount = p.vehicle.health / p.vehicle.maxHealth;
            }

            // district callout
            if (p != null)
            {
                var d = CityBuilder.District(p.Focus);
                if (d != _lastDistrict) { _lastDistrict = d; _districtTime = 3f; _district.text = d; }
                _districtTime -= dt;
                var c = _district.color; c.a = Mathf.Clamp01(_districtTime); _district.color = c;
            }

            _toastTime -= dt;
            _toast.gameObject.SetActive(_toastTime > 0);
            if (_bigTime > 0)
            {
                _bigTime -= dt;
                _bigPanel.transform.localScale = Vector3.Lerp(_bigPanel.transform.localScale, Vector3.one, dt * 10f);
                if (_bigTime <= 0) _bigPanel.SetActive(false);
            }
        }

        void UpdateDialogue(float dt)
        {
            if (!DialogueOpen) return;
            var full = _lines[_lineIdx].text ?? "";
            _typed += dt * _typeRate;
            int n = Mathf.Min(full.Length, (int)_typed);
            _dlgText.text = full.Substring(0, n);
            if (GameInput.ConfirmDown && !MenuOpen && Time.frameCount > _dlgOpenFrame)
            {
                if (n < full.Length) { _typed = full.Length; return; }
                _lineIdx++;
                VoiceSynth.Stop();
                if (_lineIdx >= _lines.Length)
                {
                    _dlgPanel.SetActive(false);
                    GameInput.Locked = MenuOpen;
                    var done = _dlgDone;
                    _dlgDone = null;
                    done?.Invoke();
                }
                else ShowLine();
            }
        }

        void UpdateMenu()
        {
            if (!MenuOpen || Time.frameCount <= _menuOpenFrame) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool up = (kb != null && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)) || (gp != null && gp.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)) || (gp != null && gp.dpad.down.wasPressedThisFrame);
            bool ok = (kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            bool cancel = (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (gp != null && gp.buttonEast.wasPressedThisFrame);
            if (_menuHint) _menuHint.text = TouchInput.Active ? "Ketik pilihan" : "W/S pilih  -  E/Enter ok  -  Esc batal";
            int tapped = TouchControls.TappedIndex(_menuItems);                 // touch: tap an option to pick it
            if (tapped >= 0 && tapped < _menuOptions.Count) { _menuSel = tapped; ok = true; }
            if (up) { _menuSel = (_menuSel + _menuOptions.Count - 1) % _menuOptions.Count; RefreshMenu(); ProcAudio.Play2D(ProcAudio.Blip, 0.2f); }
            if (down) { _menuSel = (_menuSel + 1) % _menuOptions.Count; RefreshMenu(); ProcAudio.Play2D(ProcAudio.Blip, 0.2f); }
            if (ok)
            {
                var pick = _menuPick;
                int sel = _menuSel;
                CloseMenu();
                pick?.Invoke(sel);
            }
            else if (cancel)
            {
                var c = _menuCancel;
                CloseMenu();
                c?.Invoke();
            }
        }
    }
}
