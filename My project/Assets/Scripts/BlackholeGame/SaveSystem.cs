using System.Collections.Generic;
using UnityEngine;

namespace BlackholeGame
{
    // ============================================================
    // 진행 저장 — PlayerPrefs 에 JSON 한 덩어리. WebGL 에선 브라우저(IndexedDB) 저장.
    //   환생(Phase 3)만 stagesCleared/currentStage/gold/bought 를 리셋한다.
    //   meta* 는 환생으로도 유지.
    // ============================================================
    [System.Serializable]
    public class SaveData
    {
        public int version = 1;
        public int stagesCleared;
        public int currentStage;
        public int gold;
        public int retryCount;
        public int maxScore;          // 프레스티지 보상 기준 = stagesCleared*100 + 최고 도달 웨이브
        public List<string> bought = new List<string>();

        // Phase 3 (환생) — 지금은 0/빈 값으로 저장만
        public int metaCurrency;
        public List<string> metaBought = new List<string>();

        public bool HasProgress => stagesCleared > 0 || bought.Count > 0 || gold > 0;
    }

    public static class SaveSystem
    {
        const string Key = "popcell.save.v1";

        public static bool Exists() => PlayerPrefs.HasKey(Key);

        public static SaveData Load()
        {
            if (!PlayerPrefs.HasKey(Key)) return null;
            try
            {
                var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key));
                if (d == null || d.version != 1) return null;
                if (d.bought == null) d.bought = new List<string>();
                if (d.metaBought == null) d.metaBought = new List<string>();
                return d;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[POP Cell] 세이브 로드 실패, 무시: " + e.Message);
                return null;
            }
        }

        public static void Save(SaveData d)
        {
            try
            {
                PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
                PlayerPrefs.Save();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[POP Cell] 세이브 실패: " + e.Message);
            }
        }

        public static void Wipe()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
