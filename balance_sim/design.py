"""
Design the node-cost TABLE from the measured income curve.

runs(d) = 9*C(d) / I(d-1)   ->   C(d) = rho(d) * I(d-1) / 9
Choosing rho(d) directly gives full control of pacing, which a geometric
ladder can never do because the income curve is not geometric.
"""
import math
from tree import Cfg, full_stats, build
from income import income_curve
from prog import simulate, AIM, GOLD_FIX
from sim import run_once


def rho_profile(n, start=1.0, end=3.4, power=1.7):
    """Runs-per-ring: flat-ish early (fast, fun), rising late (the real push)."""
    return [start + (end - start) * (i / (n - 1)) ** power for i in range(n)]


def nice(x):
    """Round to a readable number: 2 significant digits below 1000, 3 above."""
    if x < 20:
        return max(1, int(round(x)))
    if x < 1000:
        return int(round(x / 5.0) * 5)
    mag = 10 ** (math.floor(math.log10(x)) - 2)
    return int(round(x / mag) * mag)


def make_table(curve, chain, rho):
    tab = []
    prev = 0
    for d in range(1, chain + 1):
        c = nice(rho[d - 1] * curve[d - 1]["gold"] / 9.0)
        c = max(c, prev + 1)          # strictly increasing
        tab.append(c)
        prev = c
    return tab


def simulate_table(curve, tab, shop=15.0, stop_depth=None):
    bank = 0.0; runs = 0; sec = 0.0; per = []
    last = stop_depth or len(tab)
    for d in range(1, last + 1):
        need = 9 * tab[d - 1]
        g = curve[d - 1]["gold"]; el = curve[d - 1]["elapsed"]
        k = 0
        while bank < need:
            bank += g; k += 1; runs += 1; sec += el + shop
            if k > 400:
                return None
        bank -= need
        per.append(k)
    return dict(runs=runs, minutes=sec / 60.0, per=per)


def design(cfg, name, target_min=40.0, seeds=5, rho_end=3.4, rho_start=1.0,
           power=1.7, shop=15.0, verify=True, verify_seeds=3):
    curve = income_curve(cfg, seeds=seeds)
    bd = next((r["d"] for r in curve if r["won"] >= 0.75), None)
    fs = full_stats(cfg); w = cfg.wavecfg(); hit = fs.hit()
    frontier = max([v for v in range(1, cfg.totalWaves + 1)
                    if hit >= math.ceil(w.hp(v) * 2)] or [0])
    print(f"\n{'='*74}\n{name}\n{'='*74}")
    print(f"  full hit {hit:,.0f}   one-shot(max size) -> wave {frontier}"
          f"   boss dies at depth {bd}")
    print(f"  income  d0 ${curve[0]['gold']:,.0f}  d10 ${curve[10]['gold']:,.0f}"
          f"  d20 ${curve[20]['gold']:,.0f}")
    print(f"  wave    d0 {curve[0]['wave']:.1f}  d10 {curve[10]['wave']:.1f}"
          f"  d20 {curve[20]['wave']:.1f}")

    # scale rho so the projected time lands on target
    rho = rho_profile(cfg.chain, rho_start, rho_end, power)
    scale = 1.0
    best = None
    for _ in range(40):
        tab = make_table(curve, cfg.chain, [r * scale for r in rho])
        p = simulate_table(curve, tab, shop, stop_depth=bd)
        if p is None:
            break
        best = (tab, p)
        if abs(p["minutes"] - target_min) < 0.6:
            break
        scale *= target_min / p["minutes"]
    tab, p = best
    print(f"\n  cost table: {tab}")
    print(f"  ring runs : {p['per']}")
    print(f"  >>> projected {p['runs']} runs, {p['minutes']:.1f} min"
          f"   (tree total ${9*sum(tab):,})")

    if verify:
        cfg2 = Cfg(**{**cfg.__dict__, "cost_table": tab})
        res = [simulate(cfg2, policy="cheapest", seed=i, shop_sec=shop)
               for i in range(verify_seeds)]
        ok = [r for r in res if r["cleared"]]
        if ok:
            m = [r["minutes"] for r in ok]
            print(f"  >>> FULL SIM: {sum(m)/len(m):.1f} min "
                  f"(range {min(m):.1f}-{max(m):.1f})  "
                  f"runs {sum(r['runs'] for r in ok)/len(ok):.1f}  "
                  f"nodes {sum(r['nodes'] for r in ok)/len(ok):.0f}/{9*cfg.chain+1}"
                  f"  [{len(ok)}/{verify_seeds}]")
        else:
            print(f"  >>> FULL SIM: NOT CLEARED "
                  f"({res[0]['runs']} runs / {res[0]['minutes']:.0f} min, "
                  f"{res[0]['nodes']} nodes)")
    return curve, tab, p
