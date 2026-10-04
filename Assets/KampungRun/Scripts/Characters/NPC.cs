using System;
using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// A named character you can talk to (mission givers, shopkeepers...). They notice you: heads turn as
    /// you come near and they face you when you're close enough to talk; they say hello (or call you over
    /// when they've a job for you), and the stallholders call out to the street the way a pasar sounds.
    /// </summary>
    public class NPC : MonoBehaviour, IInteractable
    {
        public string key;
        public string displayName;
        public Action<NPC> onTalk;
        public bool hasMission;
        public bool talkable = true;
        public string idleLine;   // said when there's nothing else to do
        public string[] calls;    // a stallholder's cries, now and then, to anyone in earshot

        CharacterRig _rig;
        GameObject _marker;
        HeadLook _look;
        Quaternion _rest;
        bool _near;
        float _barkT, _callT;

        static readonly Dictionary<string, string[]> Cries = new Dictionary<string, string[]>
        {
            ["auntypasar"] = new[] { "Murah murah!", "Sayur segar, mari!", "Tiga seringgit!", "Adik, beli la sikit!" },
            ["anneh"] = new[] { "Teh tarik!", "Roti canai panas!", "Boss, makan boss?", "Mee goreng mamak!" },
            ["mei"] = new[] { "Mee hailam!", "Kopi peng, kopi-O!", "Duduk dulu, duduk!" },
            ["pakciktaksi"] = new[] { "Teksi! Teksi!", "Nak ke mana, boss?", "Murah je!" },
            ["satay"] = new[] { "Satay! Satay!", "Satay ayam, satay daging!", "Kuah kacang power!", "Mari, panas-panas!" },
        };
        static readonly string[] CallOver = { "Eh, {0}! Sini kejap!", "{0}! Tolong aku!", "Psst... {0}!", "{0}, mari sini!" };

        public string Prompt => hasMission ? $"Cakap dengan {displayName}  (MISI)" : $"Cakap dengan {displayName}";
        public Vector3 Position => transform.position;
        public float Range => 3.2f;
        public bool CanInteract => talkable && gameObject.activeInHierarchy && (onTalk != null || !string.IsNullOrEmpty(idleLine));

        public static NPC Spawn(string key, string name, string model, Vector3 pos, Quaternion rot, Transform parent,
            Dictionary<string, Color> recolor = null)
        {
            var go = ModelFactory.Spawn(model, pos, rot, parent, "NPC_" + key);
            if (recolor != null) ModelFactory.Recolor(go, recolor);
            var rig = go.AddComponent<CharacterRig>();
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Character;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.9f, 0);
            cap.height = 1.8f;
            cap.radius = 0.35f;
            var npc = go.AddComponent<NPC>();
            npc.key = key;
            npc.displayName = name;
            npc._rig = rig;
            npc._look = go.AddComponent<HeadLook>();
            npc._rest = rot;
            npc._callT = UnityEngine.Random.Range(3f, 12f);
            if (Cries.TryGetValue(key.StartsWith("satay") ? "satay" : key, out var cries)) npc.calls = cries;
            Interactables.All.Add(npc);
            return npc;
        }

        void OnDestroy() => Interactables.All.Remove(this);

        public void Interact(PlayerController p)
        {
            // turn to face the player
            var to = p.transform.position - transform.position;
            to.y = 0;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
            if (onTalk != null) onTalk(this);
            else Dialogue.Say(new[] { new Line(displayName, idleLine) });
        }

        void Update()
        {
            Notice(Time.deltaTime);
            if (_rig) _rig.waving = hasMission && Mathf.Repeat(Time.time, 4f) < 1.5f || _fanning;
            if (hasMission && _marker == null) _marker = MakeMarker();
            if (_marker)
            {
                _marker.SetActive(hasMission);
                if (hasMission)
                {
                    _marker.transform.position = transform.position + Vector3.up * (2.9f + Mathf.Sin(Time.time * 3f) * 0.15f);
                    var cam = Camera.main;
                    if (cam) _marker.transform.rotation = Quaternion.LookRotation(_marker.transform.position - cam.transform.position);
                }
            }
        }

        bool _fanning;
        float _smokeT;

        /// <summary>Watch the player come and go, greet them, and (stallholders) cry their wares.</summary>
        void Notice(float dt)
        {
            _barkT -= dt;
            var pc = PlayerController.I;
            if (pc == null) return;
            var to = pc.transform.position - transform.position;
            to.y = 0f;
            float d2 = to.sqrMagnitude;
            bool onFoot = !pc.Driving;
            if (_look) { if (onFoot && d2 < 8f * 8f) _look.LookAt(pc.transform); else _look.Clear(); }
            // close enough to talk: turn to face you; otherwise back the way they were standing
            var want = onFoot && d2 < 4f * 4f && d2 > 0.01f ? Quaternion.LookRotation(to) : _rest;
            if (Quaternion.Angle(transform.rotation, want) > 0.5f)
                transform.rotation = Quaternion.Slerp(transform.rotation, want, dt * (want == _rest ? 2f : 5f));
            // say hello the first time you come near (or call you over when there's a job going)
            bool near = onFoot && d2 < 7f * 7f;
            if (near && !_near && _barkT <= 0f && !Dialogue.Showing && talkable)
            {
                var who = pc.def != null ? pc.def.name : null;
                // a bubble wants a few words: the long idle lines are for when you stop and talk
                var line = hasMission ? Barks.Pick(CallOver, who)
                    : !string.IsNullOrEmpty(idleLine) && idleLine.Length <= 28 ? idleLine : Barks.Pick(Barks.Greet, who);
                if (Barks.Say(transform, key, line, true, 2.45f, 0.8f)) _barkT = 25f;
            }
            _near = near;
            if (calls == null) return;
            // a stallholder calls out to the street (and a satay man fans his grill)
            _fanning = key.StartsWith("satay") && Mathf.Repeat(Time.time + _rest.y * 10f, 3f) < 1.6f;
            if (_fanning && (_smokeT -= dt) <= 0f && d2 < 45f * 45f)
            {
                _smokeT = UnityEngine.Random.Range(0.35f, 0.7f);
                Fx.Puff(transform.position + transform.forward * 0.9f + Vector3.up * 1.15f, new Color(0.88f, 0.88f, 0.86f),
                    Vector3.up * 1.1f + UnityEngine.Random.insideUnitSphere * 0.25f, 0.35f, 1.6f);
            }
            if ((_callT -= dt) > 0f) return;
            _callT = UnityEngine.Random.Range(9f, 16f);
            if (d2 < 30f * 30f && !Dialogue.Showing) Barks.Say(transform, key, calls[UnityEngine.Random.Range(0, calls.Length)], true, 2.45f, 0.75f);
        }

        GameObject MakeMarker()
        {
            var go = new GameObject("MissionMarker");
            go.transform.SetParent(transform, true);
            var tm = go.AddComponent<TextMesh>();
            tm.text = "!";
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.25f;
            tm.fontSize = 72;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.95f, 0.75f, 0.1f);
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            return go;
        }
    }

    /// <summary>
    /// Burung Kamera: MegaMaju's robot mynah birds spying on the city (the H&amp;R wasp
    /// cameras). They hover and bob; punch or kick them for a coin shower.
    /// </summary>
    public class BurungKamera : MonoBehaviour
    {
        public string id;
        public Action<BurungKamera> onSmashed;
        Vector3 _home;
        float _seed;

        public static BurungKamera Spawn(string id, Vector3 pos, Transform parent)
        {
            var go = ModelFactory.Spawn("Prop_BurungKamera", pos, Quaternion.identity, parent, "BurungKamera");
            var b = go.AddComponent<BurungKamera>();
            b.id = id;
            b._home = pos;
            b._seed = UnityEngine.Random.value * 10f;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.9f;
            col.isTrigger = true;
            col.center = Vector3.zero;
            return b;
        }

        void Update()
        {
            float t = Time.time + _seed;
            transform.position = _home + new Vector3(Mathf.Sin(t * 0.7f) * 1.5f, Mathf.Sin(t * 2.1f) * 0.4f, Mathf.Cos(t * 0.5f) * 1.5f);
            var p = PlayerController.I;
            if (p != null)
            {
                var to = p.Focus - transform.position;
                to.y = 0;
                if (to.sqrMagnitude > 0.1f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 3f);
            }
        }

        bool _dead;

        public void Smash()
        {
            if (_dead) return;
            _dead = true;
            Fx.Burst(transform.position, new Color(0.3f, 0.3f, 0.35f), 16, 6f);
            Fx.Word(transform.position + Vector3.up, "PRANG!");
            ProcAudio.Play(ProcAudio.Smash, transform.position, 0.9f, 1.3f);
            for (int i = 0; i < 8; i++) Pickup.SpawnCoin(transform.position, true);
            if (!string.IsNullOrEmpty(id)) GameState.SmashCamera(id);
            HUD.I?.Toast($"BURUNG KAMERA! ({GameState.CamerasInLevel(GameState.Level)}/{GameData.CamerasPerLevel})");
            onSmashed?.Invoke(this);
            Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            // ramming one with a car counts too
            var rb = other.attachedRigidbody;
            var v = rb ? rb.GetComponent<Vehicle>() : null;
            if (v != null && v.PlayerInside) Smash();
        }
    }

    /// <summary>Pondok Telefon: summon any car you own (H&amp;R phone booth).</summary>
    public class PhoneBooth : MonoBehaviour, IInteractable
    {
        public string Prompt => "Pondok Telefon - panggil kereta";
        public Vector3 Position => transform.position;
        public float Range => 3f;
        public bool CanInteract => GameManager.I.Missions == null || !GameManager.I.Missions.Active;

        void OnEnable() => Interactables.All.Add(this);
        void OnDisable() => Interactables.All.Remove(this);

        public void Interact(PlayerController p)
        {
            var owned = new List<CarDef>();
            foreach (var c in GameData.Cars)
                if ((c.price == 0 && c.level <= GameState.Level) || GameState.Data.ownedCars.Contains(c.id)) owned.Add(c);
            var items = new List<string>();
            foreach (var c in owned) items.Add(c.name);
            HUD.I.Menu("PONDOK TELEFON", items, i =>
            {
                var spot = transform.position + transform.forward * 6f + Vector3.up * 0.6f;
                GameManager.I.SummonCar(owned[i].id, spot, transform.rotation * Quaternion.Euler(0, 90, 0));
            });
        }
    }

    /// <summary>Kedai Baju: buy costumes for whoever you're playing.</summary>
    public class ClothesShop : MonoBehaviour, IInteractable
    {
        public string Prompt => "Kedai Baju";
        public Vector3 Position => transform.position;
        public float Range => 3f;
        public bool CanInteract => true;

        void OnEnable() => Interactables.All.Add(this);
        void OnDisable() => Interactables.All.Remove(this);

        public void Interact(PlayerController p)
        {
            var list = GameData.Costumes.FindAll(c => c.character == p.def.id);
            var items = new List<string> { "Baju biasa" };
            foreach (var c in list)
            {
                bool owned = GameState.Data.ownedCostumes.Contains(c.id);
                items.Add(owned ? $"{c.name}  (ada)" : $"{c.name}  RM{c.price}");
            }
            HUD.I.Menu($"KEDAI BAJU - {p.def.name}", items, i =>
            {
                if (i == 0) { GameState.Wear(p.def.id, "default"); p.SetCharacter(p.def.id); return; }
                var c = list[i - 1];
                if (!GameState.Data.ownedCostumes.Contains(c.id))
                {
                    if (!GameState.Spend(c.price)) { HUD.I.Toast("Duit tak cukup!"); ProcAudio.Play2D(ProcAudio.Fail, 0.5f); return; }
                    GameState.Data.ownedCostumes.Add(c.id);
                    ProcAudio.Play2D(ProcAudio.Fanfare, 0.6f);
                }
                GameState.Wear(p.def.id, c.id);
                GameState.Save();
                p.SetCharacter(p.def.id);
                HUD.I.Toast($"Nampak segak! ({c.name})");
            });
        }
    }

    /// <summary>Kedai Kereta: buy the cars on sale this level.</summary>
    public class CarDealer : MonoBehaviour, IInteractable
    {
        public string Prompt => "Kedai Kereta Terpakai";
        public Vector3 Position => transform.position;
        public float Range => 3.2f;
        public bool CanInteract => GameManager.I.Missions == null || !GameManager.I.Missions.Active;

        void OnEnable() => Interactables.All.Add(this);
        void OnDisable() => Interactables.All.Remove(this);

        public void Interact(PlayerController p)
        {
            var list = GameData.Cars.FindAll(c => c.price > 0 && c.level <= GameState.Level);
            var items = new List<string>();
            foreach (var c in list)
                items.Add(GameState.Data.ownedCars.Contains(c.id) ? $"{c.name}  (sudah beli - pandu)" : $"{c.name}  RM{c.price}");
            HUD.I.Menu("KEDAI KERETA", items, i =>
            {
                var c = list[i];
                if (!GameState.Data.ownedCars.Contains(c.id))
                {
                    if (!GameState.Spend(c.price)) { HUD.I.Toast("Duit tak cukup!"); ProcAudio.Play2D(ProcAudio.Fail, 0.5f); return; }
                    GameState.Data.ownedCars.Add(c.id);
                    GameState.Save();
                    ProcAudio.Play2D(ProcAudio.Fanfare, 0.7f);
                    HUD.I.Toast($"{c.name} kau punya!");
                }
                var spot = transform.position + transform.forward * 7f + Vector3.up * 0.6f;
                GameManager.I.SummonCar(c.id, spot, transform.rotation * Quaternion.Euler(0, 90, 0));
            });
        }
    }
}
