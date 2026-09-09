"""
Balance optimiser.

For a given game config we measure the income curve G(d) ONCE (expensive), then
sweep the cost ladder (cost_a, cost_b) analytically (free) to hit a target
playtime with a healthy runs-per-ring profile.
"""
import math, statistics
from tree import Cfg, full_stats
from income import income_curve

TARGET_MIN = 40.0
SHOP = 15.0


def curve_geom(g1, gr, n=40):
    return [max(1, round(g1 * gr ** (w - 1))) for w in range(1, n + 1)]


def boss_depth(curve, thresh=0.75):
    """First depth at which the boss reliably dies -> the game ends there."""
    for r in curve:
        if r["won"] >= thresh:
            return r["d"]
    return None


def project_cost(curve, cost_a, cost_b, chain, shop=SHOP, stop_depth=None):
    """Walk the rings with an explicit bank; returns runs, minutes, per-ring runs."""
    bank = 0.0
    total_runs = 0
    total_sec = 0.0
    per = []
    last = stop_depth if stop_depth is not None else chain
    for d in range(1, last + 1):
        need = 9 * round(cost_a * cost_b ** d)
        g = curve[d - 1]["gold"]
        el = curve[d - 1]["elapsed"]
        runs = 0
        while bank < need:
            bank += g
            runs += 1
            total_sec += el + shop
            total_runs += 1
            if runs > 500:
                return None
        bank -= need
        per.append(runs)
    return dict(runs=total_runs, minutes=total_sec / 60.0, per=per)


def fit_cost(curve, chain, target_min=TARGET_MIN, stop_depth=None,
             b_range=None, shop=SHOP):
    """Find (cost_a, cost_b) hitting target_min with the smoothest ring profile."""
    best = None
    if b_range is None:
        b_range = [1.00 + 0.02 * i for i in range(0, 41)]   # 1.00 .. 1.80
    for b in b_range:
        # binary-search cost_a for the target time
        lo, hi = 1e-4, 1e9
        for _ in range(60):
            mid = math.sqrt(lo * hi)
            p = project_cost(curve, mid, b, chain, shop, stop_depth)
            if p is None:
                hi = mid
                continue
            if p["minutes"] < target_min:
                lo = mid
            else:
                hi = mid
        a = math.sqrt(lo * hi)
        p = project_cost(curve, a, b, chain, shop, stop_depth)
        if p is None:
            continue
        per = p["per"]
        if max(per) > 8:                      # a single ring that big = a wall
            pen = (max(per) - 8) * 3.0
        else:
            pen = 0.0
        # want a gently RISING profile: early rings cheap, late rings a push
        n = len(per)
        early = sum(per[:n // 3]) / max(1, n // 3)
        late = sum(per[-n // 3:]) / max(1, n // 3)
        rise = late - early
        score = abs(p["minutes"] - target_min) * 0.5 + pen - min(rise, 4.0) * 1.5 \
            + statistics.pstdev(per) * 0.3
        if best is None or score < best[0]:
            best = (score, a, b, p)
    return best


def evaluate(cfg, name, target_min=TARGET_MIN, seeds=3, verbose=True):
    curve = income_curve(cfg, seeds=seeds)
    bd = boss_depth(curve)
    fs = full_stats(cfg)
    w = cfg.wavecfg()
    hit = fs.hit()
    frontier = max([wv for wv in range(1, cfg.totalWaves + 1)
                    if hit >= math.ceil(w.hp(wv) * 2)] or [0])
    res = fit_cost(curve, cfg.chain, target_min, stop_depth=bd)
    if verbose:
        print(f"\n### {name}")
        print(f"    boss dies at depth {bd}/{cfg.chain}"
              f"   one-shot(max size) to wave {frontier}"
              f"   full hit {hit:,.0f}   hp w30 {w.hp(30):,.0f} w39 {w.hp(39):,.0f}")
        print(f"    income d0 ${curve[0]['gold']:,.0f} -> "
              f"d{cfg.chain} ${curve[-1]['gold']:,.0f}"
              f"   wave d0 {curve[0]['wave']:.1f} -> d{cfg.chain} {curve[-1]['wave']:.1f}")
        if res is None:
            print("    !! no cost ladder fits")
        else:
            score, a, b, p = res
            print(f"    BEST LADDER  cost = {a:.2f} * {b:.3f}^d"
                  f"   -> {p['minutes']:.1f} min, {p['runs']} runs")
            print(f"    node$ d1 ${round(a*b):,}  d5 ${round(a*b**5):,}  "
                  f"d10 ${round(a*b**10):,}  d15 ${round(a*b**15):,}  "
                  f"d{cfg.chain} ${round(a*b**cfg.chain):,}")
            print(f"    runs/ring: {p['per']}")
    return curve, bd, res
