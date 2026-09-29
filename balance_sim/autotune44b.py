# -*- coding: utf-8 -*-
"""
round44 2단계 — 1단계(autotune44)로 1~6지역을 고정한 뒤, 7·8지역 체력만 맞춘다.
  반복 강화 가격은 티어5 가격의 절반으로 고정(한 판에 몇 번씩 살 수 있게).
  목표: 7지역 260분, 8지역 390분(시뮬). 7·8지역을 깨려면 반복 강화가 필요해야 한다.
"""
import json, statistics
from multiprocessing import Pool
import tune42 as T

base = json.load(open("autotune44_result.json"))["cfg"]
TGT7, TGT8 = 260.0, 390.0


def evaluate(cfg):
    with Pool(len(T.SEEDS)) as p:
        res = p.map(T._one, [(s, cfg) for s in T.SEEDS])
    n = min(len(r[0]) for r in res)
    mins = [statistics.mean(r[0][j] for r in res) for j in range(n)]
    rl = {}
    for r in res:
        for k, v in r[5].items():
            rl[k] = rl.get(k, 0) + v / len(res)
    walls = [r[6] for r in res if r[6] is not None]
    return mins, rl, walls


def main():
    # 7·8지역 합 시간은 반복 강화 가격으로, 7:8 비율은 8지역 체력으로 맞춘다.
    #   (골드 배율이 체력에서 계산되므로 체력만 올리면 골드도 같이 올라 시간이 거의 안 늘었다)
    rep = 596244673
    m7, m8 = 10.23, 15.05
    best = None
    for it in range(12):
        hp = list(base["hp"]); hp[6] = m7; hp[7] = m8
        cfg = dict(base); cfg["hp"] = hp; cfg["rep"] = [int(rep)] * 6
        mins, rl, walls = evaluate(cfg)
        s7 = mins[6] if len(mins) > 6 else None
        s8 = mins[7] if len(mins) > 7 else None
        print("iter %2d rep %d m7 %.2f m8 %.2f  mins %s  rep %s  walls %s" % (
            it, cfg["rep"][0], m7, m8, [round(m) for m in mins], {k: round(v) for k, v in sorted(rl.items())}, walls), flush=True)
        if walls:
            w = min(walls)
            if w == 6: m7 *= 0.8
            elif w == 7: m8 *= 0.8
            else: break
            rep *= 0.8
            continue
        err = abs(s7 - TGT7) / TGT7 + abs(s8 - TGT8) / TGT8
        if best is None or err < best[0]:
            best = (err, cfg, mins, rl)
        if err < 0.12:
            break
        # 7지역 시간은 7지역 체력으로(반응이 약해 지수 1.2), 8지역 시간은 반복 강화 가격으로
        m7 *= (TGT7 / max(1.0, s7)) ** 1.2
        rep *= (TGT8 / max(1.0, s8)) ** 0.85
    if best:
        err, cfg, mins, rl = best
        print()
        print("BEST err %.3f tot %.0f cfg %s" % (err, sum(mins), json.dumps(cfg)))
        json.dump({"cfg": cfg, "mins": mins, "rep": rl}, open("autotune44b_result.json", "w"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
