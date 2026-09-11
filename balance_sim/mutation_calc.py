"""
변이(승천) 단계별 지역 클리어 시간 + 총 플레이타임 추정.
  round32 재밸런싱 반영: 공속 하한 0.06(기존 0.15), Speed 노드 x0.90(기존 0.94),
  StageConfig enemyHp0 x1.3 / bossHp x2.2 (round32), 변이 배율(round30):
  AscMobHpMul=1.15 / AscBossHpMul=1.08 / AscGoldMul=0.95, 복리.
  가정: 메타 강화 전혀 없이(0레벨) 8지역을 한 번에 쭉 오르는 "가장 빠른 경로" 기준
  (실제로는 환생 파밍으로 메타를 채우면 더 빨라짐 — 별도 표기).
  SIM_TO_REAL = 3.8 (round24 캘리브레이션: 실측 45분 vs 시뮬 171분, stage_calc.py 참고).
"""
import statistics, sys
from sim import Stats, run_once, WaveCfg
from tree import CfgV2, build_v2
from prog import AIM, GOLD_FIX

SHOP = 15.0
DT = 1 / 40.0
SIM_TO_REAL = 3.8

TIER_COST = [60, 700, 2300, 9000, 38000, 145000, 380000, 880000]
WAVES    = [4, 5, 7, 9, 11, 13, 15, 18]
TLIM     = [28, 34, 44, 54, 64, 74, 88, 108]
HP0      = [49, 107, 182, 224, 267, 312, 306, 260]
HPG      = [1.130, 1.136, 1.142, 1.148, 1.153, 1.158, 1.163, 1.168]
QUOTA0   = [4, 5, 6, 7, 8, 9, 10, 11]
SE       = [8, 9, 10, 11, 12, 13, 14, 15]
GOLDRATE = [0.50, 0.70, 0.82, 0.95, 1.05, 1.15, 1.30, 1.55]
BOSSHP0  = [4400, 17600, 55000, 149600, 385000, 902000, 1870000, 3960000]

# round35 재밸런싱: GameConfig.cs 의 AscMobHpMul/AscBossHpMul 을 1.15/1.08 -> 1.12/1.05 로 낮추고,
#   줄어든 만큼의 난이도는 GameManager 의 보스 패턴(치명 내성/가속 순간이동/무적 페이즈)으로 옮겼다.
#   이 시뮬은 경제(골드/체력)만 모델링하므로 패턴은 반영되지 않음 — 여기서 보는 건 "수치 벽"이 풀렸는지.
ASC_MOB, ASC_BOSS, ASC_GOLD = 1.12, 1.05, 0.95
GC0 = CfgV2().goldCurve


def _stats(nodes, ids):
    s = Stats()
    for n in nodes:
        if n.id in ids:
            n.apply(s)
    return s


def run_mutation_level(level, seed=0, max_runs=400):
    cfg = CfgV2(tier_cost=list(TIER_COST), interval_mul=0.90, interval_floor=0.06)
    nodes = build_v2(cfg)
    mob_mul = ASC_MOB ** level
    boss_mul = ASC_BOSS ** level
    gold_mul = ASC_GOLD ** level

    bought = {"root"}
    gold = 0
    region_min = []
    region_runs = []
    for idx in range(8):
        T = idx + 1
        hp0 = HP0[idx] * mob_mul
        bosshp = round(BOSSHP0[idx] * boss_mul)
        gr = GOLDRATE[idx] * gold_mul
        gc = [max(1, round(v * gr)) for v in GC0]
        w = WaveCfg(baseTimeLimit=TLIM[idx], hpGrowth=HPG[idx], baseEnemyHp=hp0,
                    totalWaves=WAVES[idx], bossHp=bosshp, baseQuota=QUOTA0[idx],
                    quotaSlope=2, goldCurve=gc)
        w.startEnemies = SE[idx]
        w.spawnBase = 0.55

        secs = 0.0
        runs = 0
        won = False
        while runs < max_runs:
            s = _stats(nodes, bought)
            r = run_once(s, w, seed=seed * 99991 + level * 7919 + T * 61 + runs * 11, dt=DT, **AIM)
            gold += round(r["gold"] * GOLD_FIX)
            secs += r["elapsed"]
            runs += 1
            if r["won"]:
                won = True
                break
            secs += SHOP
            while True:
                cand = [n for n in nodes if n.id not in bought and n.id != "root"
                        and n.parent in bought and n.tier < T and n.cost <= gold]
                if not cand:
                    break
                pick = min(cand, key=lambda n: (n.cost, n.chain))
                gold -= pick.cost
                bought.add(pick.id)
        region_min.append(secs / 60.0)
        region_runs.append(runs)
        if not won:
            # 그 tier 상한 안에서 max_runs 을 다 써도 못 깼다 — "더 파밍하면 언젠간 깬다"가 아니라
            # 살 수 있는 노드를 이미 다 산 상태에서도 못 깬 것일 수 있어 벽(wall)으로 보고 중단.
            return region_min, region_runs, idx
    return region_min, region_runs, None


