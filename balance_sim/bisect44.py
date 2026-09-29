# -*- coding: utf-8 -*-
"""
round44 3단계 — 7지역 체력(m7)·반복 강화 가격(rep)을 이분 탐색으로.
  7지역 시간은 m7 에 단조 증가지만 비선형(반복 강화 없이 깨지느냐의 문턱)이라 비례 조정은 진동했다.
  1) rep 고정, m7 을 [lo, hi] 에서 이분 탐색 → 7지역 ≈ 260분
  2) 그 m7 로 rep 을 로그 스케일 이분 탐색 → 8지역 ≈ 390분
  3) 최종 한 번 더 측정해서 저장(autotune44b_result.json 형식).
"""
import json, math, statistics
from multiprocessing import Pool
import tune42 as T

base = json.load(open("autotune44_result.json"))["cfg"]
TGT7, TGT8 = 260.0, 390.0
M8 = 15.05


def run(m7, rep):
    hp = list(base["hp"]); hp[6] = m7; hp[7] = M8
    cfg = dict(base); cfg["hp"] = hp; cfg["rep"] = [int(rep)] * 6
    with Pool(len(T.SEEDS)) as p:
        res = p.map(T._one, [(s, cfg) for s in T.SEEDS])
    n = min(len(r[0]) for r in res)
    mins = [statistics.mean(r[0][j] for r in res) for j in range(n)]
    rl = {}
    for r in res:
        for k, v in r[5].items():
            rl[k] = rl.get(k, 0) + v / len(res)
    walls = [r[6] for r in res if r[6] is not None]
    print("m7 %.2f rep %d  mins %s  rep %s  walls %s" % (m7, rep, [round(m) for m in mins],
          {k: round(v) for k, v in sorted(rl.items())}, walls), flush=True)
    return cfg, mins, rl, walls


def main():
    rep = 600_000_000
    lo, hi = 20.76, 30.83
    for _ in range(4):
        mid = (lo + hi) / 2
        cfg, mins, rl, walls = run(mid, rep)
        s7 = mins[6] if len(mins) > 6 and not walls else 1e9
        if s7 < TGT7: lo = mid
        else: hi = mid
    m7 = (lo + hi) / 2
    rlo, rhi = math.log(250_000_000), math.log(1_500_000_000)
    for _ in range(4):
        mid = math.exp((rlo + rhi) / 2)
        cfg, mins, rl, walls = run(m7, mid)
        s8 = mins[7] if len(mins) > 7 and not walls else 1e9
        if s8 < TGT8: rlo = math.log(mid)
        else: rhi = math.log(mid)
    rep = math.exp((rlo + rhi) / 2)
    cfg, mins, rl, walls = run(m7, rep)
    print("FINAL tot %.0f sim-min = %.1f h real  cfg %s" % (sum(mins), sum(mins) / 3.8 / 60, json.dumps(cfg)), flush=True)
    json.dump({"cfg": cfg, "mins": mins, "rep": rl, "walls": walls},
              open("autotune44b_result.json", "w"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
