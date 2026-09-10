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
        public readonly string id, parentId, label, desc, icon;
        public readonly Vector2 dir;        // 부모로부터의 방향 (스크린 좌표, 위 = -Y)
        public readonly int cost;
        public readonly int tier;
        public readonly Action<Stats> apply;

        public UpgradeNode(string id, string parentId, Vector2 dir, string icon,
                           string label, int cost, int tier, string desc, Action<Stats> apply)
        {
            this.id = id; this.parentId = parentId; this.dir = dir; this.icon = icon;
            this.label = label; this.cost = cost; this.tier = tier; this.desc = desc; this.apply = apply;
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
            40, 240, 1300, 5200, 18000, 58000, 160000, 420000
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

        static (string icon, string label, string desc, Action<Stats> apply) Effect(T t, int d, ref int skipIdx)
        {
            switch (t)
            {
                case T.Flat:
                    int fa = 12 + d * 5;
                    return ("sword", "공격력", $"고정 공격력 +{fa}", s => s.flatBonus += fa);
                case T.Mult:
                    return ("mult", "공격 배수", "공격력 ×1.11배", s => s.multBucketPercent = CM(s.multBucketPercent + 11f));
                case T.Speed:
                    return ("bolt", "공격 속도", "타격 간격 ×0.94", s => s.attackInterval = CI(s.attackInterval * 0.94f));
                case T.CritC:
                    return ("crosshair", "치명 확률", "치명타 확률 +4%p", s => s.critChance = CC(s.critChance + 0.04f));
                case T.CritX:
                    return ("star", "치명 배수", "치명타 배수 +0.25", s => s.critMult += 0.25f);
                case T.Range:
                    return ("ring", "공격 범위", "커서 타격 범위 +0.12", s => s.cursorRadius += 0.12f);
                case T.Gold:
                    return ("coin", "골드 획득", "처치 시 골드 +8%", s => s.goldMultPercent += 8f);
                case T.Time:
                    return ("clock", "시작 시간", "시작 제한시간 +4초", s => s.bonusTimeSec = CT(s.bonusTimeSec + 4f));
                case T.Spawn:
                    return ("chevrons", "소환 가속", "적 소환 간격 ×0.93", s => s.spawnIntervalMult = CS(s.spawnIntervalMult * 0.93f));
                case T.SCount:
                    return ("chevrons", "동시 소환", "동시 소환 마릿수 +1", s => s.spawnCount = CN(s.spawnCount + 1));
                case T.Skip:
                    int w = (++skipIdx) * 5;   // 5, 10, 15, 20
                    return ("skip", "웨이브 스킵", $"웨이브 {w}부터 시작", s => s.startWave = Mathf.Max(s.startWave, w));
                case T.Auto:
                default:
                    return ("bolt", "자동 공격", "커서 호버 자동 공격 해금 — 이 노드 전엔 클릭으로 공격",
                            s => s.autoAttack = true);
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
            new Branch { prefix = "ub", angle = 165f, cycle = new[] { T.Time,  T.Spawn, T.Gold,  T.Skip  } },
            new Branch { prefix = "uc", angle = 195f, cycle = new[] { T.Gold,  T.Time,  T.SCount,T.Range } },
            new Branch { prefix = "ud", angle = 225f, cycle = new[] { T.Spawn, T.Gold,  T.Range, T.Time  } },
        };

        public static List<UpgradeNode> BuildAll()
        {
            var L = new List<UpgradeNode>();
            L.Add(new UpgradeNode(RootId, null, Vector2.zero, "hex", "코어", 0, 0, "고정 공격력 +8", s => s.flatBonus += 8f));

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
                    L.Add(new UpgradeNode(id, last[b], dir[b], e.icon, e.label, Cost(tier), tier, e.desc, e.apply));
                    last[b] = id;
                    added++;
                }
            }
            return L;
        }
    }
}
