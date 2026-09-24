using System;
using UnityEngine;

namespace KampungRun
{
    /// <summary>Coins (Ringgit), Kad Lat collector cards and mission items.</summary>
    public class Pickup : MonoBehaviour
    {
        public enum Kind { Coin, Card, MissionItem }
        public Kind kind;
        public int value = 1;
        public string id;                 // cards: unique id for the save file
        public Action<Pickup> onCollected;
        public bool magnet = true;

        Vector3 _vel;
        bool _physics;
        float _age;
        float _baseY;

        public static Pickup SpawnCoin(Vector3 pos, bool burst = false, Transform parent = null)
        {
            var go = ModelFactory.Spawn("Prop_Coin", pos, Quaternion.identity, parent, "Coin");
            var p = Setup(go, Kind.Coin, 1);
            if (burst)
            {
                p._physics = true;
                p._vel = new Vector3(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(4f, 7f), UnityEngine.Random.Range(-3f, 3f));
                Destroy(go, 25f);
            }
            return p;
        }

        public static Pickup Setup(GameObject go, Kind kind, int value)
        {
            var p = go.AddComponent<Pickup>();
            p.kind = kind;
            p.value = value;
            p._baseY = go.transform.position.y;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = kind == Kind.Coin ? 0.9f : 1.3f;
            col.center = Vector3.zero;
            go.layer = Layers.Pickup;
            foreach (Transform t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Pickup;
            return p;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            transform.Rotate(0, 180f * dt, 0, Space.World);
            var player = PlayerController.I;
            if (_physics)
            {
                _vel += Physics.gravity * dt;
                var next = transform.position + _vel * dt;
                if (Physics.Raycast(transform.position, _vel.normalized, out var hit, _vel.magnitude * dt + 0.3f, ~(1 << Layers.Pickup | 1 << Layers.Character | 1 << Layers.Vehicle), QueryTriggerInteraction.Ignore))
                {
                    next = hit.point + hit.normal * 0.35f;
                    _vel = Vector3.Reflect(_vel, hit.normal) * 0.4f;
                    if (_vel.magnitude < 1.2f) { _physics = false; _baseY = next.y + 0.4f; }
                }
                transform.position = next;
            }
            else
            {
                var p = transform.position;
                p.y = _baseY + Mathf.Sin(_age * 3f) * 0.15f;
                transform.position = p;
            }
            // anything: touch it to take it (distance check, works on foot and in cars)
            if (player != null && _age > 0.3f)
            {
                float reach = player.Driving ? 3.2f : 1.4f;
                if ((player.Focus - transform.position).sqrMagnitude < reach * reach) { Collect(); return; }
            }
            // coins fly to the player like in H&R
            if (magnet && kind == Kind.Coin && player != null && _age > 0.5f)
            {
                var to = player.Focus - transform.position;
                float d = to.magnitude;
                float r = player.Driving ? 6f : 3.5f;
                if (d < r)
                {
                    _physics = false;
                    transform.position += to.normalized * Mathf.Min(d, (25f - d * 2f) * dt);
                    if (d < 1.2f) Collect();
                }
            }
        }

        void OnTriggerEnter(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerController>();
            var v = other.attachedRigidbody ? other.attachedRigidbody.GetComponent<Vehicle>() : null;
            if (pc != null || (v != null && v.PlayerInside)) Collect();
        }

        bool _done;

        public void Collect()
        {
            if (_done) return;
            _done = true;
            switch (kind)
            {
                case Kind.Coin:
                    GameState.AddCoins(value);
                    ProcAudio.Play(ProcAudio.Coin, transform.position, 0.35f, UnityEngine.Random.Range(1f, 1.15f));
                    break;
                case Kind.Card:
                    GameState.CollectCard(id);
                    ProcAudio.Play(ProcAudio.Fanfare, transform.position, 0.7f);
                    HUD.I?.Toast($"KAD LAT! ({GameState.CardsInLevel(GameState.Level)}/{GameData.CardsPerLevel})");
                    break;
                case Kind.MissionItem:
                    ProcAudio.Play(ProcAudio.Coin, transform.position, 0.6f, 0.7f);
                    break;
            }
            Fx.Burst(transform.position, LatMaterials.Pal.Coin, 5, 3f);
            onCollected?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
