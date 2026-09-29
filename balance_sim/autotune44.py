# -*- coding: utf-8 -*-
"""
round44 자동 튜닝 — tune42 의 측정을 반복하면서 세 가지를 번갈아 맞춘다.
  (1) 지역 2~6 체력(잡몹·보스 같이): 지역 k 클리어 때 티어 k-1 구매율이 40~60% 가 되게
  (2) 티어 1~5 가격: 지역 2~6 시간(시뮬-분)이 목표에 맞게
  (3) 반복 강화 가격(6개 공통 배율) + 지역 7·8 체력: 지역 7·8 시간이 목표에 맞게
  골드 배율은 매번 beta 공식으로 체력에서 다시 계산된다(체력↑ → 골드↑).
  결과는 매 반복 출력 + autotune44_result.json 에 저장.
"""
import json, statistics
from multiprocessing import Pool
import gamedata as G
import tune42 as T

TARGET = [5, 25, 50, 90, 130, 190, 260, 390]     # 시뮬-분, 합 1140 ≈ 실측 5시간
BETA = 1.2

base_hp0, base_boss = list(G.HP0), list(G.BOSSHP)


def evaluate(cfg):
    with Pool(len(T.SEEDS)) as p:
        res = p.map(T._one, [(s, cfg) for s in T.SEEDS])
    n = min(len(r[0]) for r in res)
    mins = [statistics.mean(r[0][j] for r in res) for j in range(n)]
    prof = [statistics.mean(r[2][j] for r in res) for j in range(n)]
    up = [None] + [statistics.mean(r[3][j] for r in res) / max(1e-9, statistics.mean(r[4][j - 1] for r in res)) for j in range(1, n)]
    walls = [r[6] for r in res if r[6] is not None]
    return mins, prof, up, walls


def main():
    cost = list(G.TIER_COST)
    rep = float(G.REP_COST[0])
    hpm = [1.0] * 8
    best = None
    for it in range(14):
        cfg = {"cost": [int(round(c)) for c in cost], "rep": [int(round(rep))] * 6,
               "hp": hpm, "beta": BETA}
        mins, prof, up, walls = evaluate(cfg)
        tot = sum(mins)
        err = sum(abs(__import__("math").log(max(0.5, m) / t)) for m, t in zip(mins, TARGET[:len(mins)])) + (5 if walls else 0)
        print("iter %2d  tot %5.0f  mins %s  prof %s  up %s  walls %s" % (
            it, tot, [round(m) for m in mins], [("%.0f" % (p * 100)) if p == p else "-" for p in prof],
            ["-" if u is None else "%.1f" % u for u in up], walls), flush=True)
        if best is None or err < best[0]:
            best = (err, dict(cfg), mins, prof, up, walls)
        if walls:
            # 벽 — 해당 지역 체력을 낮추고 다시
            w = min(walls)
            hpm[w] *= 0.75
            continue
        # (1) 체력: 지역 2~6 (index 1..5) 구매율
        for k in range(1, 6):
            if k < len(prof) and prof[k] == prof[k]:
                if prof[k] < 0.38: hpm[k] *= 1.35
                elif prof[k] > 0.65: hpm[k] *= 0.8
        # (2) 가격: 티어 k 는 지역 k+1 시간으로
        for k in range(1, 6):
            if k < len(mins):
                cost[k] *= (TARGET[k] / max(0.5, mins[k])) ** 0.8
        # (3) 반복 강화 가격 ← 지역 7·8 합 / 지역 7·8 체력은 비율 유지(7:8 시간 비)
        if len(mins) >= 8:
            late_t, late_m = TARGET[6] + TARGET[7], mins[6] + mins[7]
            rep *= (late_t / max(0.5, late_m)) ** 0.8
            r7 = (mins[6] / max(0.5, mins[7])) / (TARGET[6] / TARGET[7])
            if r7 < 0.7: hpm[6] *= 1.25
            elif r7 > 1.4: hpm[6] *= 0.85
        cost = [min(c, 1.9e9) for c in cost]
        rep = min(rep, 1.9e9)
    err, cfg, mins, prof, up, walls = best
    print("\nBEST err %.2f  tot %.0f  cfg %s" % (err, sum(mins), json.dumps(cfg)))
    json.dump({"cfg": cfg, "mins": mins, "prof": prof, "up": up, "walls": walls},
              open("autotune44_result.json", "w"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
