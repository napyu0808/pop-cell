"""Anchor C: round-13 config -> the user cleared the boss in ~7 minutes, no cheat."""
from tree import Cfg, full_stats, tree_total
from sim import run_once
from prog import simulate, AIM, GOLD_FIX

CURVE30 = [1, 2, 5, 10, 12, 15, 20,
           23, 27, 31, 36, 41, 46, 51, 56, 62, 69, 77, 86,
           96, 108, 121, 135, 151, 168, 187, 208, 231, 258, 288]


def r13(goldmult):
    return Cfg(chain=15, cost_a=7.0, cost_b=1.72,
               flat=lambda j: 6 + j * 3, mult_pp=9.0, interval_mul=0.95,
               crit_pp=0.04, critmult_add=0.2, cursor_add=0.12, gold_pp=6.0,
               time_add=3.0, time_cap=30.0,
               skip_at={3: 5, 6: 10, 9: 15, 11: 20, 13: 25},
               spawn_mul=0.94, spawn_count_at=(4, 9, 14),
               baseTimeLimit=30.0, hpGrowth=1.17, totalWaves=30, bossHp=100_000,
               goldCurve=CURVE30, goldMultApplies=goldmult)


for gm in (False, True):
    cfg = r13(gm)
    fs = full_stats(cfg)
    print(f"\n########## round-13 config, goldMult applied = {gm} ##########")
    print(f"  tree: 136 nodes, total ${tree_total(cfg):,}")
    print(f"  FULL build: hit {fs.hit():.0f}  atkInt {fs.attackInterval:.3f}  R {fs.cursorRadius:.2f}"
          f"  crit {fs.critChance:.0%}x{fs.critMult:.1f}  gold +{fs.goldMultPercent:.0f}%"
          f"  start w{fs.startWave}  time {cfg.baseTimeLimit + fs.bonusTimeSec:.0f}s")
    r = run_once(fs, cfg.wavecfg(), seed=3, **AIM)
    print(f"  FULL build one run: wave {r['wave']}  kills {r['kills']}  "
          f"gold ${round(r['gold']*GOLD_FIX):,}  boss killed: {r['won']}")
    for pol in ("cheapest",):
        for shop in (10.0, 15.0, 25.0):
            res = [simulate(cfg, policy=pol, seed=i, shop_sec=shop) for i in range(3)]
            ok = [x for x in res if x["cleared"]]
            if ok:
                m = sum(x["minutes"] for x in ok) / len(ok)
                rr = sum(x["runs"] for x in ok) / len(ok)
                nn = sum(x["nodes"] for x in ok) / len(ok)
                print(f"  {pol}, shop {shop:4.0f}s -> {m:5.2f} min  runs {rr:4.1f}"
                      f"  nodes {nn:4.0f}   (real: ~7 min)")
            else:
                print(f"  {pol}, shop {shop:4.0f}s -> NOT CLEARED "
                      f"({res[0]['runs']} runs, {res[0]['minutes']:.0f} min)")
