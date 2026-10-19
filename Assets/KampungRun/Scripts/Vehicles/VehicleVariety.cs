using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Optional parts on the Hit &amp; Run car models, so a busy street doesn't look like clones. kl_cars.py exports
    /// them as Var_&lt;Group&gt;_&lt;Option&gt;_&lt;Where&gt;[_LOD0] and the importer switches them off; Apply turns on
    /// none or one option per group from a seed. Groups: Dirt, Primer (worn cars), Rack (roof rack), Sticker
    /// (P plate, club sticker, side stripes), Shelf (tissue box and bear on the parcel shelf), Hubcap, Curtain
    /// (MPV), Load (pickup tray), Box / Basket / Visor (kapcai). Paint stays with ModelFactory.Recolor (car_paint).
    /// Traffic gets this in Vehicle.Setup; anything else (parked kerbside cars) can call Apply with its own seed.
    /// </summary>
    public static class VehicleVariety
    {
        static readonly Dictionary<string, float> Odds = new Dictionary<string, float>
        {
            ["Dirt"] = 0.5f, ["Primer"] = 0.25f, ["Rack"] = 0.22f, ["Sticker"] = 0.5f, ["Shelf"] = 0.5f, ["Hubcap"] = 0.45f,
            ["Curtain"] = 0.6f, ["Load"] = 0.65f, ["Box"] = 0.45f, ["Basket"] = 0.5f, ["Visor"] = 0.35f,
        };

        /// <summary>Pick the optional parts for this car. wear (0..1) scales how likely dirt and primer patches are.</summary>
        public static void Apply(GameObject car, int seed, float wear = 0.5f)
        {
            var rnd = new System.Random(seed);
            foreach (var group in Collect(car))
            {
                float p = Odds.TryGetValue(group.Key, out var o) ? o : 0.4f;
                if (group.Key == "Dirt" || group.Key == "Primer") p *= Mathf.Clamp01(wear) * 2f;
                var options = new List<string>(group.Value.Keys);
                options.Sort(System.StringComparer.Ordinal);
                string pick = rnd.NextDouble() < p ? options[rnd.Next(options.Count)] : null;
                foreach (var option in group.Value)
                    foreach (var go in option.Value)
                        go.SetActive(option.Key == pick);
            }
        }

        /// <summary>Back to the clean car (every optional part off).</summary>
        public static void Clear(GameObject car)
        {
            foreach (var group in Collect(car))
                foreach (var option in group.Value)
                    foreach (var go in option.Value)
                        go.SetActive(false);
        }

        /// <summary>group -> option -> nodes, from the Var_ names.</summary>
        public static Dictionary<string, Dictionary<string, List<GameObject>>> Collect(GameObject car)
        {
            var groups = new Dictionary<string, Dictionary<string, List<GameObject>>>();
            foreach (var t in car.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Var_")) continue;
                var bits = t.name.Split('_');
                if (bits.Length < 3) continue;
                if (!groups.TryGetValue(bits[1], out var options)) groups[bits[1]] = options = new Dictionary<string, List<GameObject>>();
                if (!options.TryGetValue(bits[2], out var nodes)) options[bits[2]] = nodes = new List<GameObject>();
                nodes.Add(t.gameObject);
            }
            return groups;
        }
    }
}
