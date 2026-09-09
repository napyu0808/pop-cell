using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlackholeGame
{
    // 노드 하나. 부모가 뚫려야 뚫린다. 위치는 부모 + dir * spacing.
    // 효과 텍스트는 노드에 안 보이고, 마우스 오버 시 툴팁으로만 표시된다.
    public class UpgradeNode
    {
        public readonly string id, parentId, label, desc, icon;
        public readonly Vector2 dir;        // 부모로부터의 방향 (스크린 좌표, 위 = -Y)
        public readonly int cost;
        public readonly Action<Stats> apply;

        public UpgradeNode(string id, string parentId, Vector2 dir, string icon,
                           string label, int cost, string desc, Action<Stats> apply)
        {
            this.id = id; this.parentId = parentId; this.dir = dir; this.icon = icon;
            this.label = label; this.cost = cost; this.desc = desc; this.apply = apply;
        }
        public bool IsRoot => parentId == null;
    }

    // ============================================================
    // 스킬트리 — 루트(코어)에서 9갈래가 "*" 모양으로 전방향 방사.
    //   갈래마다 15칸씩 길게. 한 칸당 수치는 작게. 관문/허브 같은 빈 노드는 없음.
    //   비용은 진행할수록 급격히 비싸짐.
    // ============================================================
    public static class UpgradeTree
    {
        public const string RootId = "root";

        static float CM(float v) => Mathf.Min(200f, v);   // 배수 상한 200%p
        static float CC(float v) => Mathf.Min(0.75f, v);  // 치명 확률 상한
        static float CI(float v) => Mathf.Max(0.15f, v);  // 타격 간격 하한
        static float CT(float v) => Mathf.Min(55f, v);    // 시간 버퍼 상한 (시간 노드 15칸 × +3 = 45)
        static float CS(float v) => Mathf.Max(0.25f, v);  // 소환 간격 배율 하한
        static int   CN(int v)   => Mathf.Clamp(v, 2, 5); // 동시 소환 마릿수 (2~5)

        // 노드 비용 — 공식이 아니라 표다.
        //   런당 수입 곡선 G(d)를 시뮬레이터로 실측한 뒤 C(d) = ρ(d)·G(d-1)/9 로 역산한 값.
        //   (9갈래가 같은 깊이면 값이 같으므로, 한 "링"을 뚫는 데 드는 판 수 = 9·C(d)/G(d-1) = ρ(d).)
        //   ρ를 1.0 → 3.4로 완만히 올려 잡아서 초반은 한 판에 한 링, 후반은 두세 판에 한 링이 된다.
        //   a·b^depth 같은 지수 공식으로는 이게 불가능하다 — 수입 곡선이 지수형이 아니라
        //   초반 1.8배/링 → 후반 1.05배/링(웨이브 40 도달 후 정체)로 꺾이기 때문.
        //   결과: 트리 전체 $3.3M, 초견 클리어 30~50분(플레이 방식에 따라).
        static readonly int[] CostTable =
        {
            8,     35,    95,    190,   440,   900,   1400,  2100,  3500,  5200,   // d1–10
            8000,  12000, 17500, 24000, 35000, 42000, 45000, 51000, 56000, 62000   // d11–20
        };
        static int Cost(int depth) =>
            depth >= 1 && depth <= CostTable.Length
                ? CostTable[depth - 1]
                : Mathf.RoundToInt(CostTable[CostTable.Length - 1]
                                   * Mathf.Pow(1.12f, depth - CostTable.Length));

        static Vector2 Rot(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        const int CHAIN = 20;   // 갈래당 20칸 (9갈래 × 20 + 코어 = 181노드)

        public static List<UpgradeNode> BuildAll()
        {
            var L = new List<UpgradeNode>();

            L.Add(new UpgradeNode(RootId, null, Vector2.zero, "hex", "코어", 0, "고정 공격력 +3", s => s.flatBonus += 3f));

            void Chain(string prefix, int angleDeg, string icon, string label,
                       Func<int, (string desc, Action<Stats> apply)> step, Func<int, string> iconFor = null)
            {
                Vector2 dir = Rot(new Vector2(0f, -1f), angleDeg).normalized;
                string p = RootId;
                for (int j = 0; j < CHAIN; j++)
                {
                    var s = step(j);
                    string id = prefix + j;
                    L.Add(new UpgradeNode(id, p, dir, iconFor != null ? iconFor(j) : icon, label, Cost(j + 1), s.desc, s.apply));
                    p = id;
                }
            }

            // 9갈래 = 40° 간격으로 전방향 (* 모양). 갈래당 20칸.
            // 전투: 풀강 시 30웨이브까지 원킬 · 40웨이브 + 100만 보스가 도전 구간이 되도록.
            Chain("cf", 0,   "sword",     "공격력",     j => ($"고정 공격력 +{6 + j * 3}",   s => s.flatBonus += 6 + j * 3));       // 풀강 +690
            Chain("cm", 40,  "mult",      "공격 배수",  j => ("공격력 ×1.09배",              s => s.multBucketPercent = CM(s.multBucketPercent + 9f)));  // 풀강 +180%p (×2.8)
            Chain("cs", 80,  "bolt",      "공격 속도",  j => ("타격 간격 ×0.95",              s => s.attackInterval = CI(s.attackInterval * 0.95f)));    // 풀강 ×0.36
            Chain("cc", 120, "crosshair", "치명 확률",  j => ("치명타 확률 +3%p",             s => s.critChance = CC(s.critChance + 0.03f)));            // 풀강 +60%p
            Chain("cx", 160, "star",      "치명 배수",  j => ("치명타 배수 +0.2",             s => s.critMult += 0.2f));                                 // 풀강 +4.0 (→6.0)
            Chain("cr", 200, "ring",      "공격 범위",  j => ("커서 타격 범위 +0.10",         s => s.cursorRadius += 0.10f));                            // 풀강 +2.0 (→2.45)
            Chain("ug", 240, "coin",      "골드 획득",  j => ("처치 시 골드 +6%",             s => s.goldMultPercent += 6f));                            // 풀강 +120%p
            // 시간 갈래 20칸 = 시작 시간 +3초(15칸) + 웨이브 스킵 5칸(j=3,7,11,15,19 → 5/10/15/20/25부터 시작)
            Chain("ut", 280, "clock", "시간 / 스킵", j =>
            {
                switch (j)
                {
                    case 3:  return ("웨이브 5부터 시작",  (Action<Stats>)(s => s.startWave = Mathf.Max(s.startWave, 5)));
                    case 7:  return ("웨이브 10부터 시작", s => s.startWave = Mathf.Max(s.startWave, 10));
                    case 11: return ("웨이브 15부터 시작", s => s.startWave = Mathf.Max(s.startWave, 15));
                    case 15: return ("웨이브 20부터 시작", s => s.startWave = Mathf.Max(s.startWave, 20));
                    case 19: return ("웨이브 25부터 시작", s => s.startWave = Mathf.Max(s.startWave, 25));
                    default: return ("시작 제한시간 +3초", s => s.bonusTimeSec = CT(s.bonusTimeSec + 3f));
                }
            }, j => (j == 3 || j == 7 || j == 11 || j == 15 || j == 19) ? "skip" : "clock");
            // 소환 갈래 20칸 = 소환 가속 17칸 + 동시 소환 +1 3칸(j=4,9,14).
            Chain("up", 320, "chevrons", "소환", j =>
            {
                if (j == 4 || j == 9 || j == 14)
                    return ("동시 소환 마릿수 +1", (Action<Stats>)(s => s.spawnCount = CN(s.spawnCount + 1)));
                return ("적 소환 간격 ×0.94", s => s.spawnIntervalMult = CS(s.spawnIntervalMult * 0.94f));
            });

            return L;
        }
    }
}
