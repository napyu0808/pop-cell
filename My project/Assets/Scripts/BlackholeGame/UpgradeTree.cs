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
        // 2시간 풀클리어 목표 (balance_sim/stage_calc 검증: 시뮬 ~430분, 실측 배율 ÷3.8 ≈ 113분).
        static readonly int[] TierCost =
        {
            60, 700, 2300, 9000, 38000, 145000, 380000, 880000
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

        // 가지 하나가 뻗는 상태. 가운데 십자에서 시작해 몇 노드마다 2~3갈래로 갈라진다.
        class Limb
        {
            public Vector2 dir;    // 단위벡터 (DrawTree 가 spacing 을 곱한다)
            public T[] cyc;        // 이 가지의 노드 타입 순환
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
                new Limb { dir = Dir(0f),   cyc = new[]{ T.Flat, T.Mult,  T.Speed,  T.CritC }, parent = RootId },
                new Limb { dir = Dir(90f),  cyc = new[]{ T.Mult, T.CritX, T.Flat,   T.Speed }, parent = RootId },
                new Limb { dir = Dir(180f), cyc = new[]{ T.Gold, T.Time,  T.Spawn,  T.Range }, parent = RootId },
                new Limb { dir = Dir(270f), cyc = new[]{ T.Auto, T.Gold,  T.SCount, T.Time  }, parent = RootId },
            };

            int added = 0, guard = 0, skipIdx = 0;
            var next = new List<Limb>();
            while (added < total && guard++ < 6000)
            {
                for (int li = 0; li < limbs.Count && added < total; li++)
                {
                    var lm = limbs[li];
                    T type = lm.cyc[lm.depth % lm.cyc.Length];
                    if (type == T.Auto && (lm.depth > 0 || added > 6)) type = T.Range;   // 자동공격은 십자 첫 노드 하나뿐
                    var e = Effect(type, lm.depth, ref skipIdx);
                    int tier = added / NodesPerTier;
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
                            next.Add(new Limb { dir = Rot(lm.dir, off).normalized, cyc = lm.cyc, parent = lm.parent, depth = lm.depth, gen = lm.gen + 1 });
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
