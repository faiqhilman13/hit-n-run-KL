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
            if (level == 4) Npc("datuk", "Datuk Mega", "chr_datukmega", Walk(P("Towers"), 5f), 180, "Hmph.");
        }

        // ------------------------------------------------------------------ Tahap 1
        static Book Level1()
        {
            var b = new Book();
            b.levelIntro = Ls(
                L("Pak Mat", "Aaah, pagi yang indah di Kampung Baru. Kereta dah basuh, ayam dah bagi makan..."),
                L("Mak Som", "ABANG! Mari sini kejap!"),
                L("", "Cakap dengan orang yang ada tanda ! di atas kepala untuk mula misi. Tekan E."));

            b.story.Add(M("Teh Tarik Kurang Manis", "maksom", 50,
                Ls(L("Mak Som", "Abang, teh tarik dekat rumah dah habis. Pergi beli dekat mamak Anneh, kurang manis ya!"),
                   L("Pak Mat", "Alamak, jauh tu, seberang sungai..."),
                   L("Mak Som", "Pergi CEPAT sebelum saya panas!")),
                Ls(L("Mak Som", "Hmm... sedap. Tapi Anneh cakap apa tadi? Ada orang MegaMaju datang kedai dia?")),
                new GetInCarObjective("Naik kereta Saga"),
                new GoToObjective(P("Mamak"), "Pandu ke Restoran Mamak Anneh", 90, true),
                new TalkObjective("anneh", "Cakap dengan Anneh",
                    L("Anneh", "Pak Mat! Teh tarik kurang manis, bungkus! Siap!"),
                    L("Anneh", "Eh boss, tadi ada orang pakai kot hitam, MegaMaju punya, nak saya jual 'Cendol Ajaib' dia."),
                    L("Anneh", "Saya cakap tak mau! Teh tarik saya no.1 la!"),
                    L("Pak Mat", "Cendol Ajaib? Pelik bunyinya.")),
                new GoToObjective(P("HomeYard"), "Balik rumah sebelum teh sejuk!", 75, true)));

            b.story.Add(M("Nasi Lemak Kenduri", "along", 75,
                Ls(L("Along", "Ayah! Kenduri Tok Ketua petang ni. Mak suruh kutip bungkus nasi lemak yang orang tinggal kat rumah-rumah."),
                   L("Pak Mat", "Kenapa tak kamu buat?"),
                   L("Along", "Saya... sibuk. Main game.")),
                Ls(L("Pak Mat", "Lapan bungkus! Cukup untuk satu kampung. Along jangan curi satu pun!")),
                new CollectObjective("Kutip bungkus nasi lemak", "Prop_Bungkus", Near(P("Home"), 150f, 8, 11), 170),
                new GoToObjective(P("HomeYard"), "Bawa balik ke rumah", 0)));

            b.story.Add(M("Bas Mini Samseng", "tokketua", 100,
                Ls(L("Tok Ketua", "Pak Mat, ada satu bas mini pink main langgar orang dekat jalan besar. Pemandunya minum cendol pelik tu!"),
                   L("Tok Ketua", "Hentikan dia sebelum ada yang cedera. Langgar sampai rosak!")),
                Ls(L("Pemandu Bas", "Eh... apa jadi? Kenapa saya kat sini? Saya minum cendol je tadi..."),
                   L("Tok Ketua", "Cendol lagi! Ini bukan kebetulan.")),
                new GetInCarObjective("Cari kereta"),
                new DestroyObjective("Rosakkan Bas Mini Samseng!", "basmini", P("Dataran"), 160, 0.8f, 16f)));

            b.story.Add(M("Burung Pelik", "adik", 100,
                Ls(L("Adik", "Ayah! Ada burung robot! Mata dia kamera! Dia tengok Adik mandi!"),
                   L("Pak Mat", "Burung... robot? Adik tengok kartun banyak sangat ni."),
                   L("Adik", "BETUL! Pergi tumbuk dia Ayah!")),
                Ls(L("Pak Mat", "Ya Allah, betul la robot. Ada tulisan: 'HAK MILIK MEGAMAJU BERHAD'."),
                   L("Adik", "Kan Adik dah cakap!")),
                new SmashObjective("Tumbuk / tendang Burung Kamera", "bird", Near(P("Home"), 120f, 4, 21, 2f), 200),
                new TalkObjective("adik", "Beritahu Adik",
                    L("Adik", "Ayah dah hapuskan semua? Yeay! Ayah hero!"))));

            b.story.Add(M("Van Hitam", "tokketua", 150,
                Ls(L("Tok Ketua", "Ada van hitam datang malam-malam hantar cendol ke kedai-kedai. Ikut dia, tengok dia pergi mana."),
                   L("Tok Ketua", "Jangan dekat sangat, jangan jauh sangat!")),
                Ls(L("Pak Mat", "Tin 'CENDOL AJAIB - Minum dan Patuh'. PATUH?!"),
                   L("Mak Som", "Abang, baik kita simpan tin ni. Esok saya bawa ke pasar tanya Aunty."),
                   L("", "TAHAP 1 SELESAI... tapi misteri Cendol Ajaib baru bermula.")),
                new GetInCarObjective("Naik kereta"),
                new FollowObjective("Ikut van hitam, jangan sampai hilang!", "van",
                    new List<Vector3> { J(9, 16), Rd("Masjid"), Rd("Dataran"), Rd("PasarSeni") }, 80f),
                new DestroyObjective("Van tu nampak kita! Rosakkan van tu!", "van", Rd("PasarSeni"), 100, 1.1f, 17f),
                new CollectObjective("Ambil tin cendol yang tercicir", "Prop_CendolCrate", new List<Vector3> { P("PasarSeni") + Vector3.up * 0.3f }),
                new GoToObjective(P("HomeYard"), "Bawa balik ke rumah", 0)));

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

            b.story.Add(M("Bahan Rahsia", "auntypasar", 75,
                Ls(L("Aunty Pasar", "Som! Aku boleh buat penawar untuk laki kau, tapi perlu bahan: santan, gula melaka, daun pandan..."),
                   L("Aunty Pasar", "Kedai-kedai dah kena beli dengan MegaMaju. Kau pergi kutip dari tempat-tempat yang masih ada.")),
                Ls(L("Aunty Pasar", "Bagus! Aku masak dulu. Ubat ni ambil masa.")),
                new CollectObjective("Kutip bahan penawar", "Prop_Bungkus", Near(P("Pasar"), 170f, 6, 31), 170),
                new GoToObjective(P("Pasar"), "Bawa bahan ke Aunty Pasar", 0)));

            b.story.Add(M("Kereta Mega", "inspektor", 100,
                Ls(L("Inspektor Rosli", "Puan Som. Ada kereta hitam MegaMaju yang edar cendol tanpa lesen."),
                   L("Inspektor Rosli", "Anggota saya... semua dah minum cendol. Saya perlukan orang awam yang berani. Rosakkan kereta tu.")),
                Ls(L("Inspektor Rosli", "Terima kasih, puan. Saya akan... buat tak nampak apa yang puan buat tadi.")),
                new GetInCarObjective("Naik kereta"),
                new DestroyObjective("Rosakkan kereta MegaMaju", "limo", P("Dataran"), 150, 1f, 19f)));

            b.story.Add(M("Ikut Teksi", "along", 100,
                Ls(L("Along", "Mak, ada pakcik teksi angkut kotak-kotak cendol dari mamak. Mesti pergi markas MegaMaju!"),
                   L("Mak Som", "Kita ikut dia, Along. Kau duduk diam-diam kat belakang.")),
                Ls(L("Mak Som", "Menara Kembar! Markas MegaMaju ada dekat situ..."),
                   L("Along", "Wah, besarnya. Macam mana nak masuk?")),
                new GetInCarObjective("Naik kereta"),
                new FollowObjective("Ikut teksi tu", "teksi",
                    new List<Vector3> { Rd("Mamak"), J(11, 17), Rd("Towers") }, 80f),
                new GoToObjective(P("Towers"), "Intai Menara Kembar", 0)));

            b.story.Add(M("Bubur Lambuk", "tokketua", 100,
                Ls(L("Tok Ketua", "Som, bubur lambuk untuk berbuka dah siap. Hantar ke Masjid Jamek dulu, lepas tu ke surau."),
                   L("Tok Ketua", "Cepat sikit, nanti sejuk!")),
                Ls(L("Tok Ketua", "Alhamdulillah. Sekurang-kurangnya orang makan bubur, bukan cendol pelik.")),
                new GetInCarObjective("Naik kereta"),
                new GoToObjective(P("Masjid"), "Hantar bubur ke Masjid Jamek", 70, true),
                new GoToObjective(P("Surau"), "Hantar ke Surau", 75, true)));

            b.story.Add(M("Kilang Cendol", "auntypasar", 150,
                Ls(L("Aunty Pasar", "Penawar dah siap! Tapi MegaMaju simpan stok cendol dekat Petaling Street dan Pasar Seni."),
                   L("Aunty Pasar", "Pecahkan semua peti cendol tu! Polis mesti datang... lari je!")),
                Ls(L("Pak Mat", "Hmm? Mana saya ni? Kenapa mulut saya manis sangat?"),
                   L("Mak Som", "ABANG! Dah sedar! Along, cepat, kita kena beritahu semua orang!"),
                   L("", "TAHAP 2 SELESAI.")),
                new SmashObjective("Pecahkan peti Cendol Ajaib", "crate", Near(P("Pasar"), 90f, 6, 41, 0.2f), 150),
                new EvadeObjective("Polis datang! Lari sampai hilang!"),
                new TalkObjective("pakmat", "Bagi penawar pada Pak Mat",
                    L("Mak Som", "Abang, minum ni. Teh tarik campur penawar."),
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

            b.story.Add(M("Rempit Senja", "rival", 100,
                Ls(L("Joe Bintang", "Kau nak info pasal MegaMaju? Kalahkan aku dulu. Bukit Bintang, satu pusingan."),
                   L("Along", "Senang je.")),
                Ls(L("Joe Bintang", "Hmph. Okay, okay. Ada saintis gila dekat Menara KL, Prof. Kassim. Dia tahu semua."),
                   L("Along", "Terima kasih, 'champion'.")),
                new GetInCarObjective("Naik kereta"),
                new RaceObjective("Menang perlumbaan!", new List<Vector3> { Rd("BukitBintang"), J(14, 14), Rd("Towers"), J(18, 14) }, 1, "saga_rempit", "saga_rempit")));

            b.story.Add(M("Hantar Adik Mengaji", "maksom", 75,
                Ls(L("Mak Som", "Along, hantar Adik pergi mengaji dekat surau. Lepas tu jemput dia di padang."),
                   L("Along", "Alaaa Mak...")),
                Ls(L("Adik", "Terima kasih Along! Along baik... kadang-kadang.")),
                new GetInCarObjective("Naik kereta"),
                new GoToObjective(P("Surau"), "Hantar Adik ke surau", 60, true),
                new GoToObjective(P("Padang"), "Jemput Adik di padang", 60, true)));

            b.story.Add(M("Antena Menara", "profkassim", 100,
                Ls(L("Prof. Kassim", "Ahh, anak muda! Burung Kamera MegaMaju hantar isyarat ke Menara KL."),
                   L("Prof. Kassim", "Kutip komponen pemancar yang terjatuh di sekitar bukit ni. Saya akan terbalikkan isyaratnya!")),
                Ls(L("Prof. Kassim", "Hebat! Dengan ini, kita boleh ganggu isyarat MegaMaju!")),
                new CollectObjective("Kutip komponen pemancar", "Prop_TehTarik", Near(P("KLTower"), 70f, 5, 51), 130),
                new TalkObjective("profkassim", "Bawa pada Prof. Kassim",
                    L("Prof. Kassim", "Sekarang, kita perlukan van pemancar mereka... tapi itu nanti."))));

            b.story.Add(M("Polis Bingung", "inspektor", 100,
                Ls(L("Inspektor Rosli", "Along! Anggota saya yang minum cendol dah diarah tangkap kau!"),
                   L("Inspektor Rosli", "Lari! Hilangkan diri, lepas tu jumpa Prof. Kassim.")),
                Ls(L("Along", "Fuh. Polis KL laju gila bila minum cendol.")),
                new EvadeObjective("Polis kena cuci otak! Lari!"),
                new GoToObjective(P("KLTower"), "Pergi ke Menara KL", 0)));

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

            b.story.Add(M("Guli Hilang", "maksom", 75,
                Ls(L("Mak Som", "Adik, guli kamu bersepah satu kampung! Kutip semua sebelum tidur."),
                   L("Adik", "Okay Mak! (lepas ni Adik nak pergi Menara Kembar, shhh)")),
                Ls(L("Mak Som", "Pandai anak Mak. Sekarang TIDUR.")),
                new CollectObjective("Kutip guli", "Prop_Coin", Near(P("Home"), 110f, 10, 61, 0.6f), 140)));

            b.story.Add(M("Burung Kamera Malam", "along", 100,
                Ls(L("Along", "Adik, Burung Kamera baru keluar malam ni. Kalau kita hapuskan, Datuk Mega buta!"),
                   L("Adik", "Adik tendang semua!")),
                Ls(L("Along", "Wah, Adik ni ganas jugak.")),
                new SmashObjective("Hapuskan Burung Kamera", "bird", Near(P("Masjid"), 170f, 6, 71, 2f), 220)));

            b.story.Add(M("Penawar Untuk Semua", "anneh", 100,
                Ls(L("Anneh", "Adik! Aunty Pasar hantar penawar dalam teh tarik. Tolong hantar ke masjid dan ke pasar!"),
                   L("Adik", "Adik boleh bawa kereta? (jangan beritahu Mak)")),
                Ls(L("Anneh", "Syabas! Setengah KL dah sedar!")),
                new GetInCarObjective("Naik kereta Kancil"),
                new GoToObjective(P("Masjid"), "Hantar penawar ke Masjid Jamek", 70, true),
                new GoToObjective(P("Pasar"), "Hantar penawar ke Petaling Street", 70, true),
                new GoToObjective(P("Dataran"), "Hantar penawar ke Dataran Merdeka", 70, true)));

            b.story.Add(M("Limo Datuk", "inspektor", 150,
                Ls(L("Inspektor Rosli", "Anggota saya dah sedar! Tapi Datuk Mega cuba lari dengan limo dia."),
                   L("Inspektor Rosli", "Adik... kamu budak paling berani di KL. Hentikan limo tu!")),
                Ls(L("Datuk Mega", "(dari jauh) Grrr! Budak kampung! Jumpa aku di Menara Kembar kalau berani!")),
                new GetInCarObjective("Naik kereta"),
                new DestroyObjective("Rosakkan limo Datuk Mega!", "limo", P("Towers"), 170, 1.6f, 22f)));

            b.story.Add(M("Pertempuran Menara Kembar", "datuk", 300,
                Ls(L("Datuk Mega", "Hahaha! Seorang budak kecil? Itu saja yang Kampung Baru boleh hantar?"),
                   L("Datuk Mega", "Bila semua orang KL minum Cendol Ajaib, aku akan roboh semua kampung dan bina 100 menara lagi!"),
                   L("Adik", "Kampung Adik tak boleh roboh! Mak cakap!")),
                Ls(L("Datuk Mega", "Tidak... bas cendol aku... menara aku... BURUNG AKU!"),
                   L("Inspektor Rosli", "Datuk Mega, anda ditahan kerana mencemar cendol negara."),
                   L("Adik", "Yeay! Esok Adik nak aiskrim tiga!")),
                new GetInCarObjective("Naik kereta"),
                new DestroyObjective("Musnahkan Bas Cendol MegaMaju!", "basmini", P("Towers"), 220, 2.4f, 19f, Black),
                new SmashObjective("Hapuskan Burung Kamera pengawal", "bird", Near(P("Towers"), 45f, 3, 81, 2.2f), 120),
                new GoToObjective(P("Towers"), "Serbu Menara Kembar!", 0),
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

            b.story.Add(M("Order Pertama", "mei", 60,
                Ls(L("Mei", "Aiman! Tiga bungkus mee hailam untuk Masjid Jamek. Pelanggan tetap, jangan lambat!"),
                   L("Aiman", "Kak Mei, saya laju macam kilat."),
                   L("Mei", "Laju boleh. Tumpah jangan.")),
                Ls(L("Mei", "Cepatnya! Eh, kenapa pelanggan cakap app lain hantar 'cendol percuma' tadi?"),
                   L("Aiman", "Cendol? Alamak... bukan lagi.")),
                new GetInCarObjective("Naik motor penghantaran"),
                new GoToObjective(P("Masjid"), "Hantar ke Masjid Jamek", 75, true),
                new GoToObjective(ck, "Balik ke Pasar Chow Kit", 70, true)));

            b.story.Add(M("Kanvas Terbang", "ravi", 75,
                Ls(L("Ravi", "Aiman, angin tadi terbangkan barang stok aku satu pasar. Buku akaun pun!"),
                   L("Ravi", "Kutip semua. Aku dah kira, ada enam. Jangan tipu, pen aku ada kat sini.")),
                Ls(L("Ravi", "Enam, betul. Tapi tengok resit ni... semua pembekal kena beli dengan MegaMaju Ekspres.")),
                new CollectObjective("Kutip barang stok Ravi", "Prop_Bungkus", Near(ck, 110f, 6, 91), 150),
                new TalkObjective("ravi", "Pulangkan pada Ravi",
                    L("Ravi", "Terima kasih. Nah, kopi O. Aku belanja."))));

            b.story.Add(M("Ikut Van Ekspres", "mei", 100,
                Ls(L("Mei", "Tu! Van hitam 'Ekspres' ambil stok cendol dari lorong belakang. Ikut dia, tengok gudang dia."),
                   L("Aiman", "Motor saya kecil, dia takkan perasan.")),
                Ls(L("Aiman", "Gudang dekat Pasar Seni. Penuh peti 'CENDOL AJAIB 2.0'. Datuk Mega ada peminat rupanya.")),
                new GetInCarObjective("Naik motor"),
                new FollowObjective("Ikut van Ekspres, jangan terlepas", "van",
                    new List<Vector3> { Rd("ChowKit"), J(7, 14), Rd("Masjid"), Rd("PasarSeni") }, 70f),
                new GoToObjective(P("PasarSeni"), "Intai gudang Pasar Seni", 0)));

            b.story.Add(M("Burung Kamera Pasar", "ravi", 100,
                Ls(L("Ravi", "Burung kamera tu pantau semua gerai. Siapa jual murah, esok kena saman."),
                   L("Ravi", "Tumbuk semua. Aku akan... pegang pen ni dengan tegas.")),
                Ls(L("Ravi", "Lima-lima dah jatuh. Sekarang gerai boleh jual harga sebenar.")),
                new SmashObjective("Hapuskan Burung Kamera di Chow Kit", "bird", Near(ck, 90f, 5, 101, 2f), 200)));

            b.story.Add(M("Gudang Ekspres", "mei", 200,
                Ls(L("Mei", "Malam ni kita tutup gudang tu. Pecahkan peti cendol, rosakkan van Ekspres!"),
                   L("Aiman", "Lepas ni saya hantar order percuma satu minggu. Untuk Chow Kit.")),
                Ls(L("Mei", "Pasar kita selamat! Order hari ni, semua dapat mee hailam lebih."),
                   L("Ravi", "Dan aku dah kira: untung naik 300 peratus. Kira-kira."),
                   L("", "TAHAP 5 SELESAI.")),
                new SmashObjective("Pecahkan peti Cendol Ajaib 2.0", "crate", Near(P("PasarSeni"), 70f, 5, 111, 0.2f), 150),
                new GetInCarObjective("Naik motor"),
                new DestroyObjective("Rosakkan van Ekspres!", "van", P("PasarSeni"), 150, 1.2f, 18f, Black),
                new EvadeObjective("Polis datang! Hilangkan diri!"),
                new TalkObjective("mei", "Balik jumpa Kak Mei",
                    L("Mei", "Aiman, kau memang penghantar paling laju di KL."))));

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
