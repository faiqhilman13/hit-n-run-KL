using UnityEngine;

namespace KampungRun
{
    public partial class CityBuilder
    {
        /// <summary>How leafy it is here, 0 (downtown concrete) .. 1 (kampung): birdsong against traffic in the ambience.</summary>
        public static float Greenery(Vector3 p)
        {
            int col = Mathf.FloorToInt((p.x - X0) / Pitch), row = Mathf.FloorToInt((p.z - Z0) / Pitch);
            if (col < 0 || col >= NX || row < 0 || row >= NZ) return 0.4f;
            var patch = PatchAt(col, row);
            if (patch != null)
                switch (patch.name)
                {
                    case "TamanTasik": return 0.85f;         // the Lake Gardens
                    case "BukitNanas": return 0.6f;          // the forest round KL Tower
                    case "KLCC": return 0.25f;               // the park under the towers
                    case "KotaLama": return 0.12f;
                    default: return 0.05f;
                }
            switch (ZoneAt(col, row))
            {
                case Zone.Kampung: case Zone.Home: case Zone.Surau: case Zone.Padang: return 1f;
                case Zone.Park: case Zone.BatuCaves: return 0.75f;
                case Zone.River: case Zone.IstanaNegara: return 0.4f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Make the top of a prop (a shop awning, a stall's canopy) a trampoline: a thin box just above it that
        /// fires you back up. It stands beside the prop rather than inside it, so the prop still merges with
        /// the rest of the street, and sits on "Ignore Raycast" so the camera passes through it.
        /// </summary>
        Bouncy Trampoline(GameObject prop, float power, float inset = 0.9f)
        {
            var b = ModelFactory.LocalBounds(prop);
            var go = new GameObject("Bouncy");
            go.transform.SetParent(_props, false);
            go.transform.SetPositionAndRotation(prop.transform.position, prop.transform.rotation);
            go.transform.localScale = prop.transform.localScale;
            go.layer = 2;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(b.size.x * inset, 0.25f, b.size.z * inset);
            bc.center = new Vector3(b.center.x, b.max.y - 0.065f, b.center.z);
            var bo = go.AddComponent<Bouncy>();
            bo.power = power;
            return bo;
        }

        /// <summary>World height of the top of a prop (its proxy collider if it has one: what you stand on).</summary>
        static float TopOf(GameObject go)
        {
            var bc = go.GetComponent<BoxCollider>();
            if (bc != null && bc.enabled) return go.transform.TransformPoint(bc.center + Vector3.up * bc.size.y * 0.5f).y;
            var b = ModelFactory.LocalBounds(go);
            return go.transform.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)).y;
        }

        // the reach of an awning bounce (11 m/s) plus a double jump, with a little to spare
        const float BounceReach = 5f;
        bool _loggedRoofs;

        /// <summary>
        /// Chow Kit's shophouse awnings are trampolines: a coin hangs over every other one, and where the roof
        /// is in reach of a bounce and a double jump, a coin waits up there too - a route over the market.
        /// </summary>
        void ShopAwningBounce(GameObject shop, GameObject awning, Vector3 front)
        {
            Trampoline(awning, 11f);
            float awningTop = TopOf(awning);
            var over = awning.transform.position;
            if ((_signIdx & 1) == 0) _city.coinSpots.Add(new Vector3(over.x, awningTop + 2.4f, over.z));
            float roof = TopOf(shop);
            if (!_loggedRoofs)
            {
                _loggedRoofs = true;
                Debug.Log($"[StreetLife] Chow Kit awning top {awningTop - G:F2} m, roof {roof - G:F2} m (reachable: {roof - awningTop < BounceReach})");
            }
            if (roof - awningTop < BounceReach)
                _city.coinSpots.Add(shop.transform.position - front * 1.5f + Vector3.up * (roof - shop.transform.position.y + 1f));
        }

        int _stallCoins;

        /// <summary>A night-market stall: its canopy is a trampoline, with a coin above every other one.</summary>
        void StallBounce(GameObject stall)
        {
            Trampoline(stall, 10f, 0.85f);
            if ((_stallCoins++ & 1) == 0)
            {
                var p = stall.transform.position;
                _city.coinSpots.Add(new Vector3(p.x, TopOf(stall) + 2.2f, p.z));
            }
        }

        // ------------------------------------------------------------------ the real-KL patches' pavements
        // the clutter of a KL five-foot way: what the patches' streets were missing (the filler blocks have it)
        // kind: 0 scenery (merged), 1 smashes (coins), 2 topples, 3 stools in a huddle
        static readonly (string model, int weight, int kind)[] Clutter =
        {
            ("Prop_Crate", 5, 1), ("env_crate_stack", 3, 1), ("env_crate_oranges", 3, 1),
            ("Prop_Kapcai", 4, 2), ("Prop_Kapcai2", 4, 2),
            ("env_kb_flower_pot", 3, 0), ("env_kb_flower_pot_white", 2, 0), ("env_planter", 2, 0), ("Prop_Bin", 3, 0),
            ("env_tarp_stack", 1, 0), ("env_stool_red", 3, 3),
        };

        /// <summary>
        /// Dress the patches' pavements: every so often along each loop, on the shop side (never in the walking
        /// line, never in the road), a crate, a parked kapcai, a pot plant, a bin, a huddle of stools. Busy
        /// streets get more. The smashables are capped (each keeps its own renderer); the rest merge.
        /// </summary>
        void DressPatches()
        {
            Physics.SyncTransforms();
            const int MaxProps = 1100, MaxBreakables = 260;
            const int Mask = ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle | 1 << 2);
            var rng = new System.Random(4242);          // its own dice, so nothing else in the city moves
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int total = 0;
            foreach (var c in Clutter) total += c.weight;
            int placed = 0, breakables = 0, loops = 0;
            float Wall(Vector3 from, Vector3 dir) =>
                Physics.Raycast(from + Vector3.up * 1.0f, dir, out var h, 6f, Mask, QueryTriggerInteraction.Ignore) ? h.distance : 99f;
            foreach (var z in _city.walkZones)
            {
                if (z.kampung || !InsidePatch(new Vector3(z.center.x, 0f, z.center.y))) continue;
                loops++;
                for (float s = Rn(2f, 10f); s < z.perimeter && placed < MaxProps; s += Rn(10f, 22f) / Mathf.Clamp(z.busy, 1f, 1.8f))
                {
                    var p = z.PointAt(s, 0f);
                    var d = z.PointAt(s + 0.6f, 0f) - z.PointAt(s - 0.6f, 0f);
                    d.y = 0f;
                    if (d.sqrMagnitude < 1e-4f) continue;
                    d.Normalize();
                    var n = new Vector3(d.z, 0f, -d.x);
                    if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var here, 6f, Mask, QueryTriggerInteraction.Ignore)) continue;
                    p.y = here.point.y;
                    // the shop side is whichever side a wall is closer on; out in the open, leave it be
                    float wl = Wall(p, n), wr = Wall(p, -n);
                    if (wl > 6f && wr > 6f) continue;
                    var inward = wl < wr ? n : -n;
                    float off = Mathf.Min(Mathf.Min(wl, wr) - 0.6f, 2.2f);
                    if (off < 1.35f) continue;                                   // too narrow: keep it clear for walking
                    var at = p + inward * off;
                    if (!Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var ground, 4f, Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(ground.point.y - p.y) > 0.25f) continue;       // a step, a kerb, a planter: not here
                    at = ground.point;
                    if (Physics.CheckSphere(at + Vector3.up * 0.75f, 0.5f, Mask, QueryTriggerInteraction.Ignore)) continue;

                    int roll = rng.Next(total), k = 0;
                    while (roll >= Clutter[k].weight) { roll -= Clutter[k].weight; k++; }
                    var (model, _, kind) = Clutter[k];
                    if (kind != 0 && kind != 3 && breakables >= MaxBreakables) { model = "env_kb_flower_pot"; kind = 0; }
                    float along = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    switch (kind)
                    {
                        case 1:
                        {
                            var cr = Prop(model, at, Rn(0f, 360f), false);
                            SetStatic(cr, false);
                            if (model.StartsWith("env_")) ModelFactory.UseProxyCollider(cr);
                            Breakable.Make(cr, Breakable.Kind.Shatter, 3);
                            breakables++;
                            break;
                        }
                        case 2:
                        {
                            // parked along the kerb line, nose out
                            var bike = Prop(model, at, along + 90f + Rn(-15f, 15f), false);
                            SetStatic(bike, false);
                            ModelFactory.AddBoundsCollider(bike, 0.05f);
                            Breakable.Make(bike, Breakable.Kind.Topple, 1);
                            breakables++;
                            break;
                        }
                        case 3:
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                var sp = at + d * (i - 1) * 0.75f + inward * Rn(-0.15f, 0.15f);
                                Prop(i == 1 ? "env_stool_blue" : "env_stool_red", sp, Rn(0f, 360f), false);
                            }
                            break;
                        }
                        default:
                        {
                            var go = Prop(model, at, Rn(0f, 360f), !model.StartsWith("env_"), 1f);
                            if (model.StartsWith("env_")) ModelFactory.UseProxyCollider(go);
                            break;
                        }
                    }
                    placed++;
                }
            }
            Debug.Log($"[StreetLife] patch pavements: {placed} props on {loops} loops ({breakables} smashable)");
        }
    }
}
