using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    public class CharacterDef
    {
        public string id, name, model;
        public float walk = 4.2f, run = 7.5f, jump = 6f;
        public Dictionary<string, Color> palette = new Dictionary<string, Color>();
    }

    public class CostumeDef
    {
        public string id, character, name;
        public int price;
        public Dictionary<string, Color> swaps;
    }

    public class CarDef
    {
        public string id, name, model;
        public int price;         // 0 = family car / not for sale
        public int level;         // level in which it's sold
        public float mass = 1100, maxSpeed = 30, accel = 14, turn = 115, grip = 9, health = 100;
        public Dictionary<string, Color> swaps;
    }

    public class LevelDef
    {
        public int number;
        public string title, subtitle, player, familyCar;
        public Vector3 sunEuler;
        public Color sun, paper, fog, shadowTint;
        public Color zenith = new Color(0.32f, 0.62f, 0.95f), cloud = Color.white, cloudShade = new Color(0.8f, 0.86f, 0.96f);
        public float cloudCover = 0.45f, stars;
        public float fogStart = 150, fogEnd = 430;
    }

    /// <summary>All static content: family, costumes, cars, the four levels.</summary>
    public static class GameData
    {
        public const int MissionsPerLevel = 5;
        public const int LastLevel = 5;
        public const int CardsPerLevel = 7;
        public const int CamerasPerLevel = 5;

        static Color C(float r, float g, float b) => new Color(r, g, b);

        public static readonly Dictionary<string, CharacterDef> Characters = new Dictionary<string, CharacterDef>
        {
            ["pakmat"] = new CharacterDef { id = "pakmat", name = "Pak Mat", model = "chr_pakmat", walk = 3.8f, run = 6.8f, jump = 5.6f },
            ["maksom"] = new CharacterDef { id = "maksom", name = "Mak Som", model = "chr_maksom", walk = 4f, run = 7f, jump = 5.8f },
            ["along"] = new CharacterDef { id = "along", name = "Along", model = "chr_along", walk = 4.6f, run = 8.2f, jump = 6.6f },
            ["adik"] = new CharacterDef { id = "adik", name = "Adik", model = "chr_adik", walk = 4.4f, run = 7.8f, jump = 6.8f },
            ["aiman"] = new CharacterDef { id = "aiman", name = "Aiman", model = "chr_aiman", walk = 4.4f, run = 8f, jump = 6.4f },
        };

        public static readonly List<CostumeDef> Costumes = new List<CostumeDef>
        {
            new CostumeDef { id = "pakmat_raya", character = "pakmat", name = "Baju Melayu Raya", price = 120,
                swaps = new Dictionary<string, Color> { ["Batik"] = C(0.35f, 0.62f, 0.45f), ["Sarong"] = C(0.85f, 0.72f, 0.35f) } },
            new CostumeDef { id = "pakmat_bola", character = "pakmat", name = "Jersi Harimau Malaya", price = 150,
                swaps = new Dictionary<string, Color> { ["Batik"] = C(0.95f, 0.8f, 0.2f), ["Sarong"] = C(0.15f, 0.15f, 0.15f) } },
            new CostumeDef { id = "maksom_kebaya", character = "maksom", name = "Kebaya Merah", price = 120,
                swaps = new Dictionary<string, Color> { ["Kurung"] = C(0.78f, 0.22f, 0.25f), ["Tudung"] = C(0.95f, 0.9f, 0.8f) } },
            new CostumeDef { id = "maksom_gym", character = "maksom", name = "Aunty Senamrobik", price = 150,
                swaps = new Dictionary<string, Color> { ["Kurung"] = C(0.45f, 0.35f, 0.75f), ["Tudung"] = C(0.95f, 0.5f, 0.7f) } },
            new CostumeDef { id = "along_sekolah", character = "along", name = "Baju Sekolah", price = 100,
                swaps = new Dictionary<string, Color> { ["TshirtRed"] = C(0.97f, 0.97f, 0.95f), ["Shorts"] = C(0.2f, 0.45f, 0.3f) } },
            new CostumeDef { id = "along_rempit", character = "along", name = "Gaya Rempit", price = 150,
                swaps = new Dictionary<string, Color> { ["TshirtRed"] = C(0.2f, 0.2f, 0.22f), ["Shorts"] = C(0.3f, 0.5f, 0.85f) } },
            new CostumeDef { id = "adik_tadika", character = "adik", name = "Uniform Tadika", price = 100,
                swaps = new Dictionary<string, Color> { ["Tshirt"] = C(0.95f, 0.6f, 0.7f), ["Shorts"] = C(0.3f, 0.3f, 0.6f) } },
            new CostumeDef { id = "adik_superhero", character = "adik", name = "Adik Super", price = 150,
                swaps = new Dictionary<string, Color> { ["Tshirt"] = C(0.25f, 0.45f, 0.85f), ["Shorts"] = C(0.85f, 0.2f, 0.2f) } },
        };

        static Dictionary<string, Color> Paint(float r, float g, float b) => new Dictionary<string, Color> { ["car_paint"] = C(r, g, b) };

        // Hit & Run-style Malaysian cars (Tools/blender/kl/kl_cars.py); "car_paint" is each car's colour
        public static readonly List<CarDef> Cars = new List<CarDef>
        {
            new CarDef { id = "saga", name = "Saga Ayah", model = "veh_saga", price = 0, level = 1, swaps = Paint(0.18f, 0.38f, 0.78f) },
            new CarDef { id = "kancil", name = "Kancil Kuning", model = "veh_kancil", price = 90, level = 1, mass = 800, maxSpeed = 27, accel = 15, turn = 135, health = 80,
                swaps = Paint(0.98f, 0.8f, 0.12f) },
            new CarDef { id = "kapcai", name = "Kapcai", model = "veh_kapcai", price = 60, level = 1, mass = 150, maxSpeed = 26, accel = 14, turn = 155, grip = 12, health = 70 },
            new CarDef { id = "teksi", name = "Teksi", model = "veh_teksi", price = 150, level = 1, mass = 1050, maxSpeed = 31, health = 100,
                swaps = new Dictionary<string, Color> { ["car_paint"] = C(0.86f, 0.14f, 0.14f) } },
            new CarDef { id = "myvi", name = "Myvi Merah", model = "veh_myvi", price = 180, level = 1, mass = 980, maxSpeed = 33, accel = 16, turn = 125, health = 95,
                swaps = Paint(0.84f, 0.1f, 0.1f) },
            new CarDef { id = "foodtruck", name = "Trak Makanan", model = "veh_food_truck", price = 260, level = 5, mass = 2400, maxSpeed = 24, accel = 11, turn = 95, grip = 8, health = 180 },
            new CarDef { id = "kereta", name = "Myvi Hijau", model = "veh_myvi", price = 0, level = 2, mass = 980, maxSpeed = 32, accel = 15, turn = 125,
                swaps = Paint(0.2f, 0.62f, 0.34f) },
            new CarDef { id = "basmini", name = "Bas Mini Pink", model = "Car_BasMini", price = 250, level = 2, mass = 4200, maxSpeed = 25, accel = 10, turn = 85, grip = 7, health = 220 },
            new CarDef { id = "hilux", name = "Hilux 4x4", model = "veh_hilux", price = 350, level = 2, mass = 2100, maxSpeed = 31, accel = 14, turn = 105, grip = 8, health = 200,
                swaps = Paint(0.95f, 0.95f, 0.93f) },
            new CarDef { id = "van", name = "Alphard Hitam", model = "veh_alphard", price = 220, level = 3, mass = 2000, maxSpeed = 30, accel = 12, turn = 100, health = 150,
                swaps = Paint(0.1f, 0.1f, 0.12f) },
            new CarDef { id = "saga_rempit", name = "Saga Rempit", model = "veh_saga", price = 300, level = 3, maxSpeed = 38, accel = 18, turn = 125,
                swaps = Paint(0.95f, 0.45f, 0.15f) },
            new CarDef { id = "bike", name = "Motor Penghantar", model = "veh_delivery_bike", price = 0, level = 5, mass = 180, maxSpeed = 24, accel = 13, turn = 150, grip = 12, health = 90 },
            new CarDef { id = "polis", name = "Kereta Polis", model = "veh_polis", price = 400, level = 4, maxSpeed = 36, accel = 17, health = 140,
                swaps = new Dictionary<string, Color> { ["car_paint"] = C(0.96f, 0.96f, 0.97f) } },
            new CarDef { id = "limo", name = "Alphard Datuk", model = "veh_alphard", price = 500, level = 4, mass = 2100, maxSpeed = 34, health = 160,
                swaps = Paint(0.94f, 0.94f, 0.95f) },
        };

        public static CarDef Car(string id) => Cars.Find(c => c.id == id) ?? Cars[0];

        public static readonly LevelDef[] Levels =
        {
            null,
            new LevelDef { number = 1, title = "Tahap 1: Teh Tarik Tragedi", subtitle = "Pak Mat", player = "pakmat", familyCar = "saga",
                sunEuler = new Vector3(48, 35, 0), sun = C(1f, 0.97f, 0.9f), paper = C(1f, 0.98f, 0.92f), fog = C(0.74f, 0.88f, 0.99f), shadowTint = C(0.68f, 0.7f, 0.9f),
                zenith = C(0.3f, 0.6f, 0.96f), cloudCover = 0.45f },
            new LevelDef { number = 2, title = "Tahap 2: Cendol Ajaib", subtitle = "Mak Som", player = "maksom", familyCar = "kereta",
                sunEuler = new Vector3(62, 140, 0), sun = C(1f, 0.99f, 0.94f), paper = C(1f, 0.98f, 0.92f), fog = C(0.78f, 0.9f, 0.99f), shadowTint = C(0.66f, 0.7f, 0.9f),
                zenith = C(0.22f, 0.55f, 0.95f), cloudCover = 0.6f },
            new LevelDef { number = 3, title = "Tahap 3: Rempit Senja", subtitle = "Along", player = "along", familyCar = "saga",
                sunEuler = new Vector3(18, 250, 0), sun = C(1f, 0.75f, 0.55f), paper = C(1f, 0.9f, 0.78f), fog = C(1f, 0.66f, 0.45f), shadowTint = C(0.62f, 0.52f, 0.8f),
                zenith = C(0.36f, 0.34f, 0.72f), cloud = C(1f, 0.82f, 0.7f), cloudShade = C(0.95f, 0.55f, 0.55f), cloudCover = 0.5f },
            new LevelDef { number = 4, title = "Tahap 4: Malam Menara", subtitle = "Adik", player = "adik", familyCar = "kancil",
                sunEuler = new Vector3(40, 300, 0), sun = C(0.62f, 0.7f, 0.98f), paper = C(0.55f, 0.6f, 0.75f), fog = C(0.16f, 0.2f, 0.4f), shadowTint = C(0.5f, 0.52f, 0.8f), fogStart = 90, fogEnd = 330,
                zenith = C(0.04f, 0.06f, 0.18f), cloud = C(0.32f, 0.36f, 0.55f), cloudShade = C(0.2f, 0.24f, 0.42f), cloudCover = 0.35f, stars = 1f },
            new LevelDef { number = 5, title = "Tahap 5: Penghantar Chow Kit", subtitle = "Aiman", player = "aiman", familyCar = "bike",
                sunEuler = new Vector3(34, 200, 0), sun = C(1f, 0.9f, 0.78f), paper = C(0.97f, 0.95f, 0.9f), fog = C(0.72f, 0.78f, 0.84f), shadowTint = C(0.58f, 0.62f, 0.82f),
                zenith = C(0.36f, 0.5f, 0.72f), cloud = C(0.94f, 0.94f, 0.96f), cloudShade = C(0.66f, 0.7f, 0.8f), cloudCover = 0.75f, fogStart = 120, fogEnd = 380 },
        };
    }
}
