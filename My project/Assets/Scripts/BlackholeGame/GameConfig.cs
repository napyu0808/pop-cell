using System;
using UnityEngine;

namespace BlackholeGame
{
    // ============================================================
    // 스탯 — 전부 "버킷 구조". 노드는 이 값에 더하기만 한다. (HANDOFF 4장)
    //   최종 공격력 = (기본 + 고정합) × (1 + Σ배수%/100)   ← 배수는 합산, 상한 200 → ×3.0
    //   치명타는 이 공식 "바깥"에서 곱해진다.
    // ============================================================
    [Serializable]
    public class Stats
    {
        public float baseAttack = 10f;
        public float flatBonus = 0f;           // 고정합 버킷
        public float multBucketPercent = 0f;   // 배수 버킷(합산). 상한 200 => ×3.0
        [Range(0f, 0.75f)] public float critChance = 0f;
        public float critMult = 2.0f;
        public float attackInterval = 0.72f;   // 타격 간격(초). 하한 0.15. 수동 클릭은 이보다 항상 느림
        public float goldMultPercent = 0f;
        public float cursorRadius = 0.45f;     // 커서 타격 범위(월드 단위). 초반은 좁게. 커서 범위 노드는 맨 마지막
        public float bonusTimeSec = 0f;        // 제한시간 +초
        public float spawnIntervalMult = 1f;   // 소환 가속 노드가 낮춘다 (하한 0.25). 낮을수록 적이 더 자주 등장
        public int spawnCount = 2;             // 소환 1회당 적 마릿수 (기본 2, 노드로 +1, 최대 5)
        public int startWave = 1;              // 스킵 노드로 시작 웨이브 상승 (5,10,15,20)
        public bool autoAttack = false;        // 자동 공격 해금 여부. false면 좌클릭으로만 타격(초반 수동 구간)

        public float GetHitDamage()
            => (baseAttack + flatBonus) * (1f + multBucketPercent / 100f);

        public Stats Clone() => new Stats
        {
            baseAttack = baseAttack, flatBonus = flatBonus, multBucketPercent = multBucketPercent,
            critChance = critChance, critMult = critMult, attackInterval = attackInterval,
            goldMultPercent = goldMultPercent, cursorRadius = cursorRadius,
            bonusTimeSec = bonusTimeSec, spawnIntervalMult = spawnIntervalMult,
            spawnCount = spawnCount, startWave = startWave, autoAttack = autoAttack,
        };

        // 치명타는 타격 시점에 공식 바깥에서 롤
        public (float amount, bool crit) RollDamage()
        {
            float dmg = GetHitDamage();
            bool isCrit = UnityEngine.Random.value < critChance;
            return (isCrit ? dmg * critMult : dmg, isCrit);
        }

        // 참고용: 범위 안 적 1마리 기준 이론 DPS
        public float EstimateDps()
        {
            float aps = 1f / Mathf.Max(0.0001f, attackInterval);
            float critFactor = 1f + critChance * (critMult - 1f);
            return GetHitDamage() * aps * critFactor;
        }
    }

    // ============================================================
    // 웨이브 설정 — 밸런스 값은 전부 데이터로 분리 (HANDOFF 7장, 하드코딩 금지)
    //   30웨이브 기준으로 조정. 세부 밸런스는 플레이하며 계속 손볼 예정.
    // ============================================================
    [Serializable]
    public class WaveConfig
    {
        public int baseQuota = 8;
        public float baseTimeLimit = 40f;      // 40웨이브 + 보스전
        public float baseEnemyHp = 10f;
        // 풀강 타격 1,968 기준 "최대 크기 적을 원킬"할 수 있는 마지막 웨이브가 정확히 30이 되는 값.
        //   w30 = 10×1.17^29 ≈ 949 (×2 크기 = 1,898 ≤ 1,968 → 원킬)
        //   w31 = 1,110 (×2 = 2,221 > 1,968 → 원킬 불가) → 31~40 + 보스가 도전 구간
        public float hpGrowth = 1.17f;
        public int startEnemies = 14;
        public int totalWaves = 40;            // 마지막 = 보스 웨이브
        public int maxEnemies = 150;
        public int bossHp = 1000000;           // 40웨이브 보스 체력. 처치 = 엔딩

        public float enemyRadius = 0.28f;      // 적 기본 반지름(월드 단위)
        public Vector2 enemySizeRange = new Vector2(1f, 2f); // 스폰 시 반지름 배율 랜덤 범위 (기본 1~2배)

        [Header("골드 — 처치 시 재화는 '적이 소환된 웨이브' 기준. GoldCurve로 웨이브별 직접 지정")]
        public float goldRate = 1f;            // 전체 골드 배율 (QA 조절용)

