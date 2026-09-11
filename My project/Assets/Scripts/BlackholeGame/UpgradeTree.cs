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
    // 스킬트리 (POP Cell 2.0) — 코어에서 위로 전투 클러스터(5갈래), 아래로 유틸 클러스터(4갈래).
    //   갈래마다 노드 타입이 섞여서 뻗는다(공격→배수→공속→치명 …). 스탯 하나가 한 줄로
    //   길게 늘어지지 않게. 총 160노드 = 8스테이지 × 20씩 해금(tier). 폭넓게 재밸런싱.
    // ============================================================
    public static class UpgradeTree
    {
        public const string RootId = "root";

        public const int NodesPerTier = 20;
        public const int Tiers = 8;                 // 160 노드 + 코어

        static float CM(float v) => Mathf.Min(300f, v);   // 배수 상한(기존 240 — 배수 노드가 상한에 걸려 죽는 걸 막음)
        static float CC(float v) => Mathf.Min(0.75f, v);  // 치명 확률 상한
        static float CI(float v) => Mathf.Max(0.06f, v);  // 타격 간격 하한 — 후반 "때리는 맛"을 위해 크게 낮춤(기존 0.15)
        static float CT(float v) => Mathf.Min(60f, v);    // 시작 시간 버퍼 상한
        static float CS(float v) => Mathf.Max(0.22f, v);  // 소환 간격 배율 하한
        static int   CN(int v)   => Mathf.Clamp(v, 2, 6); // 동시 소환 마릿수

        // tier별 노드 비용. balance_sim 으로 "tier T 풀강 → 스테이지 T 클리어" 곡선에 맞춰 재조정 예정.
        // 2시간 풀클리어 목표 (balance_sim/stage_calc 검증: 시뮬 ~430분, 실측 배율 ÷3.8 ≈ 113분).
        //   티어0 은 60 → 15. 실측해보니 1지역 풀클리어 1회 수입이 31골드라 노드 하나(60)를 사는 데
        //   2판이 필요했고, 그 사이 화력이 안 늘어 보스(1800)를 제한시간 안에 절대 못 잡는 데드락이었다.
        //   (시뮬: 현재값으론 30판을 돌려도 클리어 불가) 첫 판에 바로 자동공격 노드를 사게 만든다.
        static readonly int[] TierCost =
        {
            15, 700, 2300, 9000, 38000, 145000, 380000, 880000
        };
        static int Cost(int tier) =>
            tier >= 0 && tier < TierCost.Length ? TierCost[tier]
            : Mathf.RoundToInt(TierCost[TierCost.Length - 1] * Mathf.Pow(2.6f, tier - TierCost.Length + 1));

        static Vector2 Rot(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ---- 노드 효과 타입 ----
        // d = 그 갈래에서의 깊이(0부터). 값은 깊이에 따라 커진다.
        enum T { Flat, Mult, Speed, CritC, CritX, Range, Gold, Time, Spawn, SCount, Skip, Auto }

        // 공격력 노드값은 티어별 표 — 예전엔 "가지에서의 깊이(d)" 기준이라 같은 티어 안에서도
        // 값이 제각각이었고, 어느 티어가 얼마나 세지는지 설계로 정할 수가 없었다.
        static readonly int[] FlatByTier = { 10, 12, 15, 25, 44, 78, 145, 280 };
        static int FlatAt(int tier) =>
            tier >= 0 && tier < FlatByTier.Length ? FlatByTier[tier]
            : Mathf.RoundToInt(FlatByTier[FlatByTier.Length - 1] * Mathf.Pow(1.9f, tier - FlatByTier.Length + 1));

        static (string icon, string label, string desc, float descArg, Action<Stats> apply) Effect(T t, int tier, ref int skipIdx)
        {
            switch (t)
            {
                case T.Flat:
                    int fa = FlatAt(tier);
                    return ("sword", "n.flat", "nd.flat", fa, s => s.flatBonus += fa);
                case T.Mult:
                    return ("mult", "n.mult", "nd.mult", 0f, s => s.multBucketPercent = CM(s.multBucketPercent + 11f));
                case T.Speed:
                    return ("bolt", "n.speed", "nd.speed", 0f, s => s.attackInterval = CI(s.attackInterval * 0.90f));
                // 치명타는 "가끔 터지는 대박"이 아니라 "자주 조금 더"로 — 확률은 올리고 배율은 크게 낮췄다.
                // (예전엔 풀트리에서 확률 32% / 배율 ×3.75 라, 안 터지면 못 잡는 운빨 구조였다.)
                case T.CritC:
                    return ("crosshair", "n.critc", "nd.critc", 0f, s => s.critChance = CC(s.critChance + 0.06f));
                case T.CritX:
                    return ("star", "n.critx", "nd.critx", 0f, s => s.critMult += 0.06f);
                case T.Range:
                    return ("ring", "n.range", "nd.range", 0f, s => s.cursorRadius += 0.12f);
                case T.Gold:
                    return ("coin", "n.gold", "nd.gold", 0f, s => s.goldMultPercent += 8f);
                case T.Time:
                    return ("clock", "n.time", "nd.time", 0f, s => s.bonusTimeSec = CT(s.bonusTimeSec + 4f));
                case T.Spawn:
                    return ("chevrons", "n.spawn", "nd.spawn", 0f, s => s.spawnIntervalMult = CS(s.spawnIntervalMult * 0.93f));
                case T.SCount:
                    return ("chevrons", "n.scount", "nd.scount", 0f, s => s.spawnCount = CN(s.spawnCount + 1));
                case T.Skip:
                    int w = (++skipIdx) * 5;
                    return ("skip", "n.skip", "nd.skip", w, s => s.startWave = Mathf.Max(s.startWave, w));
                case T.Auto:
                default:
                    return ("bolt", "n.auto", "nd.auto", 0f, s => s.autoAttack = true);
            }
        }

        // ============================================================
        // 티어별 노드 구성표 — 티어마다 "무슨 노드가 몇 개"인지 못 박는다. 합계는 항상 NodesPerTier.
        //   예전엔 가지마다 타입 순환(cyc)에 맡겼는데, BFS 순서 때문에 구성이 티어별로 제멋대로 쏠렸다:
        //   티어2에 공격력 0개, 티어6은 배수(이미 상한)+골드, 티어7은 범위+배수(상한)뿐 —
        //   즉 마지막 40노드가 전투력을 1도 안 올려서 7·8지역에서 화력이 그대로였다(8지역 보스 81초).
        //        Flat Mult Speed CritC CritX Range Gold Time Spawn SCount Auto
        static readonly int[,] TierMix =
        {
            { 5, 3, 4, 1, 1, 2, 2, 1, 0, 0, 1 },   // t0 — 자동공격 해금 + 공속을 앞당겨 초반 "때리는 맛"
            { 4, 3, 3, 1, 1, 3, 3, 1, 1, 0, 0 },   // t1
            { 4, 3, 3, 1, 1, 3, 3, 1, 1, 0, 0 },   // t2
            { 4, 3, 2, 1, 1, 3, 3, 1, 1, 1, 0 },   // t3
            { 5, 3, 1, 1, 1, 3, 3, 1, 1, 1, 0 },   // t4
            { 5, 4, 1, 1, 1, 3, 3, 0, 1, 1, 0 },   // t5
            { 5, 4, 1, 1, 1, 3, 3, 1, 1, 0, 0 },   // t6
            { 5, 4, 1, 1, 1, 3, 3, 1, 0, 1, 0 },   // t7 — 마지막 티어도 반드시 화력이 오른다
        };
        static readonly T[] MixOrder =
        { T.Flat, T.Mult, T.Speed, T.CritC, T.CritX, T.Range, T.Gold, T.Time, T.Spawn, T.SCount, T.Auto };

        // 한 티어(20칸) 안에서 같은 타입이 한 가지에 몰리지 않게 고르게 흩뿌린다.
        // 노드는 가지들에 라운드로빈으로 배분되므로, 칸 순서를 고르게 섞으면 가지 구성도 골고루 섞인다.
        static T[] BuildTierTypes(int tier)
        {
            var res = new T[NodesPerTier];
            var used = new bool[NodesPerTier];
            for (int k = 0; k < MixOrder.Length; k++)
            {
                int c = TierMix[tier, k];
                for (int i = 0; i < c; i++)
                {
                    int want = Mathf.Clamp(Mathf.FloorToInt((i + 0.5f) * NodesPerTier / c), 0, NodesPerTier - 1);
                    int p = want;
                    for (int step = 1; used[p]; step++) p = (want + step) % NodesPerTier;
                    used[p] = true;
                    res[p] = MixOrder[k];
                }
            }
            // 자동공격은 코어 바로 옆(십자 첫 노드)이어야 초반에 바로 뚫린다
            if (tier == 0)
                for (int i = 0; i < NodesPerTier; i++)
                    if (res[i] == T.Auto) { res[i] = res[0]; res[0] = T.Auto; break; }
            return res;
        }

        static T[][] typeTable;
        static T TypeOf(int index)
        {
            if (typeTable == null)
            {
                typeTable = new T[Tiers][];
                for (int t = 0; t < Tiers; t++) typeTable[t] = BuildTierTypes(t);
            }
            int tier = index / NodesPerTier;
            if (tier >= Tiers) tier = Tiers - 1;          // 무한 확장 대비 — 마지막 티어 구성을 재사용
            return typeTable[tier][index % NodesPerTier];
        }

        // 가지 하나가 뻗는 상태. 가운데 십자에서 시작해 몇 노드마다 2~3갈래로 갈라진다.
        class Limb
        {
            public Vector2 dir;    // 단위벡터 (DrawTree 가 spacing 을 곱한다)
            public string parent;  // 직전 노드 id
            public int depth;      // 이 가지 계통에서의 깊이
            public int gen;        // 몇 번째 갈래치기인지 (0 = 십자 본가지)
        }

        static Vector2 Dir(float angleDeg) => Rot(new Vector2(0f, -1f), angleDeg).normalized;

        // 가운데 3×3 십자(상·하·좌·우 4갈래)에서 시작 → 2~3갈래로 갈라지며 왕관처럼 퍼진다.
        //   위·오른쪽 갈래 = 전투 위주, 아래·왼쪽 갈래 = 유틸 위주. 왼쪽 십자 첫 노드 = 자동공격 해금.
        public static List<UpgradeNode> BuildAll()
        {
            var L = new List<UpgradeNode>();
            L.Add(new UpgradeNode(RootId, null, Vector2.zero, "hex", "n.core", 0, 0, "nd.core", 0f, s => s.flatBonus += 8f));

            int total = NodesPerTier * Tiers;   // 160

            var limbs = new List<Limb>
            {
                new Limb { dir = Dir(0f),   parent = RootId },
                new Limb { dir = Dir(90f),  parent = RootId },
                new Limb { dir = Dir(180f), parent = RootId },
                new Limb { dir = Dir(270f), parent = RootId },
            };

            int added = 0, guard = 0, skipIdx = 0;
            var next = new List<Limb>();
            while (added < total && guard++ < 6000)
            {
                for (int li = 0; li < limbs.Count && added < total; li++)
                {
                    var lm = limbs[li];
                    int tier = added / NodesPerTier;
                    T type = TypeOf(added);          // 타입은 티어 구성표가 정한다(가지 순환 아님)
                    var e = Effect(type, tier, ref skipIdx);
                    string id = "u" + added;
                    L.Add(new UpgradeNode(id, lm.parent, lm.dir, e.icon, e.label, Cost(tier), tier, e.desc, e.descArg, e.apply));
                    lm.parent = id;
                    lm.depth++;
                    added++;

                    // 갈래치기: 십자 2노드 뒤 첫 분기, 그 뒤 3노드마다. gen0→2갈래, gen1→3갈래, 이후 2갈래.
                    bool fork = lm.depth == 2 || (lm.depth > 2 && (lm.depth - 2) % 3 == 0);
                    if (fork && lm.gen < 4)
                    {
                        int forks = lm.gen == 0 ? 2 : lm.gen == 1 ? 3 : 2;
                        float spread = lm.gen == 0 ? 42f : lm.gen == 1 ? 56f : 38f;
                        for (int f = 0; f < forks; f++)
                        {
                            float off = forks == 1 ? 0f : Mathf.Lerp(-spread, spread, f / (float)(forks - 1));
                            next.Add(new Limb { dir = Rot(lm.dir, off).normalized, parent = lm.parent, depth = lm.depth, gen = lm.gen + 1 });
                        }
                    }
                    else next.Add(lm);
                }
                var swap = limbs; limbs = next; next = swap; next.Clear();
                if (limbs.Count == 0) break;
            }
            return L;
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
                    (s, lv) => s.critChance = Mathf.Min(0.75f, s.critChance + 0.04f * lv)),
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
