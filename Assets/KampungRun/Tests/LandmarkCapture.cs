using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KampungRun.Tests
{
    /// <summary>
    /// Every landmark from the street and from the air, for comparing with the real-world photos in
    /// Tools/references/landmarks. Writes Tools/promo_frames/landmarks/&lt;set&gt;/&lt;landmark&gt;_{street,air,air2}.jpg
    /// (set = the LM_SET environment variable, "cur" by default) and log.txt with what each view found
    /// (model size, collider count). Explicit: only by name.
    /// </summary>
    [Explicit, Category("Promo")]
    public class LandmarkCapture
    {
        const int W = 1280, H = 720;
        const float G = 0.18f;               // CityBuilder.G: the pavement top
        static string Set => string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("LM_SET")) ? "cur" : System.Environment.GetEnvironmentVariable("LM_SET");
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "landmarks", Set));

        /// <summary>(file label, anchor / object name, place key, fallback model names, size if nothing is found)</summary>
        static readonly (string label, string anchor, string place, string[] models, float size)[] Targets =
        {
            ("petronas", "Petronas", "Towers", new[] { "Bld_MenaraKembar" }, 300f),
            ("menara_kl", "KLTower", "KLTower", new[] { "Bld_MenaraKL" }, 280f),
            ("sultan_abdul_samad", "SultanAbdulSamad", "SultanAbdulSamad", new[] { "Bld_SultanAbdulSamad" }, 90f),
            ("dataran_merdeka", "Dataran", "Dataran", new[] { "Landmark_Dataran_env_lm3_dataran_flagpole" }, 90f),
            ("masjid_jamek", "MasjidJamek", "Masjid", new[] { "env_lm2_masjid_jamek" }, 50f),
            ("merdeka118", "Merdeka118", "Merdeka118", new[] { "env_lm2_merdeka118" }, 440f),
            ("stadium_merdeka", "StadiumMerdeka", "StadiumMerdeka", new[] { "env_lm2_stadium_merdeka" }, 140f),
            ("stadium_negara", "StadiumNegara", "", new string[0], 80f),
            ("masjid_negara", "MasjidNegara", "MasjidNegara", new[] { "env_lm_masjid_negara" }, 96f),
            ("kl_railway_station", "KLRailway", "KLRailway", new string[0], 90f),
            ("pasar_seni", "PasarSeni", "PasarSeni", new[] { "Bld_PasarSeni" }, 60f),
            ("petaling_street", "Petaling", "Pasar", new[] { "Prop_ChinatownGate" }, 40f),
            ("maybank_tower", "Maybank", "", new string[0], 160f),
            ("dayabumi", "Dayabumi", "", new string[0], 120f),
            ("tugu_negara", "TuguNegara", "TuguNegara", new[] { "env_lm2_tugu_negara" }, 44f),
            ("muzium_negara", "MuziumNegara", "MuziumNegara", new[] { "env_lm_muzium_negara" }, 70f),
            ("kl_sentral", "KLSentral", "KLSentral", new[] { "env_lm_kl_sentral" }, 118f),
            ("pavilion", "Pavilion", "Pavilion", new[] { "env_lm2_pavilion" }, 100f),
            ("thean_hou", "TheanHou", "TheanHou", new[] { "env_lm2_thean_hou" }, 80f),
            ("istana_negara", "IstanaNegara", "IstanaNegara", new[] { "env_lm2_istana_negara" }, 80f),
            ("batu_caves", "BatuCaves", "BatuCaves", new[] { "env_lm2_batu_caves" }, 80f),
        };

        static int Solid => ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << 2);

        RenderTexture _rt;
        Texture2D _tex;
        Camera _cam;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void Grab(string name)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else { _cam.targetTexture = _rt; _cam.Render(); _cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Root, name + ".jpg"), _tex.EncodeToJPG(90));
        }

        IEnumerator View(string name, Vector3 eye, Vector3 look, float fov)
        {
            _cam.fieldOfView = fov;
            _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            foreach (var q in new List<Pedestrian>(Pedestrian.All))
                if (q && (q.transform.position - eye).sqrMagnitude < 8f * 8f) Object.Destroy(q.gameObject);
            Arch.ArchCity.I?.Prime(eye);
            Arch.ArchCity.I?.Prime(look);
            yield return Frames(6);
            Grab(name);
        }

        static bool Find(string[] names, Vector3 near, float within, out GameObject go)
        {
            go = null;
            float best = within * within;
            foreach (var t in Object.FindObjectsByType<Transform>())
            {
                if (System.Array.IndexOf(names, t.name) < 0) continue;
                float d = (t.position - near).sqrMagnitude;
                if (d < best) { best = d; go = t.gameObject; }
            }
            return go != null;
        }

        static Bounds RenderBounds(GameObject go)
        {
            bool first = true;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator Tour()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 2;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            var p = GameManager.I.Player;
            ChaseCamera.I.enabled = false;
            _cam = Camera.main;
            _cam.farClipPlane = Mathf.Max(_cam.farClipPlane, 2500f);
            var city = GameManager.I.City;

            // where the patches put each landmark (and the road beside it)
            var anchors = new Dictionary<string, (Vector3 pos, Vector3 road)>();
            foreach (var lp in CityBuilder.Layout.patches)
            {
                var kp = KLPatch.Load(lp.name);
                if (kp == null) continue;
                foreach (var a in kp.anchors)
                    if (!anchors.ContainsKey(a.name))
                        anchors[a.name] = (new Vector3(a.pos.x, G, a.pos.y), new Vector3(a.place.x, G, a.place.y));
            }

            foreach (var t in Targets)
            {
                Vector3 centre, road;
                if (anchors.TryGetValue(t.anchor, out var an)) { centre = an.pos; road = an.road; }
                else if (!string.IsNullOrEmpty(t.place) && city.places.TryGetValue(t.place, out var pl))
                {
                    // the outlying landmarks fill their block; their place is on the road north of it
                    road = pl;
                    centre = pl + new Vector3(0, 0, -24.5f * CityBuilder.LandmarkScale);
                }
                else { _log.AppendLine($"{t.label}: no anchor or place"); continue; }

                // the model (or a guess at its size)
                Bounds b;
                int cols = 0;
                if (Find(new[] { "Landmark_" + t.anchor }, centre, 200f, out var go) || (t.models.Length > 0 && Find(t.models, centre, 200f, out go)))
                {
                    b = RenderBounds(go);
                    cols = go.GetComponentsInChildren<Collider>().Length;
                    if (b.size.y < 1f) b = new Bounds(centre + Vector3.up * t.size * 0.3f, new Vector3(t.size, t.size * 0.6f, t.size));
                }
                else b = new Bounds(centre + Vector3.up * t.size * 0.3f, new Vector3(t.size, t.size * 0.6f, t.size));
                _log.AppendLine($"{t.label}: {(go ? go.name : "(no model)")} centre {b.center} size {b.size} colliders {cols} road {road}");

                var flat = road - b.center; flat.y = 0f;
                var dir = flat.sqrMagnitude > 1f ? flat.normalized : Vector3.back;
                float hor = Mathf.Max(b.size.x, b.size.z), tall = b.size.y;
                float radius = b.extents.magnitude;
                p.Teleport(road + Vector3.up * 0.3f, Quaternion.LookRotation(-dir));
                yield return Frames(8);

                // street: a spot on the ground with the clearest view of the whole thing (60 deg lens), as near
                // the road side as it can be; then near: from the road beside it, looking up at it
                float fov = 60f;
                float half = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                float dWide = hor * 0.62f / (half * 16f / 9f), dTall = (tall * 0.55f) / half;
                float dist = Mathf.Max(Mathf.Max(dWide, dTall), hor * 0.5f + 12f);
                var look = new Vector3(b.center.x, G + tall * 0.42f, b.center.z);
                var across = Vector3.Cross(Vector3.up, dir);
                var pts = new[] { new Vector3(b.center.x, G + tall * 0.15f, b.center.z), look, new Vector3(b.center.x, G + tall * 0.75f, b.center.z),
                                  look + across * hor * 0.3f, look - across * hor * 0.3f };
                Vector3 bestEye = b.center + dir * dist; float bestScore = -1e9f;
                for (int az = 0; az < 360; az += 15)
                    foreach (float k in new[] { 1f, 0.8f, 1.3f, 1.7f })
                    {
                        var d = Quaternion.Euler(0, az, 0) * dir;
                        var e = b.center + d * dist * k; e.y = G + 1.7f;
                        if (Physics.CheckSphere(e, 0.6f, Solid, QueryTriggerInteraction.Ignore)) continue;
                        float score = 0f;
                        foreach (var q in pts)
                        {
                            if (!Physics.Linecast(e, q, out var hit, Solid, QueryTriggerInteraction.Ignore)) score += 1f;
                            else if ((go && hit.collider.transform.IsChildOf(go.transform)) || (hit.point - q).magnitude < hor * 0.45f) score += 1f;
                        }
                        score -= Mathf.Min(az, 360 - az) * 0.004f + Mathf.Abs(k - 1f) * 0.3f;
                        if (score > bestScore) { bestScore = score; bestEye = e; }
                    }
                yield return View(t.label + "_street", bestEye, look, fov);
                var nearEye = road + Vector3.up * 1.7f - dir * 2f;
                var nearLook = new Vector3(b.center.x, G + Mathf.Min(tall * 0.5f, 40f), b.center.z);
                yield return View(t.label + "_near", nearEye, nearLook, 70f);
                // air: three-quarter from above, the side facing the road, and the far side
                var side = Quaternion.Euler(0, 40f, 0) * dir;
                float ad = radius / Mathf.Tan(25f * Mathf.Deg2Rad);
                var eye = b.center + side * ad * 0.8f + Vector3.up * ad * 0.55f;
                yield return View(t.label + "_air", eye, b.center, 50f);
                var other = Quaternion.Euler(0, 160f, 0) * dir;
                eye = b.center + other * ad * 0.8f + Vector3.up * ad * 0.45f;
                yield return View(t.label + "_air2", eye, b.center, 50f);
            }
            File.WriteAllText(Path.Combine(Root, "log.txt"), _log.ToString());
            Debug.Log("[LandmarkCapture] " + Root + "\n" + _log);
        }
    }
}
