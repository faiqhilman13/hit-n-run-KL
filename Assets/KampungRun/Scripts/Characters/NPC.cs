using System;
using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>A named character you can talk to (mission givers, shopkeepers...).</summary>
    public class NPC : MonoBehaviour, IInteractable
    {
        public string key;
        public string displayName;
        public Action<NPC> onTalk;
        public bool hasMission;
        public bool talkable = true;
        public string idleLine;   // said when there's nothing else to do

        CharacterRig _rig;
        GameObject _marker;

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
            if (_rig) _rig.waving = hasMission && Mathf.Repeat(Time.time, 4f) < 1.5f;
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
