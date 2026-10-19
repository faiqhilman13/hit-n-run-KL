using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// The story. MegaMaju Berhad (boss: the very round Datuk Mega) is feeding KL a
    /// "Cendol Ajaib" that turns people into obedient zombies, and watching everyone with
    /// robot mynah birds. One kampung family from Kampung Baru figures it out.
    ///
    ///   Tahap 1  Pak Mat  - Teh Tarik Tragedi   (morning)
    ///   Tahap 2  Mak Som  - Cendol Ajaib        (afternoon)
    ///   Tahap 3  Along    - Rempit Senja        (sunset)
    ///   Tahap 4  Adik     - Malam Menara        (night, finale at Menara Kembar)
    ///   Tahap 5  Aiman    - Penghantar Chow Kit (after the rain; the KL handoff cast)
    ///
    /// Missions pass (20 Oct 2026): Hit &amp; Run was knocked for drive-here / collect-these over and over, so every
    /// mission now has one clear verb that differs from its neighbours (drive clean, photograph, search rooms, wreck,
    /// sneak, haggle, carry carefully, tail, defend, escort, disguise, hide, brawl, ram, boss, chase on foot) and at
    /// most one plain driving job per chapter, which has a twist of its own. See docs/AGENT_HANDOFF.md for the table.
    /// </summary>
    public static class MissionBook
    {
        public class Book
        {
            public List<Mission> story = new List<Mission>();
            public Mission race;
            public Line[] levelIntro;
        }

        static CityBuilder.City _city;
        static MissionManager _mm;
        static Transform _root;

        static Vector3 P(string key) => _city.places.TryGetValue(key, out var p) ? p : Vector3.zero;
        static Line L(string who, string text) => new Line(who, text);
        /// <summary>A spot on the pavement by a place (in the real-KL districts places sit at the kerb),
        /// optionally a few metres along it.</summary>
        static Vector3 Walk(Vector3 p, float along = 0f) => CityBuilder.InsidePatch(p) ? _city.Sidewalk(p, along) : p;
        /// <summary>The street by a named place: race checkpoints and chase routes run along the roads.</summary>
        static Vector3 Rd(string key) => _city.roads.Nearest(P(key), true).pos;
        /// <summary>The grid crossroads of north-south road i and east-west road k.</summary>
        static Vector3 J(int i, int k) => _city.roads.Nearest(new Vector3(CityBuilder.RoadX(i), 0f, CityBuilder.RoadZ(k)), true).pos;

        static readonly Dictionary<string, Color> Black = new Dictionary<string, Color>
        {
            ["CarPink"] = new Color(0.14f, 0.14f, 0.16f), ["CarGreen"] = new Color(0.12f, 0.12f, 0.14f),
            ["White"] = new Color(0.55f, 0.1f, 0.12f),
        };

        /// <summary>Pick n spots for collectibles/targets within radius of a centre.</summary>
        static List<Vector3> Near(Vector3 center, float radius, int n, int seed, float lift = 0.8f)
        {
            var rng = new System.Random(seed);
            radius *= CityBuilder.WorldScale;                   // radii were set on the old compressed map
            var pool = new List<Vector3>();
            foreach (var s in _city.itemSpots) if (Vector3.Distance(s, center) < radius) pool.Add(s);
            var result = new List<Vector3>();
            while (result.Count < n && pool.Count > 0)
            {
                int k = rng.Next(pool.Count);
                result.Add(pool[k] + Vector3.up * (lift - 0.6f));
                pool.RemoveAt(k);
            }
            int guard = 0;
            while (result.Count < n && guard++ < 400)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = (float)rng.NextDouble() * radius;
                var p = center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (!Physics.Raycast(p + Vector3.up * 80f, Vector3.down, out var hit, 120f, ~(1 << Layers.Character | 1 << Layers.Pickup | 1 << Layers.Vehicle), QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y > 1.2f || hit.point.y < -0.5f) continue; // roofs / river
                bool clash = false;
                foreach (var q in result) if (Vector3.Distance(q, hit.point) < 8f) clash = true;
                if (clash) continue;
                result.Add(hit.point + Vector3.up * lift);
            }
            return result;
        }

        static NPC Npc(string key, string name, string model, Vector3 pos, float yaw, string idle = null, Dictionary<string, Color> paint = null)
        {
            var n = NPC.Spawn(key, name, model, pos, Quaternion.Euler(0, yaw, 0), _root, paint);
            n.idleLine = idle;
            _mm.npcs[key] = n;
            return n;
        }

        static Mission M(string title, string giver, int reward, Line[] intro, Line[] outro, params Objective[] steps)
        {
            var m = new Mission { title = title, giver = giver, reward = reward, intro = intro, outro = outro };
            m.objectives.AddRange(steps);
            return m;
        }

        static Line[] Ls(params Line[] l) => l;

        public static Book Build(int level, CityBuilder.City city, MissionManager mm, Transform root)
        {
            _city = city; _mm = mm; _root = root;
            Physics.SyncTransforms();
            SpawnCast(level);
            return level switch
            {
                1 => Level1(),
                2 => Level2(),
                3 => Level3(),
                4 => Level4(),
                _ => Level5(),
            };
        }

        // ------------------------------------------------------------------ cast
        static void SpawnCast(int level)
        {
            string player = GameData.Levels[level].player;
            var home = P("HomeVerandah");
            var ck = P("ChowKit");
            if (player != "maksom") Npc("maksom", "Mak Som", "chr_maksom", home + new Vector3(0, 0, 1.5f), 90,
                "Jangan balik lambat, nanti nasi sejuk.");
            if (player != "pakmat") Npc("pakmat", "Pak Mat", "chr_pakmat", P("HomeYard"), 90,
                level == 2 ? "Cen... cendol... sedapnya cendol... (Pak Mat nampak pelik)" : "Hati-hati bawa kereta Ayah tu!");
            if (player != "along") Npc("along", "Along", "chr_along", P("Home") + new Vector3(1, 0, 4), 180, "Chill la, relaks dulu.");
            if (player != "adik") Npc("adik", "Adik", "chr_adik", P("Home") + new Vector3(2.5f, 0, -3), 0, "Nak aiskrim!");

            Npc("tokketua", "Tok Ketua", "chr_pakcik", P("Surau"), 180, "Assalamualaikum. Kampung kita ni makin pelik sejak MegaMaju datang.");
            Npc("anneh", "Anneh Mamak", "chr_townman", P("MamakCounter"), 0, "Teh tarik satu? Roti canai kosong? Boleh boss!",
                new Dictionary<string, Color> { ["BatikBlue"] = new Color(0.95f, 0.95f, 0.92f), ["Pants"] = new Color(0.35f, 0.4f, 0.6f) });
            Npc("auntypasar", "Aunty Pasar", "chr_townaunty", Walk(P("Pasar"), -10f), 90, "Murah murah! Sayur segar!");
            Npc("inspektor", "Inspektor Rosli", "chr_polis", Walk(P("Dataran")), 180, "Saya perhatikan kamu. Jangan buat kacau di KL.");
            Npc("matrempit", "Mat Rempit", "chr_along", P("Padang") + new Vector3(0, 0, -18), 0, "Weh, nak lumba ke tak?",
                new Dictionary<string, Color> { ["TshirtRed"] = new Color(0.15f, 0.15f, 0.18f), ["Shorts"] = new Color(0.25f, 0.4f, 0.8f) });
            Npc("pakciktaksi", "Pakcik Teksi", "chr_pakcik", Walk(P("Dataran"), 8f), 180, "Teksi, teksi! Meter? Tak payah la meter.",
                new Dictionary<string, Color> { ["White"] = new Color(0.8f, 0.3f, 0.25f) });
            Npc("profkassim", "Prof. Kassim", "chr_pakcik", Walk(P("KLTower")), 180, "Menurut kajian saya... sesuatu tak kena dengan cendol itu.",
                new Dictionary<string, Color> { ["White"] = new Color(0.97f, 0.97f, 0.97f), ["Sarong"] = new Color(0.4f, 0.4f, 0.45f), ["Songkok"] = new Color(0.8f, 0.8f, 0.8f) });
            Npc("rival", "Joe Bintang", "chr_along", Walk(P("BukitBintang")), 90, "Kau? Lawan aku? Hahaha.",
                new Dictionary<string, Color> { ["TshirtRed"] = new Color(0.95f, 0.85f, 0.2f), ["Shorts"] = new Color(0.1f, 0.1f, 0.1f) });
            // Chow Kit regulars (Tahap 5 cast, from the KL handoff)
            Npc("mei", "Mei", "chr_mei", ck + new Vector3(-4, 0, 3), 90, "Mee hailam, kopi peng, semua ada!");
            Npc("ravi", "Ravi", "chr_ravi", ck + new Vector3(5, 0, -3), 270, "Semua aku kira. Semua.");
            if (player != "aiman") Npc("aiman", "Aiman", "chr_aiman", ck + new Vector3(0, 0, 6), 180, "Order lagi? Jom!");
            // Petaling Street's last honest gula melaka man (Tahap 2's haggle)
            Npc("ahchong", "Ah Chong", "chr_pakcik", Walk(P("Pasar"), 10f), 270, "Gula melaka asli, boss. Harga? Tanya MegaMaju la.",
                new Dictionary<string, Color> { ["White"] = new Color(0.95f, 0.95f, 0.9f), ["Sarong"] = new Color(0.25f, 0.3f, 0.45f), ["Songkok"] = new Color(0.9f, 0.85f, 0.7f) });
            if (level == 4) Npc("datuk", "Datuk Mega", "chr_datukmega", Walk(P("Towers"), 5f), 180, "Hmph.");
        }

        // ------------------------------------------------------------------ helpers for the new objective types
        /// <summary>An open spot on the ground near c (see MissionLooks.Open).</summary>
        static Vector3 O(Vector3 c, float r, int seed, float minR = 0f) => MissionLooks.Open(c, r, seed, minR);

        /// <summary>A guard pacing between two open spots near c.</summary>
        static GuardSpec Beat(Vector3 c, float r, int seed, float range = 11f) =>
            new GuardSpec { route = new[] { O(c, r, seed), O(c, r, seed + 1) }, range = range };

        /// <summary>Points inside walk-in rooms round c (stock hidden in the shops), topped up from the pavement.</summary>
        static List<Vector3> RoomSpots(Vector3 c, float radius, int n, System.Predicate<Arch.InteriorKind> kinds, int seed)
        {
            var res = new List<Vector3>();
            if (Interiors.I != null)
            {
                var rooms = new List<Arch.RoomSpec>();
                foreach (var r in Interiors.I.Rooms)
                    if (kinds(r.kind) && (r.Centre - c).sqrMagnitude < radius * radius) rooms.Add(r);
                rooms.Sort((a, b) => (a.Centre - c).sqrMagnitude.CompareTo((b.Centre - c).sqrMagnitude));
                foreach (var r in rooms)
                {
                    var p = r.f.P(r.W * 0.5f, 0.15f, -r.D * 0.55f);
                    bool clash = false;
                    foreach (var q in res) if (Vector3.Distance(p, q) < 6f) clash = true;
                    if (clash) continue;
                    res.Add(p);
                    if (res.Count >= n) break;
                }
            }
            if (res.Count < n) res.AddRange(Near(c, radius / CityBuilder.WorldScale, n - res.Count, seed, 0.2f));
            return res;
        }

        static bool Shop(Arch.InteriorKind k) => k >= Arch.InteriorKind.Runcit && k != Arch.InteriorKind.Mamak;
        static bool House(Arch.InteriorKind k) => k == Arch.InteriorKind.House || k == Arch.InteriorKind.Kitchen;

        /// <summary>Photograph n robot mynahs perched round c; they flit between 2n perches.</summary>
        static PhotoObjective BirdPhotos(string text, Vector3 c, float r, int n, int seed) => new PhotoObjective(text, n, (o, m) =>
        {
            // open sky over each perch (not lost in a tree crown), hovering a little above head height
            var perches = new List<Vector3>();
            for (int i = 0; i < n * 2; i++) perches.Add(O(c, r, seed * 13 + i * 7, r * 0.3f) + Vector3.up * 4.2f);
            for (int i = 0; i < n && i * 2 < perches.Count; i++)
            {
                var b = PerchedBird.Spawn(perches, i * 2, m.transform);
                o.subjects.Add(new PhotoObjective.Subject { t = b.transform, name = "Burung robot", bird = b });
            }
        });

        static readonly Color Teh = new Color(0.78f, 0.55f, 0.32f), Bubur = new Color(0.93f, 0.86f, 0.66f), Kuah = new Color(0.55f, 0.32f, 0.18f);

        // ------------------------------------------------------------------ Tahap 1
        static Book Level1()
        {
            var b = new Book();
            b.levelIntro = Ls(
                L("Pak Mat", "Aaah, pagi yang indah di Kampung Baru. Kereta dah basuh, ayam dah bagi makan..."),
                L("Mak Som", "ABANG! Mari sini kejap!"),
                L("", "Cakap dengan orang yang ada tanda ! di atas kepala untuk mula misi. Tekan E."));

            // DRIVE CLEAN: the one driving job of the chapter - and Pak Mat can't afford another saman
            b.story.Add(M("Teh Tarik Kurang Manis", "maksom", 50,
                Ls(L("Mak Som", "Abang, teh tarik dekat rumah dah habis. Pergi beli dekat mamak Anneh, kurang manis ya!"),
                   L("Mak Som", "Dan ingat: saman RM50 bulan lepas pun belum bayar. Jangan tambah lagi!"),
                   L("Pak Mat", "Ala, abang pandu macam pegawai JPJ la, Som.")),
                Ls(L("Mak Som", "Hmm... sedap, dan tak ada saman. Tapi Anneh cakap apa tadi? Ada orang MegaMaju datang kedai dia?")),
                new GetInCarObjective("Naik kereta Saga"),
                new GoToObjective(P("Mamak"), "Pandu ke Restoran Mamak Anneh", 0, true),
                new TalkObjective("anneh", "Cakap dengan Anneh",
                    L("Anneh", "Pak Mat! Teh tarik kurang manis, bungkus! Siap!"),
                    L("Anneh", "Eh boss, tadi ada orang pakai kot hitam, MegaMaju punya, nak saya jual 'Cendol Ajaib' dia."),
                    L("Anneh", "Saya cakap tak mau! Teh tarik saya no.1 la! Hati-hati boss, polis banyak hari ni."),
                    L("Pak Mat", "Cendol Ajaib? Pelik bunyinya.")),
                new CleanDriveObjective(P("HomeYard"), "Balik rumah - tanpa saman, sebelum teh sejuk!", 95) { checkpoint = true }));

            // PHOTOGRAPH: proof that Adik isn't making it up
            b.story.Add(M("Burung Pelik", "adik", 75,
                Ls(L("Adik", "Ayah! Ada burung robot! Mata dia kamera! Dia tengok Adik mandi!"),
                   L("Pak Mat", "Burung... robot? Adik tengok kartun banyak sangat ni."),
                   L("Adik", "Ambil gambar la kalau tak percaya! Tapi jangan dekat sangat, nanti dia terbang!")),
                Ls(L("Pak Mat", "Ya Allah, betul la robot. Zoom... ada tulisan: 'HAK MILIK MEGAMAJU BERHAD'."),
                   L("Adik", "Kan Adik dah cakap!")),
                BirdPhotos("Ambil gambar 3 burung robot (dari jauh!)", P("Home"), 120f, 3, 21),
                new TalkObjective("adik", "Tunjuk gambar pada Adik",
                    L("Adik", "Tengok! Adik betul! Ayah kena belanja aiskrim!"))));

            // SEARCH: the kenduri pot went walkabout round the kampung
            b.story.Add(M("Periuk Kenduri", "tokketua", 100,
                Ls(L("Tok Ketua", "Pak Mat, kenduri petang ni... tapi periuk besar surau hilang! Ada orang pinjam, tak pulang."),
                   L("Tok Ketua", "Mesti ada dalam salah satu rumah sekitar sini. Masuk, cari. Orang kampung tak kisah - kita semua saudara."),
                   L("Pak Mat", "Masuk rumah orang? Okay... assalamualaikum dulu la.")),
                Ls(L("Tok Ketua", "Alhamdulillah! Eh, kenapa ada label 'CENDOL AJAIB - SAMPEL PERCUMA' dalam periuk ni?"),
                   L("Pak Mat", "MegaMaju dah sampai ke kampung kita, Tok.")),
                new SearchObjective("Cari periuk besar dalam rumah-rumah kampung", P("Surau"), 160f, 5, House, "periuk besar", "Prop_TehTarik", 31),
                new TalkObjective("tokketua", "Pulangkan periuk pada Tok Ketua",
                    L("Tok Ketua", "Terima kasih. Sekarang kita boleh masak untuk satu kampung!"))));

            // WRECK: the classic - but it's a bus full of cendol
            b.story.Add(M("Bas Mini Samseng", "tokketua", 100,
                Ls(L("Tok Ketua", "Pak Mat, ada satu bas mini pink main langgar orang dekat jalan besar. Pemandunya minum cendol pelik tu!"),
                   L("Tok Ketua", "Hentikan dia sebelum ada yang cedera. Langgar sampai rosak!")),
                Ls(L("Pemandu Bas", "Eh... apa jadi? Kenapa saya kat sini? Saya minum cendol je tadi..."),
                   L("Tok Ketua", "Cendol lagi! Ini bukan kebetulan.")),
                new GetInCarObjective("Cari kereta"),
                new DestroyObjective("Rosakkan Bas Mini Samseng!", "basmini", P("Dataran"), 160, 0.8f, 16f)));

            // SNEAK: lift a tin off the black van while its minders aren't looking
            var pad = P("Padang");
            var tin = O(pad, 8f, 51, 3f);
            var sneak1 = new SneakObjective("Curi satu tin Cendol Ajaib dari van - jangan sampai nampak!",
                new List<Vector3> { tin }, "Prop_CendolCrate",
                new List<GuardSpec> { Beat(tin, 9f, 52), Beat(tin, 12f, 54, 10f) }, 0f, "Dapat tin")
            { checkpoint = true };
            sneak1.dressing = (m, list) =>
            {
                var v = VehicleSpawner.Spawn("van", O(tin, 6f, 56, 3f) + Vector3.up * 0.6f, Quaternion.Euler(0, 30, 0), VehicleRole.MissionTarget, m.transform);
                v.noEnter = true;
                v.driver = null;
                list.Add(v.gameObject);
            };
            b.story.Add(M("Van Hitam", "tokketua", 150,
                Ls(L("Tok Ketua", "Van hitam MegaMaju tu parking tepi padang, hantar cendol ke kedai-kedai kampung."),
                   L("Tok Ketua", "Kita perlukan bukti. Curi satu tin. Senyap-senyap - pengawal dia ada dua orang."),
                   L("Pak Mat", "Senyap-senyap? Saya ni pakcik 60 kilo, Tok... okay, saya cuba.")),
                Ls(L("Pak Mat", "Tin 'CENDOL AJAIB - Minum dan Patuh'. PATUH?!"),
                   L("Mak Som", "Abang, baik kita simpan tin ni. Esok saya bawa ke pasar tanya Aunty."),
                   L("", "TAHAP 1 SELESAI... tapi misteri Cendol Ajaib baru bermula.")),
                new GoToObjective(O(pad, 40f, 57, 28f), "Pergi ke padang (jangan terus serbu!)", 0, false, 9f),
                sneak1));

            b.race = RaceMission("Lumba Kampung", "matrempit", 60,
                new List<Vector3> { J(8, 14), J(10, 14), J(10, 17), J(8, 17) }, 2, "kancil", "teksi", "kereta");
            return b;
        }

        // ------------------------------------------------------------------ Tahap 2
        static Book Level2()
        {
            var b = new Book();
            b.levelIntro = Ls(
                L("Mak Som", "Pak Mat dah minum cendol tu semalam... sekarang dia asyik senyum dan cakap 'cendol, cendol'."),
                L("Mak Som", "Ini kerja MegaMaju. Tak apa. Mak Som akan uruskan."));

            // HAGGLE: Mak Som at her best
            b.story.Add(M("Bahan Rahsia", "auntypasar", 75,
                Ls(L("Aunty Pasar", "Som! Aku boleh buat penawar untuk laki kau, tapi perlu gula melaka dengan santan."),
                   L("Aunty Pasar", "Ah Chong je yang masih ada stok, tapi MegaMaju dah naikkan harga. Kau pandai tawar kan?"),
                   L("Mak Som", "Tawar-menawar? Itu sukan negara, Aunty.")),
                Ls(L("Aunty Pasar", "Murahnya! Kau ni patut jadi menteri kewangan. Aku masak dulu, ubat ni ambil masa.")),
                new HaggleObjective("ahchong", "Tawar gula melaka dengan Ah Chong", "Gula melaka + santan", 30, 18),
                new TalkObjective("auntypasar", "Bawa bahan pada Aunty Pasar",
                    L("Aunty Pasar", "Cantik. Gula melaka asli, bukan MegaMaju punya."))));

            // CARRY CAREFULLY: the chapter's one driving job
            b.story.Add(M("Bubur Lambuk", "tokketua", 100,
                Ls(L("Tok Ketua", "Som, bubur lambuk untuk berbuka dah siap. Satu periuk besar, hantar ke Masjid Jamek."),
                   L("Tok Ketua", "Periuk tu tak bertutup. Brek kuat sikit, habis bubur atas kerusi kereta."),
                   L("Mak Som", "Kereta saya baru cuci, Tok...")),
                Ls(L("Tok Ketua", "(telefon) Alhamdulillah, sampai elok. Sekurang-kurangnya orang makan bubur, bukan cendol pelik.")),
                new GetInCarObjective("Naik kereta - periuk dah dalam kereta"),
                new FragileObjective(P("Masjid"), "Hantar bubur lambuk ke Masjid Jamek - jangan tumpah!", "Bubur lambuk", 110, Bubur)));

            // TAIL: close enough to see, far enough not to be seen
            var tail = new FollowObjective("Ikut teksi tu - jangan rapat sangat", "teksi",
                new List<Vector3> { Rd("Mamak"), J(11, 17), Rd("Towers") }, 80f) { spookDist = 14f };
            b.story.Add(M("Ikut Teksi", "along", 100,
                Ls(L("Along", "Mak, ada pakcik teksi angkut kotak-kotak cendol dari mamak. Mesti pergi markas MegaMaju!"),
                   L("Mak Som", "Kita ikut dia, Along. Jauh sikit. Pakcik teksi tengok cermin lebih dari tengok jalan.")),
                Ls(L("Mak Som", "Menara Kembar! Markas MegaMaju ada dekat situ..."),
                   L("Along", "Wah, besarnya. Macam mana nak masuk?")),
                new GetInCarObjective("Naik kereta"),
                tail));

            // DEFEND: hold Aunty's stall while the cure cooks
            var pasar = O(P("Pasar"), 6f, 61);
            b.story.Add(M("Rusuhan Cendol", "auntypasar", 125,
                Ls(L("Aunty Pasar", "Som! Penawar tengah mendidih. Tapi orang yang dah minum cendol datang nak tuang Cendol Ajaib dalam periuk aku!"),
                   L("Aunty Pasar", "Tahan mereka satu minit. Tumbuk sikit, nanti mereka sedar balik."),
                   L("Mak Som", "Tumbuk orang? ...demi Abang.")),
                Ls(L("Aunty Pasar", "Penawar siap! Dan tengok, orang-orang tu semua dah sedar. Kau memang power, Som.")),
                new DefendObjective("Pertahankan periuk penawar Aunty Pasar!", pasar, 60f, "Periuk penawar", "Prop_TehTarik")));

            // SMASH, BUT FIND IT FIRST: the stock is hidden inside the shops
            b.story.Add(M("Kilang Cendol", "inspektor", 150,
                Ls(L("Inspektor Rosli", "Puan Som. Stok Cendol Ajaib disorok dalam kedai-kedai sekitar Petaling Street."),
                   L("Inspektor Rosli", "Anggota saya semua dah minum cendol. Pecahkan semua peti tu. Saya... tak nampak apa-apa."),
                   L("Inspektor Rosli", "Tapi polis yang lain mesti datang. Lari je!")),
                Ls(L("Pak Mat", "Hmm? Mana saya ni? Kenapa mulut saya manis sangat?"),
                   L("Mak Som", "ABANG! Dah sedar! Along, cepat, kita kena beritahu semua orang!"),
                   L("", "TAHAP 2 SELESAI.")),
                new SmashObjective("Masuk kedai-kedai, pecahkan peti Cendol Ajaib", "crate", RoomSpots(P("Pasar"), 110f, 5, Shop, 41), 200),
                new EvadeObjective("Polis datang! Lari sampai hilang!") { checkpoint = true },
                // no drive home for the payoff: Mak Som rings the house (H&R would have made you drive)
                new DialogueObjective(
                    L("", "(Mak Som telefon rumah)"),
                    L("Mak Som", "Along! Penawar dalam termos biru. Bagi Ayah minum SEKARANG."),
                    L("Along", "Okay Mak! ...Ayah, minum ni. Teh tarik campur penawar."),
                    L("Pak Mat", "Cen... cendol... eh? TEH TARIK!"))));

            b.race = RaceMission("Teksi Rush", "pakciktaksi", 80,
                new List<Vector3> { Rd("Dataran"), Rd("Masjid"), Rd("Pasar"), Rd("PasarSeni") }, 2, "teksi", "teksi", "kancil");
            return b;
        }

        // ------------------------------------------------------------------ Tahap 3
        static Book Level3()
        {
            var b = new Book();
            b.levelIntro = Ls(
                L("Along", "Senja di KL. Motor rempit berbunyi, lampu jalan menyala..."),
                L("Along", "Ayah dah okay. Tapi separuh KL masih minum cendol. Masa untuk Along beraksi."));

            // RACE
            b.story.Add(M("Rempit Senja", "rival", 100,
                Ls(L("Joe Bintang", "Kau nak info pasal MegaMaju? Kalahkan aku dulu. Bukit Bintang, satu pusingan."),
                   L("Along", "Senang je.")),
                Ls(L("Joe Bintang", "Hmph. Okay, okay. Ada saintis gila dekat Menara KL, Prof. Kassim. Dia tahu semua."),
                   L("Along", "Terima kasih, 'champion'.")),
                new GetInCarObjective("Naik kereta"),
                new RaceObjective("Menang perlumbaan!", new List<Vector3> { Rd("BukitBintang"), J(14, 14), Rd("Towers"), J(18, 14) }, 1, "saga_rempit", "saga_rempit")));

            // ESCORT: walk Adik to the surau, past the free-cendol men
            b.story.Add(M("Hantar Adik Mengaji", "maksom", 75,
                Ls(L("Mak Som", "Along, hantar Adik pergi mengaji dekat surau. JALAN KAKI. Senja-senja ni promoter cendol merata-rata."),
                   L("Mak Som", "Kalau ada orang bagi Adik cendol percuma... kau tahu apa nak buat."),
                   L("Along", "Alaaa Mak... okay, okay.")),
                Ls(L("Adik", "Terima kasih Along! Along baik... kadang-kadang."),
                   L("Along", "Jangan bagitau Mak Along tumbuk pakcik promoter tu.")),
                new EscortObjective("Bawa Adik ke surau - halau promoter cendol!", "chr_adik", "Adik", P("Surau"), 4) { hideNpc = "adik" }));

            // DISGUISE: walk into the compound as one of them
            var klt = P("KLTower");
            var jam = new List<Vector3> { O(klt, 16f, 71, 6f), O(klt, 16f, 73, 6f), O(klt, 16f, 75, 6f) };
            var disguise = new SneakObjective("Menyamar, pasang 3 pengacau isyarat di Menara KL", jam, "Prop_TehTarik",
                new List<GuardSpec> { Beat(klt, 18f, 76, 12f), Beat(klt, 18f, 78, 12f), Beat(klt, 18f, 80, 12f) }, 2f, "Pasang pengacau")
            {
                disguise = new Dictionary<string, Color> { ["TshirtRed"] = new Color(0.12f, 0.12f, 0.14f), ["Shorts"] = new Color(0.12f, 0.12f, 0.14f) },
                disguiseName = "juruteknik MegaMaju",
                caughtText = "KANTOI! 'Juruteknik' pakai selipar jepun?!",
                checkpoint = true,
            };
            b.story.Add(M("Antena Menara", "profkassim", 125,
                Ls(L("Prof. Kassim", "Ahh, anak muda! Burung Kamera MegaMaju hantar isyarat ke antena di kaki Menara KL."),
                   L("Prof. Kassim", "Pakai baju juruteknik MegaMaju ni. Jalan biasa, buat muka bosan. Pasang tiga pengacau isyarat saya."),
                   L("Along", "Muka bosan? Itu kepakaran saya, Prof.")),
                Ls(L("Prof. Kassim", "Hebat! Isyarat dah bercelaru! Sekarang Burung Kamera tak tahu mana utara mana selatan.")),
                disguise));

            // HIDE: the police won't give up on the open road
            b.story.Add(M("Polis Bingung", "inspektor", 100,
                Ls(L("Inspektor Rosli", "Along! Anggota saya yang minum cendol dah diarah tangkap kau!"),
                   L("Inspektor Rosli", "Kau tak boleh lari dari mereka atas jalan. Menyorok! Masuk mana-mana kedai, duduk diam-diam.")),
                Ls(L("Along", "Fuh. Polis KL laju gila bila minum cendol. Tapi tak pandai cari dalam kedai gunting.")),
                new HideObjective("Polis cari kau! Menyorok dalam kedai atau rumah")));

            // WRECK
            b.story.Add(M("Van Pemancar", "profkassim", 150,
                Ls(L("Prof. Kassim", "Van pemancar MegaMaju sedang bergerak! Itulah yang kawal semua Burung Kamera."),
                   L("Prof. Kassim", "Rosakkan van tu!")),
                Ls(L("Prof. Kassim", "Isyarat terputus! Tapi... data terakhir tunjuk satu nama: DATUK MEGA."),
                   L("Along", "Datuk Mega? Bos MegaMaju?"),
                   L("", "TAHAP 3 SELESAI.")),
                new GetInCarObjective("Naik kereta"),
                new DestroyObjective("Rosakkan van pemancar!", "van", P("BukitBintang"), 160, 1.5f, 21f)));

            b.race = RaceMission("Lumba Jambatan", "matrempit", 100,
                new List<Vector3> { J(7, 15), J(11, 15), J(11, 18), J(7, 18) }, 1, "saga_rempit", "kancil", "van");
            return b;
        }

        // ------------------------------------------------------------------ Tahap 4
        static Book Level4()
        {
            var b = new Book();
            b.levelIntro = Ls(
                L("Adik", "Malam ni semua orang tidur... kecuali Adik."),
                L("Adik", "Datuk Mega ingat budak kecik tak boleh buat apa-apa? Tunggu!"));

            // COLLECT, BUT QUIETLY: Along is on bedtime patrol with a torch
            var home = P("Home");
            var marbles = new List<Vector3>();
            for (int i = 0, seed = 600; marbles.Count < 6 && i < 60; i++, seed += 7)
            {
                var mb = O(home, 34f, seed, 8f);
                bool clash = false;
                foreach (var q in marbles) if (Vector3.Distance(q, mb) < 5f) clash = true;
                if (!clash) marbles.Add(mb);
            }
            var patrol = new GuardSpec
            {
                route = new[] { O(home, 14f, 641, 6f), O(home, 14f, 643, 6f), O(home, 14f, 645, 6f), O(home, 14f, 647, 6f) },
                model = "chr_along", name = "Along", paint = null, range = 9f, halfAngle = 40f, speed = 1.6f, pause = 2.5f,
                chatter = new[] { "Adik... mana kau...", "Mak suruh Adik tidur!", "Hmm, bunyi apa tu?", "Kalau jumpa, siap kau..." },
                spotLine = "ADIK! Pergi tidur!",
            };
            b.story.Add(M("Guli Hilang", "maksom", 75,
                Ls(L("Mak Som", "Adik, guli kamu bersepah satu halaman! Kutip semua... ESOK. Sekarang tidur."),
                   L("Adik", "(bisik) Guli tu Adik nak guna lawan Datuk Mega malam ni..."),
                   L("Adik", "(bisik) Along tengah ronda dengan lampu suluh. Jangan sampai dia nampak!")),
                Ls(L("Adik", "Enam biji guli, satu lastik. Datuk Mega, bersedia!")),
                new SneakObjective("Kutip 6 guli tanpa Along nampak", marbles, "Prop_Coin",
                    new List<GuardSpec> { patrol }, 0f, "Kutip guli")
                { takedowns = false, caughtText = "Along nampak! 'ADIK! Masuk tidur sekarang!'", hideNpc = "along" }));

            // PHOTOGRAPH, UNSEEN: Datuk Mega bribing the police with cendol
            b.story.Add(M("Bukti Datuk", "inspektor", 125,
                Ls(L("Inspektor Rosli", "Adik, Datuk Mega ada di Dataran Merdeka malam ni, bagi cendol percuma pada anggota saya."),
                   L("Inspektor Rosli", "Saya perlukan gambar. Tapi pengawal dia ramai - jangan sampai mereka nampak kamu.")),
                Ls(L("Inspektor Rosli", "Gambar ni cukup untuk mahkamah. Datuk Mega, hari kau dah dekat.")),
                new PhotoObjective("Ambil gambar Datuk Mega - jangan sampai pengawal nampak", 1, (o, m) =>
                {
                    var c = P("Dataran");
                    var a = O(c, 14f, 401, 3f);
                    var d = Goon.Spawn("chr_datukmega", "Datuk", a, 0f, m.transform);
                    d.mode = Goon.Mode.Patrol;
                    d.points.AddRange(new[] { a, O(c, 14f, 402, 3f), O(c, 14f, 403, 3f) });
                    d.speed = 1.0f; d.pause = 4f; d.invulnerable = true;
                    d.chatter = new[] { "Minum, minum! Percuma untuk polis!", "Hahaha! KL milik MegaMaju!", "Siapa nak cendol?" };
                    o.subjects.Add(new PhotoObjective.Subject { t = d.transform, name = "Datuk Mega", height = 1.3f });
                    o.spawned.Add(d.gameObject);
                    for (int i = 0; i < 2; i++)
                    {
                        var g = Goon.Spawn("chr_townman", "Pengawal", a + new Vector3(i == 0 ? 2f : -2f, 0, 1f), 0f, m.transform, MissionLooks.Suit);
                        g.mode = Goon.Mode.Follow; g.seek = d.transform; g.stopDist = 2.2f + i; g.speed = 1.6f; g.faceAway = true;
                        g.sightRange = 13f; g.sightHalfAngle = 45f; g.invulnerable = true;
                        g.ShowCone();
                        o.guards.Add(g);
                    }
                    var cop = Goon.Spawn("chr_polis", "Polis", O(c, 6f, 404), 90f, m.transform);
                    cop.chatter = new[] { "Cendol... terima kasih Datuk...", "Patuh... patuh..." };
                    cop.invulnerable = true;
                    o.spawned.Add(cop.gameObject);
                    o.minRange = 8f;
                    o.caughtText = "Pengawal Datuk nampak Adik!";
                })));

            // BRAWL: kick sense into the mamak's customers
            b.story.Add(M("Penawar Untuk Semua", "anneh", 125,
                Ls(L("Anneh", "Adik! Semua pelanggan aku dah jadi zombi cendol! Mereka tak nak teh tarik lagi!"),
                   L("Anneh", "Aunty Pasar kata satu tendangan penawar cukup untuk sedarkan mereka. Hati-hati, mereka suka kerumun.")),
                Ls(L("Anneh", "Syabas! Semua dah sedar, dan semua order teh tarik! Adik, kau hero kedai aku.")),
                new BrawlObjective("Tendang zombi cendol sampai sedar", P("Mamak"), 8)));

            // RAM: stop the limo before it reaches the towers
            b.story.Add(M("Limo Datuk", "inspektor", 150,
                Ls(L("Inspektor Rosli", "Anggota saya dah sedar! Tapi Datuk Mega cuba lari dengan limo dia ke Menara Kembar."),
                   L("Inspektor Rosli", "Limo tu berperisai - takkan rosak. Tapi kalau kamu rempuh cukup kuat, dia akan tersadai!")),
                Ls(L("Datuk Mega", "(dari jauh) Grrr! Budak kampung! Jumpa aku di Menara Kembar kalau berani!")),
                new GetInCarObjective("Naik kereta"),
                new RamObjective("Rempuh limo Datuk Mega 5 kali!", "limo",
                    new List<Vector3> { Rd("Dataran"), J(11, 17), Rd("Towers") }, 5, "Menara Kembar")));

            // BOSS
            b.story.Add(M("Pertempuran Menara Kembar", "datuk", 300,
                Ls(L("Datuk Mega", "Hahaha! Seorang budak kecil? Itu saja yang Kampung Baru boleh hantar?"),
                   L("Datuk Mega", "Bila semua orang KL minum Cendol Ajaib, aku akan roboh semua kampung dan bina 100 menara lagi!"),
                   L("Datuk Mega", "MEGA-MYNAH! Patuk budak ni!"),
                   L("Adik", "Kampung Adik tak boleh roboh! Mak cakap!")),
                Ls(L("Datuk Mega", "Tidak... burung aku... menara aku... CENDOL AKU!"),
                   L("Inspektor Rosli", "Datuk Mega, anda ditahan kerana mencemar cendol negara."),
                   L("Adik", "Yeay! Esok Adik nak aiskrim tiga!")),
                new BossObjective("Kalahkan Mega-Mynah!", O(P("Towers"), 22f, 811, 6f)),
                new TalkObjective("datuk", "Berdepan dengan Datuk Mega",
                    L("Adik", "Datuk! Sudah! Semua orang dah sedar!"),
                    L("Datuk Mega", "Mustahil... dikalahkan oleh... budak tadika..."))));

            b.race = RaceMission("Lumba Malam KL", "rival", 150,
                new List<Vector3> { Rd("Towers"), Rd("KLTower"), Rd("Merdeka118"), Rd("BukitBintang") }, 1, "polis", "limo", "saga_rempit");
            return b;
        }

        // ------------------------------------------------------------------ Tahap 5
        static Book Level5()
        {
            var b = new Book();
            b.levelIntro = Ls(
                L("Aiman", "Chow Kit lepas hujan. Jalan basah, pasar sibuk, order masuk tak henti-henti."),
                L("Aiman", "MegaMaju dah jatuh... tapi ada app baru, 'MegaMaju Ekspres', rampas semua pelanggan pasar. Pelik."),
                L("", "Motor penghantaran Aiman ada kat rumah. Tekan E dekat motor untuk naik."));

            var ck = P("ChowKit");

            // CARRY CAREFULLY: soup on a kapcai
            b.story.Add(M("Order Pertama", "mei", 60,
                Ls(L("Mei", "Aiman! Mee hailam berkuah untuk Masjid Jamek. Pelanggan tetap, jangan lambat!"),
                   L("Aiman", "Kak Mei, saya laju macam kilat."),
                   L("Mei", "Laju boleh. Tumpah jangan. Pelanggan bagi bintang satu kalau kuah tinggal separuh.")),
                Ls(L("Mei", "Lima bintang! Eh, kenapa pelanggan cakap app lain hantar 'cendol percuma' tadi?"),
                   L("Aiman", "Cendol? Alamak... bukan lagi.")),
                new GetInCarObjective("Naik motor penghantaran"),
                new FragileObjective(P("Masjid"), "Hantar mee hailam ke Masjid Jamek - kuah jangan tumpah!", "Kuah mee hailam", 85, Kuah)));

            // CHASE ON FOOT: a snatch thief through the five-foot ways
            b.story.Add(M("Ragut!", "ravi", 90,
                Ls(L("Ravi", "AIMAN! Mat ragut tu rampas buku akaun aku! Semua kiraan aku dalam tu!"),
                   L("Ravi", "Dia lari ikut kaki lima. Motor tak muat - KEJAR!")),
                Ls(L("Ravi", "Buku aku! Eh, tengok muka surat terakhir: 'Bayaran dari MegaMaju Ekspres - untuk curi data pasar'."),
                   L("Aiman", "Jadi mat ragut ni kerja dengan MegaMaju?")),
                new FootChaseObjective("Kejar mat ragut - tumbuk dia!", O(ck, 14f, 901, 6f), "Mat Ragut"),
                new TalkObjective("ravi", "Pulangkan buku akaun pada Ravi",
                    L("Ravi", "Terima kasih. Nah, kopi O. Aku belanja. Dua."))));

            // TAIL
            var tail = new FollowObjective("Ikut van Ekspres - jangan rapat sangat", "van",
                new List<Vector3> { Rd("ChowKit"), J(7, 14), Rd("Masjid"), Rd("PasarSeni") }, 70f, Black) { spookDist = 12f };
            b.story.Add(M("Ikut Van Ekspres", "mei", 100,
                Ls(L("Mei", "Tu! Van hitam 'Ekspres' ambil stok cendol dari lorong belakang. Ikut dia, tengok gudang dia."),
                   L("Aiman", "Motor saya kecil, dia takkan perasan... kalau saya tak rapat sangat.")),
                Ls(L("Aiman", "Gudang dekat Pasar Seni. Penuh peti 'CENDOL AJAIB 2.0'. Datuk Mega ada peminat rupanya.")),
                new GetInCarObjective("Naik motor"),
                tail));

            // SEARCH: the spy in the shops
            b.story.Add(M("Pengintip Chow Kit", "ravi", 100,
                Ls(L("Ravi", "Burung Kamera tu hantar gambar ke satu alat penerima. Ada dalam salah satu kedai Chow Kit."),
                   L("Ravi", "Masuk, geledah. Tauke-tauke tu kawan aku. Kebanyakannya.")),
                Ls(L("Ravi", "Penerima! Pecahkan. Sekarang gerai boleh jual harga sebenar tanpa kena intip.")),
                new SearchObjective("Geledah kedai-kedai Chow Kit, cari alat penerima", ck, 130f, 5, Shop, "alat penerima MegaMaju", "Prop_BurungKamera", 97,
                    new[] { "Bukan sini. Tauke runcit tawar Milo ais.", "Kosong. Cuma pakcik tidur atas kerusi gunting.", "Tak ada. Bau kopi je.", "Tiada apa-apa. Kalendar 2009." })));

            // SNEAK: sabotage the warehouse
            var ps = P("PasarSeni");
            var gudang = new List<Vector3> { O(ps, 14f, 951, 4f), O(ps, 14f, 953, 4f), O(ps, 14f, 955, 4f) };
            var sneak5 = new SneakObjective("Tuang penawar dalam 3 peti di gudang - jangan kantoi", gudang, "Prop_CendolCrate",
                new List<GuardSpec> { Beat(ps, 16f, 956, 12f), Beat(ps, 16f, 958, 12f), Beat(ps, 16f, 960, 12f) }, 1.5f, "Tuang penawar")
            { checkpoint = true };
            b.story.Add(M("Gudang Ekspres", "mei", 200,
                Ls(L("Mei", "Malam ni kita tutup gudang tu. Aunty Pasar bagi tiga botol penawar. Tuang dalam peti cendol mereka."),
                   L("Mei", "Lepas tu semua yang minum Cendol Ajaib 2.0 akan sedar sendiri. Pengawal ramai - senyap-senyap."),
                   L("Aiman", "Lepas ni saya hantar order percuma satu minggu. Untuk Chow Kit.")),
                Ls(L("Mei", "Pasar kita selamat! Order hari ni, semua dapat mee hailam lebih."),
                   L("Ravi", "Dan aku dah kira: untung naik 300 peratus. Kira-kira."),
                   L("", "TAHAP 5 SELESAI.")),
                new GoToObjective(O(ps, 45f, 961, 30f), "Pergi dekat gudang Pasar Seni", 0, false, 9f),
                sneak5,
                new EvadeObjective("Penggera berbunyi! Lari dari polis!") { checkpoint = true },
                new DialogueObjective(
                    L("", "(Telefon Aiman berbunyi)"),
                    L("Mei", "Aiman! Semua pelanggan yang minum Cendol 2.0 tiba-tiba order teh tarik! Kau berjaya!"),
                    L("Aiman", "Kak Mei, order seterusnya saya hantar percuma."),
                    L("Mei", "Kau memang penghantar paling laju di KL."))));

            b.race = RaceMission("Lumba Penghantar", "matrempit", 120,
                new List<Vector3> { J(1, 16), J(4, 16), J(4, 18), J(1, 18) }, 2, "bike", "kancil", "teksi");
            return b;
        }

        static Mission RaceMission(string title, string giver, int reward, List<Vector3> cps, int laps, params string[] rivals)
        {
            // the start line: on the street the course comes in by, 30 m before the first checkpoint
            var lead = _city.roads.Route(new List<Vector3> { cps[cps.Count - 1], cps[0] });
            var start = lead[0];
            float back = 0f;
            for (int i = lead.Count - 1; i > 0; i--)
            {
                back += Vector3.Distance(lead[i], lead[i - 1]);
                if (back >= 30f) { start = Vector3.Lerp(lead[i - 1], lead[i], (back - 30f) / Mathf.Max(0.01f, Vector3.Distance(lead[i], lead[i - 1]))); break; }
            }
            var m = M(title, giver, reward,
                Ls(L("", $"{title}: {cps.Count} checkpoint, {laps} pusingan. Menang tempat pertama!")),
                Ls(L("", "Juara jalanan KL!")),
                new GetInCarObjective("Naik kereta untuk berlumba"),
                new GoToObjective(start, "Pergi ke garisan mula", 0, true, 10f),
                new RaceObjective("Menang perlumbaan!", cps, laps, rivals));
            m.isRace = true;
            return m;
        }

        public static readonly Line[] Ending =
        {
            new Line("", "Dan begitulah, Kuala Lumpur kembali minum teh tarik."),
            new Line("Pak Mat", "Kurang manis, Anneh!"),
            new Line("Mak Som", "Abang, jangan lupa bayar hutang saman RM50 tu."),
            new Line("Along", "Joe Bintang nak lumba lagi minggu depan."),
            new Line("Adik", "Dan Adik dapat aiskrim tiga!"),
            new Line("Aiman", "Dan Chow Kit? Order masuk macam biasa. Penghantaran percuma minggu ni!"),
            new Line("", "KAMPUNG RUN: KL - Tamat. Teruskan meneroka: kutip semua Kad Lat dan hapuskan semua Burung Kamera!"),
        };
    }
}
