# -*- coding: utf-8 -*-
"""
round42~44 튜닝 드라이버 — 변이0, 1~8지역 클리어 시간을 seed 여러 개로 병렬 측정.
  목표(사용자, 2026-10-06 변경): 변이0 8지역 ≈ 3시간(실측) = 시뮬 ≈ 684분 (SIM_TO_REAL 3.8).
    예전 목표는 5시간이었는데 "좀 쉽게" 가자고 3시간으로 내렸다.
  round44 추가:
    - 반복 강화(가지 끝, 같은 값으로 같은 양) 를 구매 후보에 넣는다.
    - 지역별 골드 배율을 "골드/적 체력" 효율로 계산(beta = 지역 하나 올라갈 때 효율 배수).
    - "지역을 올리면 골드/분이 늘어나는가" — 지역 k 첫 3판 vs 지역 k-1 마지막 3판 골드/분 비율.

  사용: python tune42.py '{"cost":[...6개], "rep":[...6개], "beta":1.2, "hp":[..], "boss":[..]}'
"""
import sys, json, statistics, time
from multiprocessing import Pool

import gamedata as G
import mutation_calc as M
from sim import run_once
from prog import AIM, GOLD_FIX

SEEDS = [0, 1, 2]
OLD_GOLDRATE = [1.00, 0.70, 0.82, 0.95, 1.05, 1.15, 1.30, 1.55]   # round43 까지의 지역 골드 배율


def efficiency(goldrate):
    """지역별 '킬 골드 / 적 체력' (보스 웨이브 제외, 쿼터 가중)."""
    out = []
    for k in range(8):
        hp = gold = 0.0
        for w in range(1, G.WAVES[k]):
            q = G.QUOTA0[k] + (w - 1) * 2
            hp += q * G.HP0[k] * G.HPG[k] ** (w - 1)
            gold += q * G.GOLD_CURVE[w - 1] * goldrate[k]
        out.append(gold / hp)
    return out


def gold_rates(beta):
    """지역 k 의 골드/체력 효율 = 1지역 × beta^k 가 되도록 하는 배율(소수 둘째 자리 반올림)."""
    base = efficiency([1.0] * 8)
    return [round(base[0] * beta ** k / base[k], 2) for k in range(8)]


def setup(cfg):
    if cfg.get("cost"):
        G.TIER_COST[:] = cfg["cost"]
    if cfg.get("rep"):
        G.REP_COST[:] = cfg["rep"]
    if cfg.get("flat"):
        G.FLAT_BY_TIER[:] = cfg["flat"]
    if cfg.get("hp"):
        base0, baseB = list(G.HP0), list(G.BOSSHP)
        for i, m in enumerate(cfg["hp"]):
            G.HP0[i] = round(base0[i] * m)
            G.BOSSHP[i] = round(baseB[i] * m)
    if cfg.get("boss"):   # 보스만
        baseB = list(G.BOSSHP)
        for i, m in enumerate(cfg["boss"]):
            G.BOSSHP[i] = round(baseB[i] * m)
    if cfg.get("beta"):
        G.GOLDRATE[:] = gold_rates(cfg["beta"])
    if cfg.get("goldrate"):
        G.GOLDRATE[:] = cfg["goldrate"]


def rep_capped(n, s):
    """상한에 닿아 더 사도 효과 없는 반복 강화."""
    if n.type == "RSpeed":
        return s.attackInterval <= G.REP_INTERVAL_FLOOR + 1e-6
    if n.type == "RRange":
        return s.cursorRadius >= G.REP_RANGE_MAX - 1e-6
    return False


