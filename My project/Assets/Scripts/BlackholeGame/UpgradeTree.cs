using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlackholeGame
{
    // 노드 하나. 부모가 뚫려야 뚫린다. 위치는 부모 + dir * spacing.
    // 효과 텍스트는 노드에 안 보이고, 마우스 오버 시 툴팁으로만 표시된다.
    // tier = 이 노드를 열려면 클리어해야 하는 스테이지 수 (0 = 처음부터). Phase 2에서 게이팅에 사용.
    public class UpgradeNode
    {
        public readonly string id, parentId, label, desc, icon;   // label/desc = Loc 키
        public readonly float descArg;                            // 설명 서식 인자 (0 = 없음)
        public readonly Vector2 dir;        // 부모로부터의 방향 (스크린 좌표, 위 = -Y)
        public readonly int cost;
        public readonly int tier;
        public readonly Action<Stats> apply;

        public UpgradeNode(string id, string parentId, Vector2 dir, string icon,
                           string label, int cost, int tier, string desc, float descArg, Action<Stats> apply)
        {
            this.id = id; this.parentId = parentId; this.dir = dir; this.icon = icon;
            this.label = label; this.cost = cost; this.tier = tier; this.desc = desc; this.descArg = descArg; this.apply = apply;
        }
        public bool IsRoot => parentId == null;
    }

    // ============================================================
    // 스킬트리 (round42) — 능력치마다 한 가지, 같은 노드가 위로 쭉.
    //   코어에서 4갈래(분야) → 분야마다 능력치별 가지로 갈라진다. 약 300노드.
    //     공격        : [자동공격] → 배수 · 공격력 · 공속
    //     치명/범위   : [범위 1]  → 치명확률 · 범위 · 치명배수
    //     경제        : [골드 1]  → 시간 · 골드 · 웨이브 스킵
    //     소환/백신   : [소환 1]  → 동시소환 · 소환 · [백신] → 백신 대미지 · 범위 · 빈도
    //   tier(= 몇 지역을 깨야 열리나)는 가지 안에서의 높이로 정한다 — 가지마다 아래는 싸고 약하게,
    //   위로 갈수록 비싸고 늦게 열린다. 상한이 있는 능력치는 짧은 가지, 없는 건 긴 가지.
    //   ※ balance_sim/gamedata.py 가 이 표를 그대로 복제한다 — 값을 바꾸면 거기도 같이.
    // ============================================================
    public static class UpgradeTree
    {
        public const string RootId = "root";
        public const int Tiers = 8;                 // 8지역 = 8티어

        // ---- 상한 ----
        public const float MultCap = 300f;          // 배수 버킷(%p)
        public const float CritCap = 0.80f;         // 치명 확률 (round42: 0.75 → 0.80)
        public const float IntervalFloor = 0.06f;   // 타격 간격 하한
        public const float TimeCap = 120f;          // 시작 시간 +초 (round42: 60 → 120)
        public const float SpawnFloor = 0.22f;      // 소환 간격 배율 하한
        public const int   SCountMax = 10;          // 동시 소환 (round42: 기본 1 → 최대 10)
        public const float BombDmgMax = 5f, BombRadMax = 2f, BombIntervalMin = 10f;

        static float CM(float v) => Mathf.Min(MultCap, v);
        static float CC(float v) => Mathf.Min(CritCap, v);
        static float CI(float v) => Mathf.Max(IntervalFloor, v);
        static float CT(float v) => Mathf.Min(TimeCap, v);
        static float CS(float v) => Mathf.Max(SpawnFloor, v);
        static int   CN(int v)   => Mathf.Clamp(v, 1, SCountMax);

        // ---- 노드 1개당 수치 ----
        public const float MultPP = 10f, SpeedMul = 0.915f, CritCPP = 0.04f, CritXAdd = 0.08f,
                           RangeAdd = 0.10f, GoldPP = 8f, TimeAdd = 6f, SpawnMul = 0.93f,
                           BombDmgAdd = 0.25f, BombRadAdd = 0.10f, BombIntervalSub = 1f;

        // tier별 노드 비용 — balance_sim(mutation_calc)으로 "변이0 8지역 ≈ 5시간"에 맞춘다.
        static readonly int[] TierCost = { 15, 750, 1900, 40000, 110000, 240000, 1100000, 1500000 };   // round42 튜닝: 변이0 8지역 ≈ 5.3시간(시뮬)
        public static int Cost(int tier) =>
            tier >= 0 && tier < TierCost.Length ? TierCost[tier]
            : Mathf.RoundToInt(TierCost[TierCost.Length - 1] * Mathf.Pow(2.6f, tier - TierCost.Length + 1));

        // 공격력 노드값은 티어별 표(가지 안 높이 → 티어 → 값)
        static readonly int[] FlatByTier = { 10, 12, 15, 25, 44, 78, 145, 280 };
        public static int FlatAt(int tier) =>
            tier >= 0 && tier < FlatByTier.Length ? FlatByTier[tier]
            : Mathf.RoundToInt(FlatByTier[FlatByTier.Length - 1] * Mathf.Pow(1.9f, tier - FlatByTier.Length + 1));

        public enum T { Flat, Mult, Speed, CritC, CritX, Range, Gold, Time, Spawn, SCount, Skip, Auto, Bomb, BombDmg, BombRad, BombFreq }

        // 가지 정의 — (타입, id 접두어, 노드 수, 시작 티어). 노드 수는 머리 노드를 포함한 전체 길이.
        //   합계 297 + 자동공격 + 백신 = 299, 코어까지 300.
        public struct LaneDef { public T type; public string key; public int len; public int tier0; }
        static LaneDef L(T t, string key, int len, int tier0 = 0) => new LaneDef { type = t, key = key, len = len, tier0 = tier0 };
        public static readonly LaneDef
            LFlat  = L(T.Flat,  "flat",  36),  LMult  = L(T.Mult,  "mult",  30),  LSpeed = L(T.Speed, "speed", 28),
            LCritC = L(T.CritC, "critc", 20),  LRange = L(T.Range, "range", 28),  LCritX = L(T.CritX, "critx", 34),
            LTime  = L(T.Time,  "time",  20),  LGold  = L(T.Gold,  "gold",  34),  LSkip  = L(T.Skip,  "skip",  6),
            LSCount= L(T.SCount,"scount", 9),  LSpawn = L(T.Spawn, "spawn", 20),
            LBDmg  = L(T.BombDmg, "bombdmg", 12, 1), LBRad = L(T.BombRad, "bombrad", 10, 1), LBFreq = L(T.BombFreq, "bombfreq", 10, 1);
        public const int BombUnlockTier = 1;   // 백신은 1지역을 깬 뒤 — 기본 조작에 익숙해진 다음 새 요소

        // 가지 안 k번째(0부터) 노드의 티어 — 시작 티어부터 마지막 티어(7)까지 높이 비율로 고르게
        public static int TierOf(LaneDef d, int k) => d.tier0 + (k * (Tiers - d.tier0)) / d.len;

        static (string icon, string label, string desc, float descArg, Action<Stats> apply) Effect(T t, int tier)
        {
            switch (t)
            {
                case T.Flat:
                    int fa = FlatAt(tier);
                    return ("sword", "n.flat", "nd.flat", fa, s => s.flatBonus += fa);
                case T.Mult:   return ("mult", "n.mult", "nd.mult", MultPP, s => s.multBucketPercent = CM(s.multBucketPercent + MultPP));
                case T.Speed:  return ("bolt", "n.speed", "nd.speed", SpeedMul, s => s.attackInterval = CI(s.attackInterval * SpeedMul));
                case T.CritC:  return ("crosshair", "n.critc", "nd.critc", CritCPP * 100f, s => s.critChance = CC(s.critChance + CritCPP));
                case T.CritX:  return ("star", "n.critx", "nd.critx", CritXAdd, s => s.critMult += CritXAdd);
                case T.Range:  return ("ring", "n.range", "nd.range", RangeAdd, s => s.cursorRadius += RangeAdd);
                case T.Gold:   return ("coin", "n.gold", "nd.gold", GoldPP, s => s.goldMultPercent += GoldPP);
                case T.Time:   return ("clock", "n.time", "nd.time", TimeAdd, s => s.bonusTimeSec = CT(s.bonusTimeSec + TimeAdd));
                case T.Spawn:  return ("chevrons", "n.spawn", "nd.spawn", SpawnMul, s => s.spawnIntervalMult = CS(s.spawnIntervalMult * SpawnMul));
                case T.SCount: return ("cells", "n.scount", "nd.scount", 1f, s => s.spawnCount = CN(s.spawnCount + 1));
                case T.Skip:   return ("skip", "n.skip", "nd.skip", 1f, s => s.startWave += 1);
                case T.Bomb:   return ("pill", "n.bomb", "nd.bomb", 0f, s => s.bombUnlocked = true);
                case T.BombDmg:  return ("blast", "n.bombdmg", "nd.bombdmg", BombDmgAdd, s => s.bombDmgMul = Mathf.Min(BombDmgMax, s.bombDmgMul + BombDmgAdd));
                case T.BombRad:  return ("blastring", "n.bombrad", "nd.bombrad", BombRadAdd, s => s.bombRadiusMul = Mathf.Min(BombRadMax, s.bombRadiusMul + BombRadAdd));
                case T.BombFreq: return ("pillfast", "n.bombfreq", "nd.bombfreq", BombIntervalSub, s => s.bombInterval = Mathf.Max(BombIntervalMin, s.bombInterval - BombIntervalSub));
                case T.Auto:
                default:
                    return ("bolt", "n.auto", "nd.auto", 0f, s => s.autoAttack = true);
            }
        }

        static UpgradeNode Make(List<UpgradeNode> list, string id, string parent, T t, int tier)
        {
            var e = Effect(t, tier);
            var n = new UpgradeNode(id, parent, Vector2.zero, e.icon, e.label, Cost(tier), tier, e.desc, e.descArg, e.apply);
            list.Add(n);
            return n;
        }

        // 가지 하나를 parent 밑에 쭉 — from 번째 노드부터(머리 노드를 이미 만들었으면 1부터).
        static void Lane(List<UpgradeNode> list, LaneDef d, string parent, int from = 0)
        {
            for (int k = from; k < d.len; k++)
            {
                string id = d.key + k;
                Make(list, id, parent, d.type, TierOf(d, k));
                parent = id;
            }
        }

        // 자식 순서 = 화면 왼쪽→오른쪽. 이어지는 가지를 가운데에 둬서 머리 노드 바로 위로 곧게 뻗게.
        public static List<UpgradeNode> BuildAll()
        {
            var list = new List<UpgradeNode>();
            list.Add(new UpgradeNode(RootId, null, Vector2.zero, "hex", "n.core", 0, 0, "nd.core", 0f, s => s.flatBonus += 8f));

            // 공격 — 머리는 자동공격(첫 구매)
            Make(list, "auto", RootId, T.Auto, 0);
            Lane(list, LMult, "auto");
            Lane(list, LFlat, "auto");
            Lane(list, LSpeed, "auto");

            // 치명/범위 — 머리 = 범위 1
            Make(list, LRange.key + 0, RootId, T.Range, TierOf(LRange, 0));
            Lane(list, LCritC, LRange.key + 0);
            Lane(list, LRange, LRange.key + 0, 1);
            Lane(list, LCritX, LRange.key + 0);

            // 경제 — 머리 = 골드 1
            Make(list, LGold.key + 0, RootId, T.Gold, TierOf(LGold, 0));
            Lane(list, LTime, LGold.key + 0);
            Lane(list, LGold, LGold.key + 0, 1);
            Lane(list, LSkip, LGold.key + 0);

            // 소환/백신 — 머리 = 소환 1, 그 위에 [백신] 해금 노드가 따로 갈라져 3갈래
            Make(list, LSpawn.key + 0, RootId, T.Spawn, TierOf(LSpawn, 0));
            Lane(list, LSCount, LSpawn.key + 0);
            Lane(list, LSpawn, LSpawn.key + 0, 1);
            Make(list, "bomb", LSpawn.key + 0, T.Bomb, BombUnlockTier);
            Lane(list, LBDmg, "bomb");
            Lane(list, LBRad, "bomb");
            Lane(list, LBFreq, "bomb");

            return list;
        }

        // ============================================================
        // 메타(환생) 강화 — 환생으로도 유지되는 영구 업그레이드. 재화 = shard.
        //   baseStats 에 한 번 적용된다(런/스테이지마다 이득). 트리 아님, 그냥 목록.
        // ============================================================
        public class MetaNode
        {
            public readonly string id, label, desc;
            public readonly int cost, maxLv, unlockAsc;   // unlockAsc = 이 노드가 보이려면 필요한 승천 레벨(0=항상)
            public readonly Action<Stats, int> apply;   // (baseStats, 레벨)
            public MetaNode(string id, string label, string desc, int cost, int maxLv, int unlockAsc, Action<Stats, int> apply)
            { this.id = id; this.label = label; this.desc = desc; this.cost = cost; this.maxLv = maxLv; this.unlockAsc = unlockAsc; this.apply = apply; }
        }

        public static List<MetaNode> BuildMeta()
        {
            return new List<MetaNode>
            {
                new MetaNode("m_dmg",   "m.dmg",   "md.dmg", 3, 10, 0,
                    (s, lv) => s.multBucketPercent += 8f * lv),
                new MetaNode("m_spd",   "m.spd",   "md.spd",   4, 8, 0,
                    (s, lv) => s.attackInterval = Mathf.Max(0.06f, s.attackInterval * Mathf.Pow(0.94f, lv))),
                new MetaNode("m_gold",  "m.gold",   "md.gold",     3, 10, 0,
                    (s, lv) => s.goldMultPercent += 15f * lv),
                new MetaNode("m_range", "m.range",   "md.range",    5, 6, 0,
                    (s, lv) => s.cursorRadius += 0.08f * lv),
                new MetaNode("m_crit",  "m.crit",   "md.crit",     5, 8, 0,
                    (s, lv) => s.critChance = Mathf.Min(CritCap, s.critChance + 0.04f * lv)),
                new MetaNode("m_start", "m.start", "md.start",  4, 10, 0,
                    (s, lv) => { /* 시작 골드는 GameManager 에서 metaLv 로 처리 */ }),
                new MetaNode("m_auto",  "m.auto", "md.auto", 6, 1, 0,
                    (s, lv) => { if (lv > 0) s.autoAttack = true; }),
                // ---- 승천으로만 열리는 노드 — "승천에 따른 강화요소 추가" ----
                new MetaNode("m_critx", "m.critx", "md.critx", 6, 6, 1,
                    (s, lv) => s.critMult += 0.05f * lv),
                new MetaNode("m_time",  "m.time",  "md.time",  5, 8, 2,
                    (s, lv) => s.bonusTimeSec += 3f * lv),
            };
        }
    }
}
