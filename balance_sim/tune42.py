# -*- coding: utf-8 -*-
"""
round42 튜닝 드라이버 — 변이0, 1~8지역 클리어 시간을 seed 여러 개로 병렬 측정.
  목표(사용자, 2026-09-27): 변이0 8지역 ≈ 5시간(실측) = 시뮬 ≈ 1140분 (SIM_TO_REAL 3.8).
  + 지역 k 를 깰 때 티어 k-1 이 어느 정도 필요해야 한다(새 트리가 세서 예전 체력표로는
    2티어 앞 노드로 깨지고, 마지막 2티어는 8지역까지 한 번도 안 샀다).

  사용: python tune42.py '{"cost":[...], "hp":[지역별 체력배율 8개], "flat":[...]}'
"""
import sys, json, statistics, time
from multiprocessing import Pool

import gamedata as G
import mutation_calc as M
from sim import run_once
from prog import AIM, GOLD_FIX

SEEDS = [0, 1, 2]


def setup(cfg):
    if cfg.get("cost"):
        G.TIER_COST[:] = cfg["cost"]
    if cfg.get("flat"):
        G.FLAT_BY_TIER[:] = cfg["flat"]
    if cfg.get("hp"):
        base0, baseB = list(G.HP0), list(G.BOSSHP)
        for i, m in enumerate(cfg["hp"]):
            G.HP0[i] = round(base0[i] * m)
            G.BOSSHP[i] = round(baseB[i] * m)
    if cfg.get("boss"):   # round43: 보스만 (보스가 안 맞던 버그가 고쳐지면서 보스전만 짧아졌다)
        baseB = list(G.BOSSHP)
        for i, m in enumerate(cfg["boss"]):
            G.BOSSHP[i] = round(baseB[i] * m)


def play(seed, max_runs=400):
    """mutation_calc.run_level(0) 과 같은 규칙 + 지역별 '클리어 시점 티어별 구매 수' 기록."""
    nodes = G.build()
    bought = {"root"}; lane_cnt = {}
    s = G.new_stats(); s.flatBonus += G.ROOT_FLAT
    gold = 0
    mins, runs_l, prof = [], [], []
    for stage in range(8):
        w = G.wave_cfg(stage, asc=0)
        secs, runs, won = 0.0, 0, False
        while runs < max_runs:
            r = run_once(s, w, seed=seed * 99991 + stage * 61 + runs * 11, dt=M.DT, **AIM)
            gold += round(r["gold"] * GOLD_FIX)
            secs += r["elapsed"]; runs += 1
            if r["won"]:
                won = True
                break
            secs += M.SHOP
            while True:
                cand = [n for n in nodes if n.id not in bought and n.parent in bought
                        and n.tier < stage + 1 and n.cost <= gold]
                if not cand:
                    break
                p = min(cand, key=lambda n: (n.cost, lane_cnt.get(n.lane, 0), M.LANE_PRIO.get(n.lane, 99), n.id))
                gold -= p.cost; bought.add(p.id); lane_cnt[p.lane] = lane_cnt.get(p.lane, 0) + 1
                p.apply(s)
        mins.append(secs / 60.0); runs_l.append(runs)
        # 이번 지역에서 새로 열린 티어(stage) 를 얼마나 샀나
        tt = [n for n in nodes if n.type is not None and n.tier == stage]
        prof.append(sum(1 for n in tt if n.id in bought) / max(1, len(tt)))
        if not won:
            return mins, runs_l, prof, stage
    return mins, runs_l, prof, None


def _one(args):
    seed, cfg = args
    setup(cfg)
    return play(seed)


def main():
    cfg = json.loads(sys.argv[1]) if len(sys.argv) > 1 else {}
    t0 = time.time()
    with Pool(len(SEEDS)) as p:
        res = p.map(_one, [(s, cfg) for s in SEEDS])
    setup(cfg)
    print("TIER_COST =", G.TIER_COST)
    if cfg.get("hp"):
        print("HP 배율 =", cfg["hp"])
    if cfg.get("flat"):
        print("FLAT_BY_TIER =", G.FLAT_BY_TIER)
    n_common = min(len(r[0]) for r in res)
    avg = [statistics.mean(r[0][i] for r in res) for i in range(n_common)]
    rn = [statistics.mean(r[1][i] for r in res) for i in range(n_common)]
    pf = [statistics.mean(r[2][i] for r in res) for i in range(n_common)]
    print("지역         " + "  ".join("  S%d " % (i + 1) for i in range(n_common)))
    print("분(시뮬)     " + "  ".join("%5.1f" % a for a in avg))
    print("판수         " + "  ".join("%5.0f" % a for a in rn))
    print("해당티어 구매 " + "  ".join("%4.0f%%" % (a * 100) for a in pf) + "   (지역 k 클리어 시 티어 k-1 을 산 비율)")
    tot = sum(avg)
    print("누적 시뮬 %.0f분 → 실측 추정 %.0f분 (%.1f시간)   목표 ≈ 300분" % (tot, tot / M.SIM_TO_REAL, tot / M.SIM_TO_REAL / 60))
    walls = [r[3] for r in res if r[3] is not None]
    if walls:
        print("*** 벽: S%d 에서 %d/%d seed 가 400판 안에 못 깸 ***" % (min(walls) + 1, len(walls), len(SEEDS)))
    print("(%.0f초 걸림)" % (time.time() - t0))


if __name__ == "__main__":
    main()
