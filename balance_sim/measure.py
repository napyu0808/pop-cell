"""Measure a Cfg: full-build power, per-wave one-shot check, and clear time."""
import math
from tree import Cfg, full_stats, tree_total, build
from sim import run_once
from prog import simulate, AIM, GOLD_FIX


def describe(cfg, name="", show_runs=False, seeds=3, shop=15.0):
    fs = full_stats(cfg)
    w = cfg.wavecfg()
    nodes = build(cfg)
    print(f"\n########## {name} ##########")
    print(f"  nodes {len(nodes)}   tree total ${tree_total(cfg):,}   "
          f"deepest node ${cfg.cost(cfg.chain):,}")
    print(f"  FULL: hit {fs.hit():,.0f}  atkInt {fs.attackInterval:.3f}  R {fs.cursorRadius:.2f}"
          f"  crit {fs.critChance:.0%}x{fs.critMult:.1f}  gold +{fs.goldMultPercent:.0f}%"
          f"  spawnInt x{fs.spawnIntervalMult:.2f}/{fs.spawnCount}  start w{fs.startWave}"
          f"  run {cfg.baseTimeLimit + fs.bonusTimeSec:.0f}s")
    # one-shot frontier: biggest enemy at wave X has hp = ceil(hp(X) * 2)
    hit = fs.hit()
    frontier = 0
    for wv in range(1, cfg.totalWaves + 1):
        if hit >= math.ceil(w.hp(wv) * 2):
            frontier = wv
    print(f"  one-shot (max-size) up to wave {frontier}   "
          f"| hp w20 {w.hp(20):,.0f}  w30 {w.hp(30):,.0f}  w40 {w.hp(40):,.0f}")
    r = run_once(fs, w, seed=3, **AIM)
    print(f"  FULL one run: wave {r['wave']}  kills {r['kills']}  "
          f"gold ${round(r['gold']*GOLD_FIX):,}  boss killed {r['won']}")

    res = [simulate(cfg, policy="cheapest", seed=i, shop_sec=shop, verbose=show_runs)
           for i in range(seeds)]
    ok = [x for x in res if x["cleared"]]
    if ok:
        m = [x["minutes"] for x in ok]
        print(f"  >>> CLEAR: {sum(m)/len(m):5.1f} min  "
              f"(range {min(m):.1f}-{max(m):.1f})  runs {sum(x['runs'] for x in ok)/len(ok):.1f}"
              f"  nodes {sum(x['nodes'] for x in ok)/len(ok):.0f}/{len(nodes)}"
              f"  [{len(ok)}/{seeds} cleared]")
    else:
        print(f"  >>> NOT CLEARED after {res[0]['runs']} runs / {res[0]['minutes']:.0f} min"
              f"  (nodes {res[0]['nodes']}/{len(nodes)})")
    return res


CURVE40 = [1, 2, 5, 10, 12, 15, 20,
           23, 27, 31, 36, 41, 46, 51, 56, 62, 69, 77, 86,
           96, 108, 121, 135, 151, 168, 187, 208, 231, 258, 288,
           320, 356, 396, 440, 489, 543, 603, 669, 742, 823]

R14 = Cfg(chain=20, cost_a=8.0, cost_b=1.6,
          flat=lambda j: 6 + j * 3, mult_pp=9.0, interval_mul=0.95,
          crit_pp=0.03, critmult_add=0.2, cursor_add=0.10, gold_pp=6.0,
          time_add=3.0, time_cap=55.0,
          skip_at={3: 5, 7: 10, 11: 15, 15: 20, 19: 25},
          spawn_mul=0.94, spawn_count_at=(4, 9, 14),
          baseTimeLimit=40.0, hpGrowth=1.155, totalWaves=40, bossHp=1_000_000,
          goldCurve=CURVE40, goldMultApplies=True)

if __name__ == "__main__":
    describe(R14, "ROUND 14 (current, shipped)", show_runs=True, seeds=3)
