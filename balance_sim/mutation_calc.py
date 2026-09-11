# -*- coding: utf-8 -*-
"""
변이(Ascension) 단계별 지역 클리어 시간 + 총 플레이타임 추정.

  모든 상수는 gamedata.py 에서 온다(= 출시 중인 C# 과 일치, gamedata.self_check 로 검증).
  플레이어 모델: 지역을 반복 시도하면서, 살 수 있는 '경계 노드'(부모가 뚫린 것)를
  싼 것부터 사 나간다 — round38 에서 일괄구매를 없앴으므로 시뮬도 한 칸씩 산다.

  SIM_TO_REAL = 3.8 (round24 캘리브레이션: 실측 45분 vs 시뮬 171분).
"""
import statistics
import gamedata as G
from sim import run_once
from prog import AIM, GOLD_FIX

SHOP = 15.0           # 판 사이 상점/트리에서 쓰는 시간(초)
DT = 1 / 40.0
SIM_TO_REAL = 3.8


def run_level(level, seed=0, max_runs=400):
    """변이 `level` 에서 1지역부터 8지역까지. (지역별 분, 지역별 시도수, 벽 지역) 반환."""
    nodes = G.build()
    by_id = {n.id: n for n in nodes}
    bought = {"root"}
    stats = G.new_stats()
    stats.flatBonus += G.ROOT_FLAT

    gold = 0
    region_min, region_runs = [], []

    for stage in range(8):
        w = G.wave_cfg(stage, asc=level)
        tier_open = stage + 1            # 지역 N 클리어 -> tier N 해금 => 지금은 0..stage 까지
        secs, runs, won = 0.0, 0, False

        while runs < max_runs:
            r = run_once(stats, w,
                         seed=seed * 99991 + level * 7919 + stage * 61 + runs * 11,
                         dt=DT, **AIM)
            gold += round(r["gold"] * GOLD_FIX)
            secs += r["elapsed"]
            runs += 1
            if r["won"]:
                won = True
                break
            secs += SHOP

            # 경계 노드만, 싼 것부터 — 더 못 살 때까지
            while True:
                cand = [n for n in nodes
                        if n.id not in bought
                        and n.parent in bought
                        and n.tier < tier_open
                        and n.cost <= gold]
                if not cand:
                    break
                pick = min(cand, key=lambda n: (n.cost, n.id))
                gold -= pick.cost
                bought.add(pick.id)
                pick.apply(stats)

        region_min.append(secs / 60.0)
        region_runs.append(runs)
        if not won:
            return region_min, region_runs, stage      # 벽 — 이후 지역은 의미 없음
    return region_min, region_runs, None


def main():
    levels = [0, 1, 2, 3, 4, 5]
    seeds = [0, 1, 2]

    if not G.self_check(verbose=False):
        print("!!! gamedata 가 C# 과 어긋납니다 — gamedata.self_check() 를 먼저 확인하세요.\n")

    print("변이 단계별 지역 클리어 시간 (시뮬-분, seed 평균).  실측 추정 = 시뮬 / %.1f" % SIM_TO_REAL)
    print("            " + "  ".join("S%d" % (i + 1) for i in range(8)) + "   | 누적(시뮬) | 누적(실측추정)")

    rows = []
    for lv in levels:
        per_region = [[] for _ in range(8)]
        walls = []
        for sd in seeds:
            rm, rr, wall = run_level(lv, seed=sd)
            for i, v in enumerate(rm):
                per_region[i].append(v)
            walls.append(wall)

        # 모든 seed 가 도달한 지역까지만 평균 (앞에서부터, 하나라도 비면 거기서 끊는다)
        n_common = 0
        for i in range(8):
            if len(per_region[i]) == len(seeds):
                n_common = i + 1
            else:
                break
        avg = [statistics.mean(per_region[i]) for i in range(n_common)]
        cum = sum(avg)
        real = cum / SIM_TO_REAL
        hit = [w for w in walls if w is not None]
        note = ""
        if hit:
            note = "  *** 벽: S%d 에서 %d/%d seed 가 400판 안에 못 깸 ***" % (min(hit) + 1, len(hit), len(seeds))
        print("변이%d: " % lv + "  ".join("%6.1f" % v for v in avg)
              + "      " * (8 - n_common)
              + "  | %8.1f | %8.1f분" % (cum, real) + note, flush=True)
        rows.append((lv, real, hit, n_common))

    print("\n=== 실측 추정 요약 ===")
    total = 0.0
    for lv, real, hit, n_common in rows:
        if hit:
            print("변이%d: S%d 에서 벽 — 0메타 기준 %d/8 지역까지만 유효" % (lv, min(hit) + 1, n_common))
            continue
        total += real
        print("변이%d 8지역 클리어: %.0f분 (%.1f시간)  |  변이0~%d 누적: %.0f분 (%.1f시간)"
              % (lv, real, real / 60, lv, total, total / 60), flush=True)


if __name__ == "__main__":
    main()
