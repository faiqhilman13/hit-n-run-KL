using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// The "Hit &amp; Run" meter, KL edition: every bump, knocked-over pakcik and flattened
    /// lamp post raises your SAMAN meter. Fill it and the polis come for you. Get caught
    /// sitting still next to them and it's a RM50 fine ("KENA SAMAN!").
    /// </summary>
    public class SamanMeter : MonoBehaviour
    {
        public static SamanMeter I { get; private set; }
        public float heat;                 // 0..100
        public bool Wanted { get; private set; }
        public float BustProgress { get; private set; }
        public bool suppressed;            // during some missions / cutscenes

        readonly List<Vehicle> _police = new List<Vehicle>();
        float _calm;
        float _spawnTimer;
        AudioSource _siren;
        const int Fine = 50;

        void Awake() => I = this;

        public void AddHeat(float amount)
        {
            if (suppressed) return;
            heat = Mathf.Min(100f, heat + amount);
            _calm = 0f;
            if (heat >= 100f && !Wanted) StartWanted();
        }

        public void ForceWanted()
        {
            heat = 100f;
            if (!Wanted) StartWanted();
        }

        void StartWanted()
        {
            Wanted = true;
            _spawnTimer = 0f;
            HUD.I?.Toast("POLIS DATANG!");
            if (_siren == null) _siren = ProcAudio.Loop(ProcAudio.Siren, transform, 0.25f);
            _siren.Play();
        }

        void EndWanted(bool busted)
        {
            Wanted = false;
            BustProgress = 0f;
            heat = 0f;
            if (_siren) _siren.Stop();
            foreach (var p in _police)
                if (p)
                {
                    p.driver = new TrafficDriver(GameManager.I.City.roads);
                    p.role = VehicleRole.Traffic;
                    Destroy(p.gameObject, 25f);
                }
            _police.Clear();
            if (!busted) HUD.I?.Toast("Dah lepas!");
        }

        public void ClearAll() { if (Wanted) EndWanted(false); heat = 0f; }

        void Update()
        {
            var player = PlayerController.I;
            if (player == null || GameManager.I == null || !GameManager.I.Playing) return;
            float dt = Time.deltaTime;
            _calm += dt;

            if (!Wanted)
            {
                if (_calm > 2.5f) heat = Mathf.Max(0f, heat - 7f * dt);
                return;
            }

            if (_siren) _siren.transform.position = player.Focus;
            // keep 2-3 police cars on you
            _police.RemoveAll(p => p == null || p.Wrecked);
            _spawnTimer -= dt;
            int want = heat > 70 ? 3 : 2;
            if (_police.Count < want && _spawnTimer <= 0f)
            {
                _spawnTimer = 4f;
                SpawnPolice(player.Focus);
            }

            // escape: distance from police drains the meter
            float nearest = float.MaxValue;
            foreach (var p in _police) nearest = Mathf.Min(nearest, Vector3.Distance(p.transform.position, player.Focus));
            float drain = nearest > 60f ? 12f : nearest > 30f ? 4f : 1f;
            heat -= drain * dt;
            if (heat <= 0f) { EndWanted(false); return; }

            // busted: police right next to you while you're stopped (or on foot)
            float playerSpeed = player.Speed;
            bool close = nearest < (player.Driving ? 7f : 4f);
            if (close && playerSpeed < 3f) BustProgress += dt / 1.6f;
            else BustProgress = Mathf.Max(0f, BustProgress - dt);
            if (BustProgress >= 1f) Busted();
        }

        void SpawnPolice(Vector3 near)
        {
            var roads = GameManager.I.City.roads;
            var node = roads.RandomNodeAwayFrom(near, 50f, 120f);
            var v = VehicleSpawner.Spawn("polis", node.pos + Vector3.up * 0.6f,
                Quaternion.LookRotation(near - node.pos, Vector3.up), VehicleRole.Police);
            v.driver = new PoliceDriver();
            v.displayName = "Polis";
            _police.Add(v);
        }

        void Busted()
        {
            GameState.AddCoins(-Fine);
            GameState.Save();
            ProcAudio.Play2D(ProcAudio.Fail, 0.8f);
            HUD.I?.BigMessage("KENA SAMAN!", $"-RM{Fine}");
            EndWanted(true);
            // H&R: you get put out of the car
            var player = PlayerController.I;
            if (player.Driving) player.ExitVehicle(true);
        }
    }
}
