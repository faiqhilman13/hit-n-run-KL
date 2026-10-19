using UnityEngine;
using UnityEngine.UI;

namespace KampungRun
{
    /// <summary>
    /// The extra mission read-outs the new objective types need, on their own canvas so the main HUD stays as it is:
    /// a labelled meter under the objective panel (spill, suspicion, the stall's health, saman strikes...), a one-line
    /// hint under it, a camera viewfinder for the photo missions, and a white flash for the shutter. Every element
    /// hides itself unless an objective set it this frame, so a failed or finished mission never leaves one behind.
    /// </summary>
    public class MissionOverlay : MonoBehaviour
    {
        static MissionOverlay _i;

        static readonly Color Paper = new Color(0.98f, 0.95f, 0.86f, 0.96f);
        static readonly Color Ink = new Color(0.08f, 0.07f, 0.06f, 1f);

        RectTransform _root;
        GameObject _meterPanel, _finder;
        Image _meterFill, _finderFill, _flash;
        Text _meterLabel, _hint, _finderText;
        int _meterFrame = -9, _hintFrame = -9, _finderFrame = -9;
        float _flashT;
        Font _font;

        static MissionOverlay I
        {
            get
            {
                if (_i == null && HUD.I != null)
                {
                    var go = new GameObject("MissionOverlay");
                    go.transform.SetParent(HUD.I.transform, false);
                    _i = go.AddComponent<MissionOverlay>();
                }
                return _i;
            }
        }

        /// <summary>Show a meter this frame (value 0..1).</summary>
        public static void Meter(string label, float value, Color color)
        {
            var o = I;
            if (o == null) return;
            o._meterFrame = Time.frameCount;
            o._meterLabel.text = label;
            o._meterFill.fillAmount = Mathf.Clamp01(value);
            o._meterFill.color = color;
        }

        /// <summary>A line of advice under the objective this frame ("Jangan lari dekat pengawal!").</summary>
        public static void Hint(string text)
        {
            var o = I;
            if (o == null || string.IsNullOrEmpty(text)) return;
            o._hintFrame = Time.frameCount;
            o._hint.text = text;
        }

        /// <summary>The photo viewfinder this frame; fill = how steady the shot is.</summary>
        public static void Viewfinder(float fill, string text)
        {
            var o = I;
            if (o == null) return;
            o._finderFrame = Time.frameCount;
            o._finderFill.fillAmount = Mathf.Clamp01(fill);
            o._finderText.text = text ?? "";
        }

        public static void Flash() { var o = I; if (o != null) o._flashT = 0.35f; }

        public static bool MeterShown => _i != null && _i._meterPanel.activeSelf;

        void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Canvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9;          // under the main HUD's dialogue and menus
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            _root = go.GetComponent<RectTransform>();

            // meter: a paper strip under the Meter Saman (top left)
            var mp = Panel("Meter", _root, new Vector2(0, 1), new Vector2(24, -102), new Vector2(300, 52), Paper, true, new Vector2(0, 1));
            _meterPanel = mp.gameObject;
            _meterLabel = Label("Label", mp.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -2), new Vector2(280, 24), 17, Ink, TextAnchor.UpperLeft);
            var bg = Panel("Bar", mp.transform, new Vector2(0, 0), new Vector2(10, 6), new Vector2(280, 14), new Color(0.85f, 0.82f, 0.74f), false, new Vector2(0, 0));
            _meterFill = Fill(bg.transform);

            _hint = Label("Hint", _root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -262), new Vector2(1000, 34), 22, Paper, TextAnchor.MiddleCenter);
            var ol = _hint.gameObject.AddComponent<Outline>();
            ol.effectColor = Ink;
            ol.effectDistance = new Vector2(2f, -2f);

            // viewfinder: four corner brackets round the middle of the screen and a steadiness bar
            var vf = new GameObject("Viewfinder", typeof(RectTransform)).GetComponent<RectTransform>();
            vf.SetParent(_root, false);
            vf.anchorMin = vf.anchorMax = vf.pivot = new Vector2(0.5f, 0.5f);
            vf.anchoredPosition = new Vector2(0, 40);
            vf.sizeDelta = new Vector2(560, 380);
            _finder = vf.gameObject;
            foreach (var c in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
            {
                Corner(vf, c, true);
                Corner(vf, c, false);
            }
            var fb = Panel("Steady", vf, new Vector2(0.5f, 0), new Vector2(-150, -36), new Vector2(300, 14), new Color(0.85f, 0.82f, 0.74f), true, new Vector2(0, 0.5f));
            _finderFill = Fill(fb.transform);
            _finderFill.color = new Color(0.95f, 0.75f, 0.2f);
            _finderText = Label("Text", vf, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(560, 30), 22, Paper, TextAnchor.UpperCenter);
            var ol2 = _finderText.gameObject.AddComponent<Outline>();
            ol2.effectColor = Ink;
            ol2.effectDistance = new Vector2(2f, -2f);

            var fl = new GameObject("Flash", typeof(RectTransform)).GetComponent<RectTransform>();
            fl.SetParent(_root, false);
            fl.anchorMin = Vector2.zero; fl.anchorMax = Vector2.one;
            fl.offsetMin = fl.offsetMax = Vector2.zero;
            _flash = fl.gameObject.AddComponent<Image>();
            _flash.raycastTarget = false;
            _flash.color = new Color(1, 1, 1, 0);

            _meterPanel.SetActive(false);
            _hint.gameObject.SetActive(false);
            _finder.SetActive(false);
        }

        void LateUpdate()
        {
            int f = Time.frameCount;
            bool world = GameManager.I != null && GameManager.I.InWorld && !Dialogue.Showing;
            _meterPanel.SetActive(world && f - _meterFrame <= 1);
            _hint.gameObject.SetActive(world && f - _hintFrame <= 1);
            _finder.SetActive(world && f - _finderFrame <= 1);
            _flashT = Mathf.Max(0f, _flashT - Time.unscaledDeltaTime);
            _flash.color = new Color(1, 1, 1, _flashT / 0.35f * 0.85f);
            _flash.enabled = _flashT > 0f;
        }

        void Corner(RectTransform parent, Vector2 corner, bool horizontal)
        {
            var rt = new GameObject("Bracket", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = horizontal ? new Vector2(70, 8) : new Vector2(8, 70);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Paper;
            img.raycastTarget = false;
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = Ink;
            o.effectDistance = new Vector2(2, -2);
        }

        Image Panel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, bool border = true, Vector2? pivot = null)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (border)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Ink;
                o.effectDistance = new Vector2(3, -3);
            }
            return img;
        }

        Image Fill(Transform bar)
        {
            var f = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
            f.SetParent(bar, false);
            f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one;
            f.offsetMin = f.offsetMax = Vector2.zero;
            var img = f.gameObject.AddComponent<Image>();
            var tex = Texture2D.whiteTexture;
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.raycastTarget = false;
            return img;
        }

        Text Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
