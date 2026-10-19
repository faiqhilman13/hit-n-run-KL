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
    /// The missions pass (Oct 2026): boots every chapter and plays each story mission through, driving the new
    /// objective types (clean driving, fragile cargo, photos, haggling, stealth, room searches, hiding, ramming,
    /// escort, defence, brawls, a foot chase, the boss) through their real mechanics where a test can - walking into
    /// rooms, punching guards from behind, framing birds in the camera, dodging the boss's dive - and saving a frame
    /// of each to Tools/promo_frames/x_missions. Log lines start "[Mx]".
    /// </summary>
    public class MissionTests
    {
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "promo_frames", "x_missions"));
        static readonly List<string> Report = new List<string>();

        static void Shot(string name)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            // the mission title card would hide what the frame is for
            if (HUD.I != null) { var big = HUD.I.transform.Find("Canvas/Big"); if (big) big.gameObject.SetActive(false); }
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var olds = new List<(Canvas, RenderMode, float)>();
            foreach (var c in canvases)
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                olds.Add((c, c.renderMode, c.planeDistance));
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = c.sortingOrder >= 10 ? 0.5f : 0.6f;
            }
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            foreach (var (c, m, d) in olds) { c.renderMode = m; c.planeDistance = d; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }
        static void Skip() { HUD.I.DebugSkipDialogue(); GameInput.Locked = HUD.I.MenuOpen; }

        static void MoveCar(Vehicle v, Vector3 to)
        {
            v.Body.position = to + Vector3.up * 0.8f;
            v.transform.position = to + Vector3.up * 0.8f;
            v.Body.linearVelocity = Vector3.zero;
        }

        static Quaternion Facing(Vector3 from, Vector3 to)
        {
            var d = to - from; d.y = 0;
            return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        static void Stand(PlayerController p, Vector3 at, Vector3 look)
        {
            p.Teleport(Goon.Ground(at) + Vector3.up * 0.1f, Facing(at, look));
        }

        static void Log(string s) { Debug.Log("[Mx] " + s); Report.Add(s); }

        static string _tag;

        // ------------------------------------------------------------------ per-type solvers

        static IEnumerator Solve(GameManager gm, Mission mission, Objective o)
        {
            var p = gm.Player;
            var mm = gm.Missions;
            string tag = _tag + "_" + o.GetType().Name.Replace("Objective", "");
            switch (o)
            {
                case TalkObjective _:
                {
                    var t = o.Target;
                    Assert.IsTrue(t.HasValue, "talk target missing");
                    NPC best = null;
                    foreach (var n in mm.npcs.Values)
                        if (n && (best == null || Vector3.Distance(n.transform.position, t.Value) < Vector3.Distance(best.transform.position, t.Value))) best = n;
                    best.Interact(p);
                    yield return null;
                    Skip();
                    break;
                }
                case GetInCarObjective _:
                    if (gm.LastCar == null || gm.LastCar.Wrecked) gm.SummonCar(GameData.Levels[GameState.Level].familyCar, p.transform.position + Vector3.right * 4, Quaternion.identity);
                    if (!p.Driving) p.EnterVehicle(gm.LastCar, true);
                    break;
                case GoToObjective _:
                    if (!p.Driving && gm.LastCar != null && !gm.LastCar.Wrecked && Vector3.Distance(gm.LastCar.transform.position, p.transform.position) < 30f) p.EnterVehicle(gm.LastCar, true);
                    if (p.Driving) MoveCar(p.vehicle, o.Target.Value);
                    else p.Teleport(o.Target.Value, Quaternion.identity);
                    break;
                case CleanDriveObjective cd:
                {
                    if (!p.Driving) { if (gm.LastCar == null) gm.SummonCar("saga", p.transform.position + Vector3.right * 4, Quaternion.identity); p.EnterVehicle(gm.LastCar, true); }
                    yield return Seconds(1.8f);                 // past the grace period
                    cd.Strike("ujian");
                    Assert.AreEqual(1, cd.Strikes, "a saman should count");
                    yield return Seconds(0.5f);
                    Shot(tag);
                    MoveCar(p.vehicle, o.Target.Value);
                    break;
                }
                case FragileObjective fr:
                {
                    if (!p.Driving) { if (gm.LastCar == null) gm.SummonCar(GameData.Levels[GameState.Level].familyCar, p.transform.position + Vector3.right * 4, Quaternion.identity); p.EnterVehicle(gm.LastCar, true); }
                    yield return Seconds(0.5f);
                    // a real crash: drive it into... just spill a bit, then check hard braking does too
                    fr.Spill(20f);
                    Assert.Less(fr.Integrity, 81f, "spill should cost cargo");
                    var v = p.vehicle;
                    v.Body.linearVelocity = v.transform.forward * 18f;
                    yield return new WaitForFixedUpdate();
                    yield return Seconds(0.15f);
                    float before = fr.Integrity;
                    v.Body.linearVelocity = Vector3.zero;                 // an emergency stop
                    yield return Seconds(0.3f);
                    Log($"{_tag} fragile: braking took {before - fr.Integrity:F1}% (left {fr.Integrity:F0}%)");
                    Shot(tag);
                    MoveCar(p.vehicle, o.Target.Value);
                    break;
                }
                case PhotoObjective ph:
                {
                    yield return Frames(3);
                    int guard = 0, real = 0, fallback = 0;
                    while (ph.Got < ph.subjects.Count && guard++ < 10 && o.failReason == null)
                    {
                        PhotoObjective.Subject s = null;
                        foreach (var x in ph.subjects) if (!x.done && x.t) { s = x; break; }
                        if (s == null) break;
                        if (s.bird != null) while (s.bird.Flying) yield return null;
                        var target = s.t.position;
                        // find a standpoint 15-20 m off with a clear view, behind the bodyguards if there are any
                        Vector3 stand = target;
                        for (int k = 0; k < 24; k++)
                        {
                            var dir = Quaternion.Euler(0, k * 15f + 7f, 0) * Vector3.forward;
                            var c = Goon.Ground(target + dir * 17f);
                            if (Physics.Linecast(c + Vector3.up * 1.6f, target, Goon.SightMask, QueryTriggerInteraction.Ignore)) continue;
                            bool seen = false;
                            foreach (var g in ph.guards) if (g && g.CanSee(c + Vector3.up)) seen = true;
                            if (seen) continue;
                            stand = c;
                            break;
                        }
                        Stand(p, stand, target);
                        float t0 = Time.time;
                        int got0 = ph.Got;
                        bool shot = false;
                        while (Time.time - t0 < 3f && ph.Got == got0 && o.failReason == null)
                        {
                            if (!shot && Time.time - t0 > 0.55f) { Shot(tag + "_" + ph.Got); shot = true; }
                            yield return null;
                        }
                        if (ph.Got > got0) real++;
                        else if (o.failReason == null) { fallback++; ph.Snap(s); Log($"{_tag} photo: framing {s.name} from {Vector3.Distance(stand, target):F0} m didn't snap; forced"); }
                    }
                    Log($"{_tag} photo: {real} real snaps, {fallback} forced, suspicion {ph.Suspicion:F2}");
                    break;
                }
                case HaggleObjective hg:
                {
                    yield return Frames(2);
                    hg.OpenHaggle();
                    yield return Frames(2);
                    Skip();
                    yield return Frames(2);
                    Shot(tag);
                    foreach (var pick in new[] { 0, 1, 1, 1, 3 })
                    {
                        hg.Choose(pick);
                        Log($"{_tag} haggle: pick {pick} -> RM{hg.Price}, patience {hg.Patience}");
                        yield return Frames(2);
                        Skip();
                        yield return Frames(2);
                        if (o.failReason != null) break;
                    }
                    Skip();
                    break;
                }
                case SneakObjective sn:
                {
                    yield return Frames(5);
                    var g0 = sn.guards.Count > 0 ? sn.guards[0] : null;
                    if (g0 != null)
                    {
                        // a stage shot: behind the first guard, cones in view
                        Stand(p, g0.transform.position - g0.transform.forward * 9f, g0.transform.position);
                        yield return Seconds(0.6f);
                        Shot(tag);
                        // stand right in front of a guard for a moment: suspicion has to rise
                        Stand(p, g0.transform.position + g0.transform.forward * 3f, g0.transform.position);
                        yield return Seconds(0.25f);
                        float sus = sn.Suspicion;
                        Log($"{_tag} sneak: in front of a guard for 0.25 s -> suspicion {sus:F2}{(sn.disguise != null ? " (disguised)" : "")}");
                        Assert.Greater(sus, 0.01f, "a guard should notice you in front of him");
                        // back off out of sight and let it cool
                        Stand(p, g0.transform.position - g0.transform.forward * 30f, g0.transform.position);
                        float t0 = Time.time;
                        while (sn.Suspicion > 0.05f && Time.time - t0 < 6f) yield return null;
                    }
                    if (sn.takedowns)
                    {
                        // knock every guard out from behind with real punches
                        foreach (var g in new List<Goon>(sn.guards))
                        {
                            if (g == null || gm.Missions.Step != o) break;
                            // (a guard standing over a crate is knocked out too: you'd never get to it otherwise)
                            for (int k = 0; k < 6 && g && !g.Down; k++)
                            {
                                Stand(p, g.transform.position - g.transform.forward * 1.1f, g.transform.position);
                                yield return null;
                                Log($"{_tag} sneak: before punch {k}: alert {g.alert} sus {sn.Suspicion:F2} fail {o.failReason}");
                                p.DebugPunch(1);
                                yield return Seconds(0.2f);
                                if (g) Log($"{_tag} sneak: punch {k}: hp {g.hp} down {g.Down} alert {g.alert} d {Vector3.Distance(g.transform.position, p.transform.position):F2} sus {sn.Suspicion:F2}");
                            }
                            Log($"{_tag} sneak: guard takedown {(g && g.Down ? "ok" : "FAILED")}");
                            Assert.IsTrue(g == null || g.Down, "a punch from behind should knock an unaware guard out");
                        }
                    }
                    var safe = p.transform.position;
                    for (int i = 0; i < sn.Goals.Count && o.failReason == null; i++)
                    {
                        if (sn.Taken(i)) continue;
                        // wait for a moment nobody's looking at the spot (the way you'd play it)
                        float tw = Time.time;
                        while (Time.time - tw < 10f)
                        {
                            bool watched = false;
                            foreach (var g in sn.guards) if (g && !g.Down && g.CanSee(sn.Goals[i] + Vector3.up, 1.3f)) watched = true;
                            if (!watched) break;
                            yield return null;
                        }
                        Stand(p, sn.Goals[i] + Vector3.right * 0.3f, sn.Goals[i] + Vector3.forward);
                        float t0 = Time.time;
                        while (!sn.Taken(i) && Time.time - t0 < 4f && o.failReason == null) yield return null;
                        if (!sn.Taken(i)) Log($"{_tag} sneak: goal {i} not taken; player {p.transform.position} goal {sn.Goals[i]}");
                        // and slip away from it again
                        if (sn.Taken(i)) Stand(p, safe, sn.Goals[i]);
                    }
                    Log($"{_tag} sneak: goals {sn.Got}/{sn.Goals.Count}, suspicion {sn.Suspicion:F2}, fail={o.failReason}");
                    break;
                }
                case SearchObjective se:
                {
                    yield return Frames(3);
                    Log($"{_tag} search: {se.Candidates} candidates, prize #{se.PrizeIndex}");
                    Assert.Greater(se.Candidates, 1, "search found no rooms");
                    Stand(p, se.DoorOf(0) + (se.DoorOf(0) - se.InsideOf(0)).normalized * 6f, se.DoorOf(0));
                    yield return Seconds(0.5f);
                    Shot(tag);
                    int real = 0;
                    for (int i = 0; i < se.Candidates && !se.Found && o.failReason == null; i++)
                    {
                        Stand(p, se.InsideOf(i), se.DoorOf(i));
                        float t0 = Time.time;
                        while (Time.time - t0 < 3.2f && !se.Found) { yield return null; if (Dialogue.Showing) Skip(); }
                        real++;
                        if (i == 1) Shot(tag + "_inside");
                    }
                    if (!se.Found) { Log($"{_tag} search: walking in didn't find it; forced"); se.DebugSearch(-1); }
                    Log($"{_tag} search: found after {real} rooms");
                    yield return Seconds(1.2f);
                    Skip();
                    break;
                }
                case HideObjective hd:
                {
                    yield return Seconds(0.5f);
                    var room = Interiors.I.Nearest(p.transform.position, r => { foreach (var op in r.openings) if (op.door && op.wall == 0) return true; return false; });
                    Assert.IsNotNull(room, "no room to hide in");
                    Stand(p, room.f.P(room.W * 0.5f, 0.2f, -room.D * 0.6f), room.f.P(room.W * 0.5f, 0.2f, 0f));
                    yield return Seconds(1f);
                    Shot(tag);
                    float t0 = Time.time;
                    while (SamanMeter.I.Wanted && Time.time - t0 < 20f && o.failReason == null) yield return null;
                    Log($"{_tag} hide: lost the police after {Time.time - t0 + 1f:F1} s hidden={hd.Hidden}");
                    break;
                }
                case RamObjective ra:
                {
                    yield return Frames(3);
                    if (!p.Driving) { if (gm.LastCar == null) gm.SummonCar("saga", p.transform.position + Vector3.right * 4, Quaternion.identity); p.EnterVehicle(gm.LastCar, true); }
                    var t = ra.TargetCar;
                    MoveCar(p.vehicle, t.transform.position - t.transform.forward * 9f);
                    p.vehicle.transform.rotation = t.transform.rotation; p.vehicle.Body.rotation = t.transform.rotation;
                    yield return Seconds(1.2f);
                    Shot(tag);
                    for (int i = 0; i < 8 && ra.Hits < 5 && o.failReason == null; i++) { ra.RegisterHit(); yield return Seconds(1f); }
                    Log($"{_tag} ram: {ra.Hits} hits");
                    break;
                }
                case EscortObjective es:
                {
                    yield return Frames(3);
                    Time.timeScale = 3f;
                    var dest = es.Dest;
                    float t0 = Time.time;
                    bool shot = false;
                    while (gm.Missions.Step == o && o.failReason == null && Time.time - t0 < 200f)
                    {
                        if (es.Ward == null) break;
                        // walk ahead of the ward in 4 m steps toward the surau, waiting for her to keep up
                        var w = es.Ward.transform.position;
                        if (Vector3.Distance(w, p.transform.position) < 5f)
                        {
                            var to = dest - p.transform.position; to.y = 0;
                            var next = p.transform.position + to.normalized * Mathf.Min(4f, to.magnitude);
                            Stand(p, next, dest);
                        }
                        foreach (var g in es.promoters)
                            if (g && !g.Down && Vector3.Distance(g.transform.position, w) < 9f) g.Punched((g.transform.position - w).normalized, 12f, true);
                        if (!shot && es.promoters.Count > 0) { Time.timeScale = 1f; yield return Seconds(0.3f); Shot(tag); shot = true; Time.timeScale = 3f; }
                        yield return Seconds(0.25f);
                    }
                    Time.timeScale = 1f;
                    Log($"{_tag} escort: hearts {es.Hearts}, took {Time.time - t0:F0} s (x3), fail={o.failReason}");
                    break;
                }
                case DefendObjective df:
                {
                    var c = o.Target.Value;
                    Stand(p, c + Vector3.right * 2.5f, c);
                    Time.timeScale = 3f;
                    bool shot = false;
                    while (gm.Missions.Step == o && o.failReason == null)
                    {
                        foreach (var g in df.crowd)
                            if (g && !g.Down && g.tag == 0 && Vector3.Distance(g.transform.position, c) < 7f) g.Punched((g.transform.position - c).normalized, 12f, true);
                        if (!shot && df.Remaining < 45f) { Time.timeScale = 1f; yield return Seconds(0.2f); Shot(tag); shot = true; Time.timeScale = 3f; }
                        yield return null;
                    }
                    Time.timeScale = 1f;
                    Log($"{_tag} defend: cured {df.Cured}, pot {df.Health:F0}%, fail={o.failReason}");
                    break;
                }
                case BrawlObjective br:
                {
                    yield return Frames(3);
                    var c = br.crowd[0].transform.position;
                    Stand(p, c + Vector3.right * 4f, c);
                    yield return Seconds(1.5f);
                    Shot(tag);
                    foreach (var g in new List<Goon>(br.crowd))
                    {
                        for (int k = 0; k < 6 && g && g.tag != 1; k++)
                        {
                            Stand(p, g.transform.position - g.transform.forward * 1.0f, g.transform.position);
                            yield return null;
                            p.DebugKick();
                            yield return Seconds(0.35f);
                        }
                    }
                    yield return Seconds(0.5f);
                    Log($"{_tag} brawl: cured {br.Cured}/{br.crowd.Count}, knocked down {br.Knockouts}");
                    break;
                }
                case FootChaseObjective fc:
                {
                    yield return Frames(3);
                    var r = fc.Runner;
                    var start = r.transform.position;
                    Stand(p, start - Vector3.forward * 10f, start);
                    yield return Seconds(2.5f);
                    float ran = Vector3.Distance(start, r.transform.position);
                    Log($"{_tag} chase: he ran {ran:F1} m in 2.5 s");
                    Assert.Greater(ran, 4f, "the thief should run along the pavement");
                    Stand(p, r.transform.position - r.transform.forward * 4f, r.transform.position);
                    yield return Seconds(0.25f);
                    Shot(tag);
                    int punches = 0;
                    for (int k = 0; k < 20 && r && !r.Down; k++)
                    {
                        Stand(p, r.transform.position - r.transform.forward * 0.9f, r.transform.position);
                        yield return null;
                        p.DebugPunch(1);
                        punches++;
                        yield return Seconds(0.25f);
                    }
                    Log($"{_tag} chase: down after {punches} punches: {(r && r.Down)}");
                    Assert.IsTrue(r && r.Down, "punching the thief should floor him");
                    break;
                }
                case BossObjective bo:
                {
                    yield return Frames(3);
                    float t0 = Time.time;
                    bool sMark = false, sDaze = false;
                    var center = bo.Boss.center;
                    Stand(p, center + Vector3.right * 6f, center);
                    while (gm.Missions.Step == o && o.failReason == null && Time.time - t0 < 150f)
                    {
                        var b = bo.Boss;
                        if (b == null) { yield return null; continue; }
                        if (b.state == MegaMynah.State.Mark)
                        {
                            if (!sMark && Time.time - t0 > 2f) { Shot(tag + "_mark"); sMark = true; }
                            // run clear of the ring
                            var away = p.transform.position - center; away.y = 0;
                            var off = p.transform.position - b.Mark; off.y = 0;
                            if (off.magnitude < 6f)
                                Stand(p, b.Mark + (off.sqrMagnitude > 0.1f ? off.normalized : Vector3.right) * 9f, b.Mark);
                        }
                        else if (b.state == MegaMynah.State.Dazed)
                        {
                            var bp = Goon.Ground(b.transform.position);
                            var from = p.transform.position - bp; from.y = 0;
                            Stand(p, bp + (from.sqrMagnitude > 0.1f ? from.normalized : Vector3.right) * 2.2f, bp);
                            yield return null;
                            if (!sDaze) { Shot(tag + "_dazed"); sDaze = true; }
                            p.DebugPunch(1);
                            yield return Seconds(0.25f);
                            continue;
                        }
                        yield return null;
                    }
                    Log($"{_tag} boss: {(o.failReason ?? "beaten")} in {Time.time - t0:F0} s, KOs {(bo.Boss ? bo.Boss.playerKOs : -1)}");
                    break;
                }
                case CollectObjective _:
                    foreach (var pk in mm.GetComponentsInChildren<Pickup>()) if (pk.kind == Pickup.Kind.MissionItem) pk.Collect();
                    break;
                case DestroyObjective d:
                    yield return null;
                    Assert.IsNotNull(d.target, "destroy target missing");
                    d.target.Damage(99999f);
                    break;
                case SmashObjective _:
                    yield return Seconds(0.5f);
                    {
                        var first = mm.GetComponentInChildren<Breakable>();
                        if (first) { Stand(p, first.transform.position + Vector3.right * 4f, first.transform.position); yield return Seconds(0.5f); Shot(tag); }
                    }
                    foreach (var b in mm.GetComponentsInChildren<BurungKamera>()) b.Smash();
                    foreach (var b in mm.GetComponentsInChildren<Breakable>()) b.Hit(b.transform.position + Vector3.forward, 5f, true);
                    break;
                case EvadeObjective _:
                    yield return null;
                    SamanMeter.I.ClearAll();
                    break;
                case FollowObjective fo:
                    yield return Seconds(0.5f);
                    if (fo.Car && p.Driving) { MoveCar(p.vehicle, fo.Car.transform.position - fo.Car.transform.forward * 25f); yield return Seconds(1f); Shot(tag); }
                    o.debugForceDone = true;
                    break;
                default:
                    yield return new WaitForSeconds(0.5f);
                    o.debugForceDone = true;
                    break;
            }
            yield return null;
            if (!(o is HaggleObjective)) Skip();
        }

        static IEnumerator RunMission(GameManager gm, Mission m, int level, int index)
        {
            int fails = gm.Missions.Failures;
            gm.Missions.StartMission(m);
            Skip();
            int guard = 0, step = -1;
            while (gm.Missions.Active && gm.Missions.Current == m && guard++ < 40)
            {
                var o = gm.Missions.Step;
                if (o == null) { yield return null; continue; }
                Assert.IsNull(o.failReason, $"'{m.title}' failed: {o.failReason}");
                step = m.objectives.IndexOf(o);
                _tag = $"L{level}M{index + 1}s{step + 1}";
                yield return Solve(gm, m, o);
                Assert.IsNull(o.failReason, $"'{m.title}' step {step + 1} ({o.GetType().Name}) failed: {o.failReason}");
                Assert.AreEqual(fails, gm.Missions.Failures, $"'{m.title}' step {step + 1} ({o.GetType().Name}) failed: {gm.Missions.LastFailure}");
                yield return null;
                yield return null;
            }
            Skip();
            Assert.IsFalse(gm.Missions.Active && gm.Missions.Current == m, $"mission '{m.title}' got stuck at step {step + 1}");
        }

        /// <summary>Every chapter, every story mission, through its real mechanics; screenshots on the way.</summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator PlayEveryMission()
        {
            Report.Clear();
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(20);
            var gm = GameManager.I;
            int only = 0;
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a.StartsWith("-mxLevel=")) int.TryParse(a.Substring(9), out only);
            for (int level = 1; level <= GameData.LastLevel; level++)
            {
                if (only > 0 && level != only) { if (level < GameData.LastLevel) { GameState.Data.missionsDone[level] = 5; gm.LoadLevel(level + 1); yield return Frames(10); } continue; }
                Skip();
                if (GameState.Level != level) { gm.LoadLevel(level); yield return Frames(10); Skip(); }
                var story = new List<Mission>(gm.Missions.story);
                Assert.AreEqual(GameData.MissionsPerLevel, story.Count);
                var home = gm.City.places["HomeYard"];
                foreach (var k in new[] { "Mamak", "Surau", "Padang", "Pasar", "Masjid", "Dataran", "PasarSeni", "Towers", "KLTower", "ChowKit" })
                    Log($"L{level} distance HomeYard->{k}: {Vector3.Distance(home, gm.City.places[k]):F0} m");
                for (int i = 0; i < story.Count; i++)
                {
                    var kinds = new List<string>();
                    foreach (var ob in story[i].objectives) kinds.Add(ob.GetType().Name.Replace("Objective", ""));
                    Log($"L{level} M{i + 1} '{story[i].title}': {string.Join(" > ", kinds)}");
                    yield return RunMission(gm, story[i], level, i);
                    Assert.AreEqual(i + 1, GameState.Data.missionsDone[level], $"mission {story[i].title} not recorded");
                    yield return Frames(3);
                    Skip();
                    yield return Frames(3);
                }
                yield return Frames(10);
                Skip();
            }
            File.WriteAllLines(Path.Combine(ShotDir, "report.txt"), Report);
        }

        /// <summary>Failing a later step of a checkpointed mission retries from the checkpoint, not from the top.</summary>
        [UnityTest]
        public IEnumerator CheckpointRetry()
        {
            GameState.Ephemeral = true;
            GameState.Data = new SaveData();
            GameManager.ForceLevel = 1;
            SceneManager.LoadScene("KampungRun");
            yield return Frames(20);
            var gm = GameManager.I;
            Skip();
            var m = gm.Missions.story[0];          // Teh Tarik: get in, drive, talk, clean drive (checkpoint)
            int cp = m.objectives.FindIndex(o => o.checkpoint);
            Assert.Greater(cp, 0, "Teh Tarik should have a checkpoint");
            gm.Missions.StartMission(m);
            Skip();
            for (int guard = 0; guard < 20 && gm.Missions.Step != m.objectives[cp]; guard++)
            {
                yield return Solve(gm, m, gm.Missions.Step);
                yield return Frames(2);
            }
            Assert.AreSame(m.objectives[cp], gm.Missions.Step, "didn't reach the checkpoint");
            yield return Seconds(1.8f);
            var cd = (CleanDriveObjective)gm.Missions.Step;
            cd.Strike("a"); cd.Strike("b"); cd.Strike("c");
            Assert.IsNotNull(cd.failReason, "three saman should fail the drive");
            yield return Frames(3);
            Assert.IsFalse(gm.Missions.Active, "mission should have failed");
            Skip();                                   // "press E to retry"
            yield return Frames(3);
            Assert.IsTrue(gm.Missions.Active, "retry didn't start");
            Assert.AreSame(m.objectives[cp], gm.Missions.Step, "retry should resume at the checkpoint");
            Assert.AreEqual(0, ((CleanDriveObjective)gm.Missions.Step).Strikes, "strikes should reset on retry");
            gm.Missions.Abort();
        }
    }
}