def play(seed, max_runs=400):
    """mutation_calc.run_level(0) 과 같은 규칙 + 반복 강화 + 지역별 기록."""
    nodes = G.build()
    bought = {"root"}; lane_cnt = {}; rep_lv = {}
    s = G.new_stats(); s.flatBonus += G.ROOT_FLAT
    gold = 0
    mins, runs_l, prof, gpm_first, gpm_last, boss_sec = [], [], [], [], [], []
    for stage in range(8):
        w = G.wave_cfg(stage, asc=0)
        secs, runs, won = 0.0, 0, False
        hist = []   # (골드, 초) 판별
        while runs < max_runs:
            r = run_once(s, w, seed=seed * 99991 + stage * 61 + runs * 11, dt=M.DT, **AIM)
            g = round(r["gold"] * GOLD_FIX)
            gold += g
            hist.append((g, r["elapsed"] + M.SHOP))
            secs += r["elapsed"]; runs += 1
            if r["won"]:
                won = True
                boss_sec.append(r.get("boss_sec", 0.0))
                break
            secs += M.SHOP
            while True:
                # 사람처럼: 살 수 있는 일반 노드가 있으면 그것부터(싼 것 → 덜 찍은 가지 순).
                # 없으면 반복 강화를 레벨이 낮은 것부터 고르게 — 상한에 닿은 것(공속·범위)은 제외.
                cand = [n for n in nodes if not n.repeat and n.id not in bought and n.parent in bought
                        and n.tier < stage + 1 and n.cost <= gold]
                if cand:
                    p = min(cand, key=lambda n: (n.cost, lane_cnt.get(n.lane, 0), M.LANE_PRIO.get(n.lane, 99), n.id))
                else:
                    cand = [n for n in nodes if n.repeat and n.parent in bought and n.tier < stage + 1
                            and n.cost <= gold and not rep_capped(n, s)]
                    if not cand:
                        break
                    p = min(cand, key=lambda n: (rep_lv.get(n.id, 0), n.cost, n.id))
                gold -= p.cost; bought.add(p.id); lane_cnt[p.lane] = lane_cnt.get(p.lane, 0) + 1
                if p.repeat:
                    rep_lv[p.id] = rep_lv.get(p.id, 0) + 1
                p.apply(s)
        mins.append(secs / 60.0); runs_l.append(runs)
        tt = [n for n in nodes if n.type is not None and not n.repeat and n.tier == stage]
        prof.append(sum(1 for n in tt if n.id in bought) / max(1, len(tt)) if tt else float("nan"))
        f = hist[:3]; l = hist[-3:]
        gpm_first.append(sum(g for g, _ in f) / max(1e-9, sum(t for _, t in f)) * 60)
        gpm_last.append(sum(g for g, _ in l) / max(1e-9, sum(t for _, t in l)) * 60)
        if not won:
            return mins, runs_l, prof, gpm_first, gpm_last, rep_lv, stage
    return mins, runs_l, prof, gpm_first, gpm_last, rep_lv, None


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
    print("TIER_COST =", G.TIER_COST, " REP_COST =", G.REP_COST)
    print("GOLDRATE  =", G.GOLDRATE)
    n = min(len(r[0]) for r in res)
    mean = lambda i, j: statistics.mean(r[i][j] for r in res)
    print("지역           " + "".join("%9s" % ("S%d" % (j + 1)) for j in range(n)))
    print("분(시뮬)       " + "".join("%9.1f" % mean(0, j) for j in range(n)))
    print("판수           " + "".join("%9.0f" % mean(1, j) for j in range(n)))
    print("해당티어 구매  " + "".join(("%8.0f%%" % (mean(2, j) * 100)) if j < G.NODE_TIERS else "        -" for j in range(n)))
    print("골드/분 첫3판  " + "".join("%9.0f" % mean(3, j) for j in range(n)))
    print("골드/분 끝3판  " + "".join("%9.0f" % mean(4, j) for j in range(n)))
    print("올라간 직후 배율 " + "".join("%8.2fx" % (mean(3, j) / max(1e-9, mean(4, j - 1))) if j > 0 else "        -" for j in range(n))
          + "   (지역 k 첫 3판 골드/분 ÷ 지역 k-1 마지막 3판)")
    rl = {}
    for r in res:
        for k, v in r[5].items():
            rl[k] = rl.get(k, 0) + v / len(res)
    if rl:
        print("반복 강화 레벨(평균): " + ", ".join("%s %.0f" % (k, v) for k, v in sorted(rl.items())))
    tot = sum(mean(0, j) for j in range(n))
    print("누적 시뮬 %.0f분 → 실측 추정 %.0f분 (%.1f시간)   목표 ≈ 180분(3시간)" % (tot, tot / M.SIM_TO_REAL, tot / M.SIM_TO_REAL / 60))
    walls = [r[6] for r in res if r[6] is not None]
    if walls:
        print("*** 벽: S%d 에서 %d/%d seed 가 400판 안에 못 깸 ***" % (min(walls) + 1, len(walls), len(SEEDS)))
    print("(%.0f초 걸림)" % (time.time() - t0))


if __name__ == "__main__":
    main()
