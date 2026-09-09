"""
Compare income-curve SHAPE for different gold curves / hp growths.

What matters for a long game is not the absolute gold but gamma(d) = G(d)/G(d-1).
If gamma is roughly constant, a geometric cost ladder a*b^d can hold the
runs-per-ring ratio constant at any value we choose -> playtime becomes a dial.
"""
import math, statistics, sys
from tree import Cfg
from income import income_curve


def curve_geom(g1, gr, n=40):
    return [max(1, round(g1 * gr ** (w - 1))) for w in range(1, n + 1)]


def analyse(cfg, name, seeds=3):
    c = income_curve(cfg, seeds=seeds)
    gam = [c[i]["gold"] / c[i - 1]["gold"] for i in range(1, len(c)) if c[i - 1]["gold"] > 0]
    span = c[-1]["gold"] / max(1e-9, c[0]["gold"])
    # geometric mean gamma and its spread
    lg = [math.log(g) for g in gam if g > 0]
    gm = math.exp(statistics.mean(lg))
    sd = statistics.pstdev(lg)
    waves = [r["wave"] for r in c]
    plateau = sum(1 for r in c if r["wave"] >= cfg.totalWaves - 0.5)
    print(f"\n### {name}")
    print(f"    gold/run  d0 ${c[0]['gold']:,.0f} -> d{cfg.chain} ${c[-1]['gold']:,.0f}"
          f"   span {span:,.0f}x")
    print(f"    gamma  geo-mean {gm:.3f}  log-sd {sd:.3f}  min {min(gam):.2f} max {max(gam):.2f}")
    print(f"    wave reached: d0 {waves[0]:.1f} -> d{cfg.chain} {waves[-1]:.1f}"
          f"   (plateaued at max wave for {plateau} depths)")
    print(f"    kills: {c[0]['kills']:.0f} -> {c[-1]['kills']:.0f}"
          f"   boss killed at full: {c[-1]['won']:.0%}")
    print("    gamma by depth: " + " ".join(f"{g:.2f}" for g in gam))
    return c, gm, sd


BASE = dict(chain=20, cost_a=8.0, cost_b=1.6,
            flat=lambda j: 6 + j * 3, mult_pp=9.0, interval_mul=0.95,
            crit_pp=0.03, critmult_add=0.2, cursor_add=0.10, gold_pp=6.0,
            time_add=3.0, time_cap=55.0,
            skip_at={3: 5, 7: 10, 11: 15, 15: 20, 19: 25},
            spawn_mul=0.94, spawn_count_at=(4, 9, 14),
            baseTimeLimit=40.0, totalWaves=40, bossHp=1_000_000,
            goldMultApplies=True)

if __name__ == "__main__":
    tests = [
        ("current (1 -> 823, 823x)", dict(hpGrowth=1.155,
                                          goldCurve=[1, 2, 5, 10, 12, 15, 20, 23, 27, 31, 36, 41,
                                                     46, 51, 56, 62, 69, 77, 86, 96, 108, 121, 135,
                                                     151, 168, 187, 208, 231, 258, 288, 320, 356,
                                                     396, 440, 489, 543, 603, 669, 742, 823])),
        ("flat  g=8", dict(hpGrowth=1.155, goldCurve=curve_geom(8, 1.00))),
        ("gentle 6 x1.03 (w40=19)", dict(hpGrowth=1.155, goldCurve=curve_geom(6, 1.03))),
        ("gentle 5 x1.05 (w40=33)", dict(hpGrowth=1.155, goldCurve=curve_geom(5, 1.05))),
        ("gentle 4 x1.08 (w40=80)", dict(hpGrowth=1.155, goldCurve=curve_geom(4, 1.08))),
    ]
    for name, over in tests:
        cfg = Cfg(**{**BASE, **over})
        analyse(cfg, name)
