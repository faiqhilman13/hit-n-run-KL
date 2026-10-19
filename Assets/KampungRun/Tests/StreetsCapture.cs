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
    /// The streets district by district, the way a player sees them: for each spot an eye-level view down the
    /// street (1.65 m, on the pavement), a chase-height view and an oblique view from about 28 m up. Written to
    /// Tools/promo_frames/streets/&lt;tag&gt;/ for the reference-vs-game sheets (Tools/klmap/street_sheets.py),
    /// with the triangles and renderers in view per shot in the log ([Streets] lines). Explicit: by name only.
    /// </summary>
    [Explicit, Category("Promo"), Timeout(1800000)]
    public class StreetsCapture
    {
        const int W = 1280, H = 720;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "streets"));
        static int Solid => ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << 2 | 1 << 12);

        RenderTexture _rt;
        Texture2D _tex;
        Camera _cam;
        string _dir;
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
            File.WriteAllBytes(Path.Combine(_dir, name + ".jpg"), _tex.EncodeToJPG(90));
        }

        static Vector3 Ground(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out var hit, 60f, Solid, QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
            return at;
        }

        /// <summary>The way a street runs past a spot: the direction with the longest clear view both ways at eye height.</summary>
        static Vector3 Axis(Vector3 at)
        {
            float Clear(Vector3 dir) => Physics.Raycast(at + Vector3.up * 1.7f, dir, out var h, 150f, Solid, QueryTriggerInteraction.Ignore) ? h.distance : 150f;
            var best = Vector3.forward;
            float bestLen = 0f;
            for (int a = 0; a < 180; a += 5)
            {
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float f = Clear(d), b = Clear(-d);
                if (f + b > bestLen) { bestLen = f + b; best = f >= b ? d : -d; }
            }
            return best;
        }

        static Vector3 Free(Vector3 eye)
        {
            for (float d = 0f; d < 6f; d += 0.5f)
                foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    var p = eye + dir * d;
                    if (!Physics.CheckSphere(p, 0.5f, Solid, QueryTriggerInteraction.Ignore)) return p;
                }
            return eye;
        }

        /// <summary>Triangles and renderers inside the view (a rough count: whole meshes whose bounds touch the frustum).</summary>
        (long tris, int rends) InView()
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(_cam);
            long tris = 0; int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if ((_cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                float dist = Vector3.Distance(_cam.transform.position, r.bounds.ClosestPoint(_cam.transform.position));
                // the layer cull distances (Detail drops out) are honoured roughly
                var lcd = _cam.layerCullDistances;
                if (lcd != null && lcd.Length > r.gameObject.layer && lcd[r.gameObject.layer] > 0f && dist > lcd[r.gameObject.layer]) continue;
                if (dist > _cam.farClipPlane) continue;
                n++;
                var m = mf.sharedMesh;
                for (int s = 0; s < m.subMeshCount; s++) tris += (long)m.GetIndexCount(s) / 3;
            }
            return (tris, n);
        }

        IEnumerator View(string name, Vector3 eye, Vector3 look, float fov)
        {
            eye = Free(eye);
            _cam.fieldOfView = fov;
            _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            foreach (var q in new List<Pedestrian>(Pedestrian.All))
                if (q && (q.transform.position - eye).sqrMagnitude < 3f * 3f) Object.Destroy(q.gameObject);
            Arch.ArchCity.I?.Prime(eye);
            yield return Frames(6);
            Grab(name);
            var (t, r) = InView();
            string line = $"[Streets] {name}: {t / 1000}k tris, {r} renderers in view, eye {eye}";
            Debug.Log(line);
            _log.AppendLine(line);
        }

        static bool Clear(Vector3 a, Vector3 b, float slack = 2f)
        {
            var d = b - a;
            return !Physics.Raycast(a, d.normalized, Mathf.Max(0f, d.magnitude - slack), Solid, QueryTriggerInteraction.Ignore);
        }

        /// <summary>The first of the candidate eyes that sees `look` (and isn't inside anything), else the last.</summary>
        static Vector3 Pick(Vector3 look, params Vector3[] eyes)
        {
            foreach (var e in eyes)
                if (!Physics.CheckSphere(e, 0.4f, Solid, QueryTriggerInteraction.Ignore) && Clear(e, look, 6f)) return e;
            return eyes[eyes.Length - 1];
        }

        /// <summary>Three views of a street: eye level on the pavement, chase height and a high oblique.</summary>
        IEnumerator Spot(string tag, Vector3 at, Vector3? axisHint = null, float side = 7f)
        {
            at = Ground(at);
            var axis = axisHint ?? Axis(at);
            axis.y = 0f; axis.Normalize();
            var s = Vector3.Cross(Vector3.up, axis);
            var p = GameManager.I.Player;
            p.Teleport(at + Vector3.up * 0.3f + s * 2f, Quaternion.LookRotation(axis));
            Arch.ArchCity.I?.Prime(at);
            yield return Frames(20);
            // eye level: on the pavement to one side (whichever side has a pavement clear of walls), looking down the street
            var eyeLook = at + axis * 40f + Vector3.up * 3f;
            var eye = Pick(eyeLook, Ground(at + s * side) + Vector3.up * 1.65f, Ground(at - s * side) + Vector3.up * 1.65f,
                           Ground(at + s * side * 0.6f) + Vector3.up * 1.65f, Ground(at - s * side * 0.6f) + Vector3.up * 1.65f, at + Vector3.up * 1.65f);
            yield return View(tag + "_eye", eye, eyeLook + Vector3.ProjectOnPlane(eye - at, Vector3.up) * 0.4f, 64f);
            var chaseLook = at + axis * 26f + Vector3.up * 1.5f;
            yield return View(tag + "_chase", Pick(chaseLook, at - axis * 8f + Vector3.up * 3.4f, at - axis * 4f + Vector3.up * 3.4f, at + Vector3.up * 3.4f), chaseLook, 60f);
            var airLook = at + axis * 18f;
            yield return View(tag + "_air", Pick(airLook, at - axis * 40f + s * 26f + Vector3.up * 28f, at - axis * 40f - s * 26f + Vector3.up * 28f,
                                                 at - axis * 45f + Vector3.up * 34f, at - axis * 30f + Vector3.up * 60f), airLook, 58f);
        }

        [UnityTest] public IEnumerator Before() => Shoot("before");
        [UnityTest] public IEnumerator After() => Shoot("after");
        [UnityTest] public IEnumerator Round() => Shoot("round");

        IEnumerator Shoot(string tag)
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(40);
            HUD.I.DebugSkipDialogue();
            GameInput.Locked = false;
            foreach (var c in Object.FindObjectsByType<Canvas>()) c.enabled = false;
            _rt = new RenderTexture(W, H, 24);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            _dir = Path.Combine(Root, tag);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            ChaseCamera.I.enabled = false;
            _cam = Camera.main;
            var city = GameManager.I.City;

            // the real-KL patches round their named places
            foreach (var (key, name) in new[] { ("Pasar", "01_petaling"), ("Masjid", "02_tunperak"), ("Dataran", "03_dataran"),
                                                ("BukitBintang", "04_bukitbintang"), ("Brickfields", "05_brickfields"), ("ChowKit", "06_chowkit"),
                                                ("Towers", "07_klcc"), ("KLSentral", "08_klsentral"), ("PasarSeni", "09_pasarseni"),
                                                ("Home", "10_kampung") })
            {
                if (!city.places.TryGetValue(key, out var at)) { Debug.Log($"[Streets] no place {key}"); continue; }
                if (key == "Home") { yield return Spot(name, at); continue; }
                // the street nearest the place: the middle of the longest link (at least 25 m) among the nodes round it
                Vector3 best = at, bestDir = Vector3.forward;
                float bestScore = float.MaxValue;
                foreach (var n in city.roads.nodes)
                {
                    if (n.pos.y > 1f || (n.pos - at).sqrMagnitude > 90f * 90f) continue;
                    foreach (var l in n.links)
                    {
                        float len = Vector3.Distance(n.pos, l.pos);
                        if (len < 25f || l.pos.y > 1f) continue;
                        var mid = (n.pos + l.pos) * 0.5f;
                        float score = Vector3.Distance(mid, at) - len * 0.5f;
                        if (score < bestScore) { bestScore = score; best = mid; bestDir = (l.pos - n.pos).normalized; }
                    }
                }
                yield return Spot(name, best, bestDir, 7.5f);
            }
            // the filler districts: mid-block on a street, looking along it
            Vector3 MidNS(int i, int k) => new Vector3(CityBuilder.RoadX(i), 0f, CityBuilder.RoadZ(k) + CityBuilder.Pitch * 0.5f);
            Vector3 MidEW(int i, int k) => new Vector3(CityBuilder.RoadX(i) + CityBuilder.Pitch * 0.5f, 0f, CityBuilder.RoadZ(k));
            yield return Spot("11_jalantar_shops", MidNS(3, 19), Vector3.forward, 8.5f);
            yield return Spot("12_titiwangsa_condo", MidEW(1, 18), Vector3.right, 8.5f);
            yield return Spot("13_ampang_office", MidNS(16, 14), Vector3.forward, 8.5f);
            yield return Spot("14_pudu_shops", MidEW(12, 2), Vector3.right, 8.5f);
            yield return Spot("15_imbi_mix", MidNS(15, 6), Vector3.forward, 8.5f);
            yield return Spot("16_hangtuah", MidEW(10, 1), Vector3.right, 8.5f);
            // a junction, close
            var j = Ground(new Vector3(CityBuilder.RoadX(12), 0f, CityBuilder.RoadZ(3)));
            var jl = j + new Vector3(4f, 1f, 6f);
            yield return View("17_junction", Pick(jl, j + new Vector3(-24f, 3.2f, -22f), j + new Vector3(-14f, 3.2f, -14f), j + new Vector3(-6f, 3.2f, -18f)), jl, 62f);
            // set pieces: the mamak's tables, a petrol station
            foreach (var (key, name) in new[] { ("Mamak", "20_mamak"), ("Petrol", "21_petrol") })
                if (city.places.TryGetValue(key, out var sp))
                {
                    var look = sp + Vector3.up * 1.5f;
                    yield return View(name, Pick(look, sp + new Vector3(-14f, 4f, 20f), sp + new Vector3(14f, 4f, -20f), sp + new Vector3(0f, 4f, -22f),
                                                 sp + new Vector3(0f, 4f, 22f), sp + new Vector3(-20f, 5f, 0f), sp + new Vector3(0f, 25f, -30f)), look, 60f);
                }
            // the skyline from high up
            yield return View("18_city_high", new Vector3(-330f, 95f, -470f), new Vector3(-60f, 0f, -120f), 60f);
            if (city.places.TryGetValue("Towers", out var klcc))
                yield return View("19_klcc_skyline", klcc + new Vector3(-260f, 60f, -260f), klcc + new Vector3(0f, 60f, 0f), 60f);
            File.WriteAllText(Path.Combine(_dir, "log.txt"), _log.ToString());
            ChaseCamera.I.enabled = true;
        }
    }
}
