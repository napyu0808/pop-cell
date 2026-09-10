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

        static float CM(float v) => Mathf.Min(240f, v);   // 배수 상한
        static float CC(float v) => Mathf.Min(0.75f, v);  // 치명 확률 상한
        static float CI(float v) => Mathf.Max(0.15f, v);  // 타격 간격 하한 (오토마우스 방지 하한)
        static float CT(float v) => Mathf.Min(60f, v);    // 시작 시간 버퍼 상한
        static float CS(float v) => Mathf.Max(0.22f, v);  // 소환 간격 배율 하한
        static int   CN(int v)   => Mathf.Clamp(v, 2, 6); // 동시 소환 마릿수

        // tier별 노드 비용. balance_sim 으로 "tier T 풀강 → 스테이지 T 클리어" 곡선에 맞춰 재조정 예정.
        static readonly int[] TierCost =
        {
            45, 420, 1400, 5500, 20000, 75000, 200000, 520000
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

        static (string icon, string label, string desc, float descArg, Action<Stats> apply) Effect(T t, int d, ref int skipIdx)
        {
            switch (t)
            {
                case T.Flat:
                    int fa = 12 + d * 5;
                    return ("sword", "n.flat", "nd.flat", fa, s => s.flatBonus += fa);
                case T.Mult:
                    return ("mult", "n.mult", "nd.mult", 0f, s => s.multBucketPercent = CM(s.multBucketPercent + 11f));
                case T.Speed:
                    return ("bolt", "n.speed", "nd.speed", 0f, s => s.attackInterval = CI(s.attackInterval * 0.94f));
                case T.CritC:
                    return ("crosshair", "n.critc", "nd.critc", 0f, s => s.critChance = CC(s.critChance + 0.04f));
                case T.CritX:
                    return ("star", "n.critx", "nd.critx", 0f, s => s.critMult += 0.25f);
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

        struct Branch { public string prefix; public float angle; public T[] cycle; }

        static readonly Branch[] Branches =
        {
            // 전투 클러스터 — 위쪽으로 부채꼴 (angle 0 = 정위). 타입이 섞여서 뻗는다.
            new Branch { prefix = "ca", angle = -70f, cycle = new[] { T.Flat,  T.Mult,  T.Speed, T.CritC } },
            new Branch { prefix = "cb", angle = -35f, cycle = new[] { T.Mult,  T.CritX, T.Flat,  T.Speed } },
            new Branch { prefix = "cc", angle =   0f, cycle = new[] { T.Speed, T.Flat,  T.CritC, T.Range } },
            new Branch { prefix = "cd", angle =  35f, cycle = new[] { T.Flat,  T.Range, T.CritX, T.Mult  } },
            new Branch { prefix = "ce", angle =  70f, cycle = new[] { T.CritC, T.Mult,  T.Flat,  T.Speed } },
            // 유틸 클러스터 — 아래쪽으로 부채꼴. ua0 = 자동 공격 해금 (제일 먼저 닿는 유틸 노드).
            new Branch { prefix = "ua", angle = 135f, cycle = new[] { T.Auto,  T.Gold,  T.Time,  T.Range } },
            new Branch { prefix = "ub", angle = 165f, cycle = new[] { T.Time,  T.Spawn, T.Gold,  T.CritC } },
            new Branch { prefix = "uc", angle = 195f, cycle = new[] { T.Gold,  T.Time,  T.SCount,T.Range } },
            new Branch { prefix = "ud", angle = 225f, cycle = new[] { T.Spawn, T.Gold,  T.Range, T.Time  } },
        };

        public static List<UpgradeNode> BuildAll()
        {
            var L = new List<UpgradeNode>();
            L.Add(new UpgradeNode(RootId, null, Vector2.zero, "hex", "n.core", 0, 0, "nd.core", 0f, s => s.flatBonus += 8f));

            int total = NodesPerTier * Tiers;   // 160
            var dir = new Vector2[Branches.Length];
            var last = new string[Branches.Length];
            for (int b = 0; b < Branches.Length; b++)
            {
                dir[b] = Rot(new Vector2(0f, -1f), Branches[b].angle).normalized;
                last[b] = RootId;
            }

            int skipIdx = 0;
            int added = 0;
            // 너비 우선 — 깊이 0을 모든 갈래에 먼저, 그다음 깊이 1 … 그래서 tier 0 = 안쪽 링 전부
            for (int depth = 0; added < total; depth++)
            {
                for (int b = 0; b < Branches.Length && added < total; b++)
                {
                    var br = Branches[b];
                    T type = br.cycle[depth % br.cycle.Length];
                    if (type == T.Auto && depth > 0) type = T.Range;   // 자동 해금은 ua 첫 노드 1개만
                    var e = Effect(type, depth, ref skipIdx);
                    int tier = added / NodesPerTier;
                    string id = br.prefix + depth;
                    L.Add(new UpgradeNode(id, last[b], dir[b], e.icon, e.label, Cost(tier), tier, e.desc, e.descArg, e.apply));
                    last[b] = id;
                    added++;
                }
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
            public readonly int cost, maxLv;
            public readonly Action<Stats, int> apply;   // (baseStats, 레벨)
            public MetaNode(string id, string label, string desc, int cost, int maxLv, Action<Stats, int> apply)
            { this.id = id; this.label = label; this.desc = desc; this.cost = cost; this.maxLv = maxLv; this.apply = apply; }
        }

        public static List<MetaNode> BuildMeta()
        {
            return new List<MetaNode>
            {
                new MetaNode("m_dmg",   "m.dmg",   "md.dmg", 3, 10,
                    (s, lv) => s.multBucketPercent += 8f * lv),
                new MetaNode("m_spd",   "m.spd",   "md.spd",   4, 8,
                    (s, lv) => s.attackInterval = Mathf.Max(0.15f, s.attackInterval * Mathf.Pow(0.97f, lv))),
                new MetaNode("m_gold",  "m.gold",   "md.gold",     3, 10,
                    (s, lv) => s.goldMultPercent += 15f * lv),
                new MetaNode("m_range", "m.range",   "md.range",    5, 6,
                    (s, lv) => s.cursorRadius += 0.08f * lv),
                new MetaNode("m_crit",  "m.crit",   "md.crit",     5, 8,
                    (s, lv) => s.critChance = Mathf.Min(0.75f, s.critChance + 0.03f * lv)),
                new MetaNode("m_start", "m.start", "md.start",  4, 10,
                    (s, lv) => { /* 시작 골드는 GameManager 에서 metaLv 로 처리 */ }),
                new MetaNode("m_auto",  "m.auto", "md.auto", 6, 1,
                    (s, lv) => { if (lv > 0) s.autoAttack = true; }),
            };
        }
    }
}
