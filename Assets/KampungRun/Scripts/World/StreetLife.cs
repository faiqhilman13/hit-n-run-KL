using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Life the city builder doesn't make on its own: a hawker at every satay cart (fanning the grill,
    /// crying "Satay! Satay!"), pigeons in the squares, kids playing football on the kampung padang, and
    /// the sound of the place underneath it all.
    /// </summary>
    public static class StreetLife
    {
        public static void Populate(CityBuilder.City city, Transform parent)
        {
            var root = new GameObject("StreetLife").transform;
            root.SetParent(parent, false);
            Pigeons.Populate(city, root);
            int i = 0;
            foreach (var cart in city.satayCarts)
            {
                if (cart == null) continue;
                bool aunty = i % 3 == 1;
                var at = cart.position - cart.forward * 1.05f;
                var npc = NPC.Spawn("satay" + i, aunty ? "Makcik Satay" : "Pakcik Satay", aunty ? "chr_townaunty" : "chr_pakcik", at,
                    Quaternion.LookRotation(cart.forward), root);
                npc.idleLine = "Satay! Sepuluh cucuk lima ringgit. Kuah kacang lebih, free!";
                i++;
            }
            // a kickabout on the kampung padang
            if (city.places.TryGetValue("Padang", out var pad))
                Football.Spawn(pad, 30f * CityBuilder.LandmarkScale, 22f * CityBuilder.LandmarkScale, root);
            Ambience.Ensure(parent);
        }
    }
}
