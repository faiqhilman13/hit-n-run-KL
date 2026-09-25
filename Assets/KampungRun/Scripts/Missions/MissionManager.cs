using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Runs the story missions of the current level in order, plus the level's street race.
    /// Mission givers wear a bobbing "!" when they have something for you.
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        public readonly Dictionary<string, NPC> npcs = new Dictionary<string, NPC>();
        public List<Mission> story = new List<Mission>();
        public Mission race;

        public Mission Current { get; private set; }
        public bool Active => Current != null;
        public Objective Step => Active && _step < Current.objectives.Count ? Current.objectives[_step] : null;
        public float TimeLeft { get; private set; }

        int _step;
        readonly List<GameObject> _beacons = new List<GameObject>();
        readonly List<GameObject> _raceBeacons = new List<GameObject>();
        GameObject _targetBeacon;
        Vector3 _startPos;
        Quaternion _startRot;

        public NPC Npc(string key) => npcs.TryGetValue(key, out var n) ? n : null;

        public void Setup(List<Mission> storyMissions, Mission raceMission)
        {
            story = storyMissions;
            race = raceMission;
            RefreshGivers();
        }

        public void RefreshGivers()
        {
            foreach (var n in npcs.Values) { n.hasMission = false; n.onTalk = null; }
            if (Active) return;
            int done = GameState.Data.missionsDone[GameState.Level];
            if (done < story.Count)
            {
                var m = story[done];
                var giver = Npc(m.giver);
                if (giver)
                {
                    giver.hasMission = true;
                    giver.onTalk = _ => Offer(m);
                }
            }
            if (race != null)
            {
                var rg = Npc(race.giver);
                if (rg && rg.onTalk == null)
                {
                    rg.hasMission = !GameState.Data.raceDone[GameState.Level];
                    rg.onTalk = _ => Offer(race);
                }
            }
        }

        void Offer(Mission m)
        {
            if (Active) return;
            Dialogue.Say(m.intro, () => StartMission(m));
        }

        public void StartMission(Mission m)
        {
            Current = m;
            _step = -1;
            _startPos = PlayerController.I.transform.position;
            _startRot = PlayerController.I.transform.rotation;
            foreach (var n in npcs.Values) n.hasMission = false;
            HUD.I.BigMessage(m.title, m.isRace ? "PERLUMBAAN" : "MISI BARU");
            ProcAudio.Play2D(ProcAudio.Fanfare, 0.5f, 0.9f);
            if (!string.IsNullOrEmpty(m.startCar) && !PlayerController.I.Driving)
            {
                var p = PlayerController.I.transform;
                GameManager.I.SummonCar(m.startCar, p.position + p.right * 4f + Vector3.up * 0.5f, p.rotation);
            }
            Next();
        }

        void Next()
        {
            if (_step >= 0 && _step < Current.objectives.Count) Current.objectives[_step].End(this);
            ClearBeacons();
            _step++;
            if (_step >= Current.objectives.Count) { Complete(); return; }
            var o = Current.objectives[_step];
            o.failReason = null;
            TimeLeft = o.timeLimit * CityBuilder.WorldScale;   // limits were set on the old compressed map
            o.Begin(this);
            if (!string.IsNullOrEmpty(o.text)) ProcAudio.Play2D(ProcAudio.Blip, 0.4f);
        }

        void Update()
        {
            if (!Active || GameManager.I == null || !GameManager.I.Playing) return;
            var o = Step;
            if (o == null) return;
            float dt = Time.deltaTime;
            if (!Dialogue.Showing && o.timeLimit > 0)
            {
                TimeLeft -= dt;
                if (TimeLeft <= 0) { Fail("Masa tamat!"); return; }
            }
            if (o.failReason != null) { Fail(o.failReason); return; }
            if (o.debugForceDone || o.Tick(this, dt)) { Next(); return; }

            // beacon on the current target
            var t = o.Target;
            if (t.HasValue)
            {
                if (_targetBeacon == null) _targetBeacon = MakeBeacon(t.Value, 1f);
                _targetBeacon.SetActive(true);
                _targetBeacon.transform.position = t.Value + Vector3.up * 30f;
            }
            else if (_targetBeacon) _targetBeacon.SetActive(false);
        }

        void Complete()
        {
            var m = Current;
            Current = null;
            ClearBeacons();
            ClearRaceBeacons();
            if (_targetBeacon) _targetBeacon.SetActive(false);
            int lvl = GameState.Level;
            bool first;
            if (m.isRace) { first = !GameState.Data.raceDone[lvl]; GameState.Data.raceDone[lvl] = true; }
            else
            {
                first = true;
                GameState.Data.missionsDone[lvl] = Mathf.Max(GameState.Data.missionsDone[lvl], story.IndexOf(m) + 1);
            }
            if (first) GameState.AddCoins(m.reward);
            GameState.Save();
            ProcAudio.Play2D(ProcAudio.Fanfare, 0.8f);
            HUD.I.BigMessage("MISI SELESAI!", first ? $"+RM{m.reward}" : "");
            if (m.outro != null && m.outro.Length > 0) Dialogue.Say(m.outro, AfterComplete);
            else AfterComplete();
        }

        void AfterComplete()
        {
            if (GameState.Data.missionsDone[GameState.Level] >= story.Count && story.Count > 0)
                GameManager.I.LevelComplete();
            else RefreshGivers();
        }

        void Fail(string reason)
        {
            var m = Current;
            if (Step != null) Step.End(this);
            Current = null;
            ClearBeacons();
            ClearRaceBeacons();
            if (_targetBeacon) _targetBeacon.SetActive(false);
            ProcAudio.Play2D(ProcAudio.Fail, 0.8f);
            HUD.I.BigMessage("MISI GAGAL", reason);
            SamanMeter.I.suppressed = false;
            // put everything back like H&R's retry
            Dialogue.Say(new[] { new Line("", "Tekan E untuk cuba lagi.") }, () =>
            {
                GameManager.I.ResetForRetry(_startPos, _startRot);
                StartMission(m);
            });
        }

        public void Abort()
        {
            if (!Active) return;
            if (Step != null) Step.End(this);
            Current = null;
            ClearBeacons();
            ClearRaceBeacons();
            if (_targetBeacon) _targetBeacon.SetActive(false);
            SamanMeter.I.suppressed = false;
            RefreshGivers();
        }

        public void NotifyPedHit(Pedestrian p) { }

        // ------------------------------------------------------------- beacons
        GameObject MakeBeacon(Vector3 at, float width)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Beacon";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.position = at + Vector3.up * 30f;
            go.transform.localScale = new Vector3(2.2f * width, 30f, 2.2f * width);
            go.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.Beam(new Color(1f, 0.82f, 0.25f, 0.55f));
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public void Beacon(Vector3 at, float width) => _beacons.Add(MakeBeacon(at, width));

        void ClearBeacons()
        {
            foreach (var b in _beacons) if (b) Destroy(b);
            _beacons.Clear();
        }

        public void RaceBeacons(List<Vector3> cps, int next = 0)
        {
            ClearRaceBeacons();
            for (int i = 0; i < cps.Count; i++)
            {
                var b = MakeBeacon(cps[i], i == next % cps.Count ? 2.5f : 0.8f);
                if (i != next % cps.Count) b.GetComponent<MeshRenderer>().sharedMaterial = LatMaterials.Beam(new Color(0.5f, 0.75f, 1f, 0.35f));
                _raceBeacons.Add(b);
            }
        }

        void ClearRaceBeacons()
        {
            foreach (var b in _raceBeacons) if (b) Destroy(b);
            _raceBeacons.Clear();
        }
    }
}
