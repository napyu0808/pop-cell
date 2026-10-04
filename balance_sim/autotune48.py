# -*- coding: utf-8 -*-
"""
round48 자동 튜닝 — "초반 고통" 완화.

문제(round47 측정, 타워 포함):
    지역      S1   S2   S3   S4   S5   S6   S7   S8
    판수       6   76   85  116   99   47   32   16     <- 2~4지역에서 같은 판을 수십 번
    구매율    82%  78%  65%  44%  32%   8%   4%   0%    <- 후반엔 노드를 안 사고 지나간다
  1지역 2판 -> 2지역 20판(실측 환산)의 절벽이 초반 고통의 정체다.
  동시에 후반은 싱겁다 — 노드를 거의 안 사고 깨진다(= 성장 쾌감이 없다).

목표: 판수를 "완만하게 증가"하는 곡선으로 재분배한다. 총합은 거의 그대로라
      전체 플레이타임(실측 ≈ 5시간)은 유지되면서 초반만 가벼워진다.
    RUNS_T = [8, 23, 30, 38, 53, 68, 84, 99]   (실측 환산 2·6·8·10·14·18·22·26판)
  그리고 각 지역에서 그 티어 노드를 대부분 사고 넘어가게(구매율 75~95%).

레버
  (1) TIER_COST[k]  — 판수를 직접 좌우한다(판수 ≈ 사야 할 총액 / 판당 수입).
  (2) 지역 체력 배율 — 구매율을 좌우한다(세면 더 많이 사야 깨진다). 골드 배율은
      beta 공식으로 체력에서 다시 계산되므로 수입도 같이 따라온다.

결과는 매 반복 출력 + autotune48_result.json 에 저장.
"""
import json, math, statistics
from multiprocessing import Pool
import gamedata as G
import tune42 as T

RUNS_T = [8, 23, 30, 38, 53, 68, 84, 99]      # 지역별 목표 판수(시뮬). 실측 ≈ /3.8
PROF_LO, PROF_HI = 0.75, 0.95                 # 그 티어 노드 구매율 목표대
BETA = 1.2
ITERS = 14


def evaluate(cfg):
    with Pool(len(T.SEEDS)) as p:
        res = p.map(T._one, [(s, cfg) for s in T.SEEDS])
    n = min(len(r[0]) for r in res)
    mins = [statistics.mean(r[0][j] for r in res) for j in range(n)]
    runs = [statistics.mean(r[1][j] for r in res) for j in range(n)]
    prof = [statistics.mean(r[2][j] for r in res) for j in range(n)]
    walls = [r[6] for r in res if r[6] is not None]
    return mins, runs, prof, walls


def score(mins, runs, prof, walls):
    """목표에서 얼마나 벗어났는지 — 작을수록 좋다."""
    e = 0.0
    for k in range(len(runs)):
        e += abs(math.log(max(0.5, runs[k]) / RUNS_T[k]))
        p = prof[k]
        if p == p:
            if p < PROF_LO: e += (PROF_LO - p) * 2.0
            elif p > PROF_HI: e += (p - PROF_HI) * 1.0
    e += 6 * len(walls)
    e += 3 * (8 - len(runs))      # 못 깬 지역
    return e


def main():
    cost = [float(c) for c in G.TIER_COST]
    hpm = [1.0] * 8
    best = None
    for it in range(ITERS):
        cfg = {"cost": [max(5, int(round(c))) for c in cost],
               "rep": list(G.REP_COST), "hp": list(hpm), "beta": BETA}
        mins, runs, prof, walls = evaluate(cfg)
        e = score(mins, runs, prof, walls)
        print("it%2d  err %5.2f  runs %s  prof %s  mins %4.0f  walls %s"
              % (it, e, [round(r) for r in runs],
                 [("%.0f" % (p * 100)) if p == p else "-" for p in prof],
                 sum(mins), walls), flush=True)
        if best is None or e < best[0]:
            best = (e, dict(cfg), mins, runs, prof, walls)
        if it == ITERS - 1:
            break

        if walls:
            w = min(walls)
            hpm[w] *= 0.72           # 벽 — 그 지역을 못 깬다
            continue

        # (1) 가격 — 판수를 목표로. 판수는 가격에 거의 비례하므로 비율 보정(감쇠 0.6).
        for k in range(min(8, len(runs))):
            ratio = RUNS_T[k] / max(1.0, runs[k])
            cost[k] *= ratio ** 0.6
        # (2) 체력 — 구매율을 목표대로. 덜 사고 깨면 세게, 다 사고도 빠듯하면 약하게.
        for k in range(min(8, len(prof))):
            p = prof[k]
            if p != p:
                continue
            if p < PROF_LO:   hpm[k] *= 1.22
            elif p > PROF_HI: hpm[k] *= 0.90

    e, cfg, mins, runs, prof, walls = best
    print("\n=== 최선 (err %.2f) ===" % e)
    print("TIER_COST =", cfg["cost"])
    print("HP 배율   =", [round(v, 3) for v in cfg["hp"]])
    print("판수      =", [round(r) for r in runs], " 목표", RUNS_T)
    print("구매율    =", [("%.0f%%" % (p * 100)) if p == p else "-" for p in prof])
    print("시뮬분    =", [round(m) for m in mins], " 합 %.0f -> 실측 %.0f분 (%.1f시간)"
          % (sum(mins), sum(mins) / 3.8, sum(mins) / 3.8 / 60))
    json.dump({"cost": cfg["cost"], "hp": cfg["hp"], "beta": BETA,
               "runs": runs, "prof": prof, "mins": mins},
              open("autotune48_result.json", "w"), indent=1)


if __name__ == "__main__":
    main()