        // 웨이브 1 → index 0.
        // 기울기가 완만해야 하는 이유: 처치 골드는 "적이 소환된 웨이브" 기준이라, 곡선이 가파르면
        //   스킵 노드(웨이브 25부터 시작)가 골드/처치를 수십 배로 뻥튀기해서 런당 수입이
        //   매 판 7~10배씩 폭증한다. (그래서 예전엔 노드 값을 아무리 올려도 8판이면 트리가 다 뚫렸다)
        //   1→823(823배)에서 1→158(158배)로 압축 → 수입이 완만히 올라 30~50분짜리 진행이 된다.
        static readonly int[] GoldCurve =
        {
            1, 2, 5, 10, 12, 15, 20,                       // w1–7 (초반 램프, 유지)
            21, 23, 24, 26, 27, 29, 31, 33, 35, 38, 40, 43, // w8–19
            45, 48, 51, 55, 58, 62, 66, 70, 75, 80, 85,     // w20–30
            90, 96, 102, 109, 116, 123, 131, 140, 149, 158  // w31–40
        };

        public int QuotaAt(int w)        => baseQuota + (w - 1) * 2;
        public float TimeAt(int w)       => baseTimeLimit;
        public float EnemyHpAt(int w)    => baseEnemyHp * Mathf.Pow(hpGrowth, w - 1);

        // w번 웨이브에 소환된 적을 잡으면 주는 골드
        public int GoldAt(int w)
        {
            float g;
            if (w <= GoldCurve.Length)
                g = GoldCurve[Mathf.Max(1, w) - 1];
            else                                   // 무한 모드: 30 이후 완만히 계속 증가
                g = GoldCurve[GoldCurve.Length - 1] * Mathf.Pow(1.10f, w - GoldCurve.Length);
            return Mathf.Max(1, Mathf.RoundToInt(g * goldRate));
        }
        // 누적 스폰 간격: 잡아도 리스폰 없음. 후반에 더 자주 나오게 (플레이타임 벽 완화)
        public float SpawnIntervalAt(int w) => Mathf.Max(0.16f, 0.50f - (w - 1) * 0.013f);

        // ---- Phase 2: 스테이지(지역) 파라미터를 이 WaveConfig 에 채워 넣는다 ----
        //   기존 wave.XXX 호출부는 그대로 두고, 스테이지 선택 시 이 메서드로 값만 교체.
        public void LoadStage(int idx)
        {
            var st = StageConfig.Stages[Mathf.Clamp(idx, 0, StageConfig.Stages.Length - 1)];
            totalWaves    = st.waves;          // 마지막 웨이브 = 보스
            baseTimeLimit = st.timeLimit;
            baseEnemyHp   = st.enemyHp0;
            hpGrowth      = st.hpGrowth;
            bossHp        = st.bossHp;
            baseQuota     = st.quota0;
            startEnemies  = st.startEnemies;
            goldRate      = st.goldRate;
        }
    }

    // ============================================================
    // 스테이지(지역) 데이터 — 8개. 냥코대전쟁식 세계 맵.
    //   각 지역은 tier(=idx) 노드를 풀강해야 보스를 잡을 수 있는 DPS 체크.
    //   지역 N 클리어 -> tier N 노드 해금. 값은 시뮬 1차 + 플레이테스트로 조정.
    // ============================================================
    public class StageConfig
    {
        public readonly int index;
        public readonly string name;
        public readonly int waves;
        public readonly float timeLimit;
        public readonly float enemyHp0;
        public readonly float hpGrowth;
        public readonly int bossHp;
        public readonly int quota0;
        public readonly int startEnemies;
        public readonly float goldRate;
        public readonly Color theme;

        StageConfig(int i, string n, int w, float t, float hp0, float hpg, int boss,
                    int q0, int se, float gr, Color th)
        {
            index = i; name = n; waves = w; timeLimit = t; enemyHp0 = hp0; hpGrowth = hpg;
            bossHp = boss; quota0 = q0; startEnemies = se; goldRate = gr; theme = th;
        }

        // 1차 밸런스 (플레이테스트로 조정). 목표 클리어타임 S1=15 S2=30 S3~ 40~65분.
        public static readonly StageConfig[] Stages =
        {
            new StageConfig(0, "s.0",  4, 26f,  34f, 1.130f,     1500,  4,  8, 0.50f, new Color(0.42f,0.72f,0.46f)),
            new StageConfig(1, "s.1",         5, 32f,  70f, 1.136f,     6000,  5,  9, 0.72f, new Color(0.36f,0.62f,0.70f)),
            new StageConfig(2, "s.2",         6, 40f, 118f, 1.142f,    18600,  6, 10, 0.85f, new Color(0.60f,0.54f,0.36f)),
            new StageConfig(3, "s.3",         8, 48f, 134f, 1.148f,    46000,  7, 11, 1.00f, new Color(0.40f,0.46f,0.60f)),
            new StageConfig(4, "s.4",          10, 56f, 157f, 1.154f,   112000,  8, 12, 1.10f, new Color(0.30f,0.58f,0.62f)),
            new StageConfig(5, "s.5",          12, 64f, 177f, 1.160f,   273000,  9, 13, 1.20f, new Color(0.34f,0.56f,0.34f)),
            new StageConfig(6, "s.6",       14, 72f, 162f, 1.166f,   563000, 10, 14, 1.40f, new Color(0.62f,0.66f,0.72f)),
            new StageConfig(7, "s.7",       18, 84f, 116f, 1.172f,  1115000, 11, 15, 1.70f, new Color(0.66f,0.30f,0.34f)),
        };
    }
}
