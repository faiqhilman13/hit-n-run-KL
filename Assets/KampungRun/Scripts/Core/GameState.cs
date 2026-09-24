using System;
using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    [Serializable]
    public class SaveData
    {
        public int level = 1;                    // current level 1..4
        public int[] missionsDone = new int[6];  // per level (index 1..5): story missions completed
        public bool[] raceDone = new bool[6];
        public int coins;
        public List<string> cards = new List<string>();
        public List<string> cameras = new List<string>();  // burung kamera smashed
        public List<string> ownedCars = new List<string>();
        public List<string> ownedCostumes = new List<string>();
        public List<string> wornCostume = new List<string>(); // "char=costumeId"
        public bool finale;
    }

    /// <summary>Progress, coins and collectibles. Saved to PlayerPrefs as JSON.</summary>
    public static class GameState
    {
        const string Key = "KampungRun.Save.v1";
        public static SaveData Data = new SaveData();
        public static bool Ephemeral; // tests: never touch the real save
        public static event Action Changed;

        public static int Level => Data.level;
        public static int Coins => Data.coins;

        public static void Load()
        {
            if (Ephemeral) return;
            LoadNow();
        }

        static void LoadNow()
        {
            var json = PlayerPrefs.GetString(Key, "");
            Data = string.IsNullOrEmpty(json) ? new SaveData() : JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
            if (Data.missionsDone == null) Data.missionsDone = new int[6];
            if (Data.raceDone == null) Data.raceDone = new bool[6];
            if (Data.missionsDone.Length < 6) System.Array.Resize(ref Data.missionsDone, 6);
            if (Data.raceDone.Length < 6) System.Array.Resize(ref Data.raceDone, 6);
            Data.level = Mathf.Clamp(Data.level, 1, GameData.LastLevel);
        }

        public static void Save()
        {
            if (Ephemeral) return;
            SaveNow();
        }

        static void SaveNow()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        public static void NewGame()
        {
            Data = new SaveData();
            Save();
        }

        public static bool HasSave => PlayerPrefs.HasKey(Key);

        public static void AddCoins(int n)
        {
            Data.coins = Mathf.Max(0, Data.coins + n);
            Changed?.Invoke();
        }

        public static bool Spend(int n)
        {
            if (Data.coins < n) return false;
            Data.coins -= n;
            Changed?.Invoke();
            Save();
            return true;
        }

        public static void CollectCard(string id)
        {
            if (!Data.cards.Contains(id)) Data.cards.Add(id);
            Changed?.Invoke();
            Save();
        }

        public static int CardsInLevel(int level)
        {
            int n = 0;
            foreach (var c in Data.cards) if (c.StartsWith($"L{level}_")) n++;
            return n;
        }

        public static void SmashCamera(string id)
        {
            if (!Data.cameras.Contains(id)) Data.cameras.Add(id);
            Changed?.Invoke();
            Save();
        }

        public static int CamerasInLevel(int level)
        {
            int n = 0;
            foreach (var c in Data.cameras) if (c.StartsWith($"L{level}_")) n++;
            return n;
        }

        public static string Costume(string character)
        {
            foreach (var w in Data.wornCostume)
                if (w.StartsWith(character + "=")) return w.Substring(character.Length + 1);
            return "default";
        }

        public static void Wear(string character, string costume)
        {
            Data.wornCostume.RemoveAll(w => w.StartsWith(character + "="));
            Data.wornCostume.Add(character + "=" + costume);
            Save();
        }

        /// <summary>Overall completion in %, like H&amp;R's percentage on the save screen.</summary>
        public static int Percent
        {
            get
            {
                float total = 0, got = 0;
                for (int l = 1; l <= GameData.LastLevel; l++)
                {
                    total += GameData.MissionsPerLevel + 1 + GameData.CardsPerLevel + GameData.CamerasPerLevel;
                    got += Mathf.Min(Data.missionsDone[l], GameData.MissionsPerLevel) + (Data.raceDone[l] ? 1 : 0) + CardsInLevel(l) + CamerasInLevel(l);
                }
                total += GameData.Cars.Count + GameData.Costumes.Count;
                got += Data.ownedCars.Count + Data.ownedCostumes.Count;
                return Mathf.RoundToInt(got / total * 100f);
            }
        }

        public static void Notify() => Changed?.Invoke();
    }
}