def main():
    levels = [0, 1, 2, 3, 4, 5]
    seeds = [0, 1, 2]
    print("레벨별 지역 클리어 시간(시뮬-분, seed 평균) — SIM_TO_REAL=%.1f 로 나누면 실측 추정(분)" % SIM_TO_REAL)
    print("region:      " + "  ".join("S%d" % (i + 1) for i in range(8)) + "   | 누적(시뮬분) | 누적(실측분,÷3.8)")
    all_rows = []
    grand_total = 0.0
    for lv in levels:
        per_region = [[] for _ in range(8)]
        wall_hits = []
        for sd in seeds:
            rm, rr, wall = run_mutation_level(lv, seed=sd)
            for i, v in enumerate(rm):
                per_region[i].append(v)
            wall_hits.append(wall)
        # 모든 seed 가 공통으로 도달한 "지역 개수"(seed 개수 아님!) 까지만 평균 —
        # 어느 지역이라도 seed 하나가 벽에 부딪혀 값이 없으면 그 지역부터는 평균 신뢰 불가.
        n_common = 0
        for i in range(8):
            if len(per_region[i]) == len(seeds):
                n_common = i + 1
            else:
                break
        avg = [statistics.mean(per_region[i]) for i in range(n_common)]
        cum_sim = sum(avg)
        cum_real = cum_sim / SIM_TO_REAL
        wall_regions = [w for w in wall_hits if w is not None]
        wall_note = ""
        if wall_regions:
            wall_note = "  *** 벽: S%d 에서 %d/%d seed 가 %d회 시도 안에 못 깸(그 이후 지역 값 없음/무의미) ***" \
                        % (min(wall_regions) + 1, len(wall_regions), len(seeds), 400)
        row = "변이%d: " % lv + "  ".join("%6.1f" % v for v in avg) + \
              ("      " * (8 - n_common)) + "  | %8.1f | %8.1f" % (cum_sim, cum_real) + wall_note
        print(row, flush=True)
        all_rows.append((lv, avg, cum_sim, cum_real, wall_regions, n_common))

    print("\n=== 실측 추정(분) 요약 — '벽'이 있는 레벨은 거기서부터 사실상 불가능(0메타 기준) ===")
    for lv, avg, cum_sim, cum_real, wall_regions, n_common in all_rows:
        if wall_regions:
            print("변이%d: S%d 에서 벽 — 0메타로는 %d/8 지역까지만 유효(그 이후 누적 의미 없음)"
                  % (lv, min(wall_regions) + 1, n_common), flush=True)
            continue
        grand_total += cum_real
        print("변이%d 8지역 클리어 실측추정: %.0f분 (%.1f시간)  |  변이0~%d 누적: %.0f분 (%.1f시간)"
              % (lv, cum_real, cum_real / 60, lv, grand_total, grand_total / 60), flush=True)


if __name__ == "__main__":
    main()
