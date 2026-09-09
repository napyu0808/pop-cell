"""
Income-vs-build-depth curve.

The "cheapest first" policy buys whole depth rings (9 nodes of equal cost) in order,
so the build is fully described by the ring depth d.  Measure G(d) = gold per run at
depth d, then the runs needed to buy ring d+1 is  9*C(d+1) / G(d).
That turns balancing into arithmetic instead of guesswork.
"""
from sim import Stats, run_once
from tree import build
from prog import AIM, GOLD_FIX


def stats_at_depth(cfg, d):
    """All nodes with j < d bought, in every chain (+ root)."""
    s = Stats()
    for n in build(cfg):
        if n.chain == "root" or n.j < d:
            n.apply(s)
    if not cfg.goldMultApplies:
        s.goldMultPercent = 0.0
    return s


def income_curve(cfg, seeds=4, dt=1 / 60.0):
    """-> list of dicts per depth 0..chain"""
    w = cfg.wavecfg()
    out = []
    for d in range(cfg.chain + 1):
        s = stats_at_depth(cfg, d)
        gs, ks, wv, won, el = [], [], [], 0, []
        for i in range(seeds):
            r = run_once(s, w, seed=1000 + d * 17 + i, dt=dt, **AIM)
            gs.append(r["gold"] * GOLD_FIX); ks.append(r["kills"])
            wv.append(r["wave"]); won += r["won"]; el.append(r["elapsed"])
        out.append(dict(
            d=d, gold=sum(gs) / seeds, kills=sum(ks) / seeds, wave=sum(wv) / seeds,
            won=won / seeds, elapsed=sum(el) / seeds,
            run_len=cfg.baseTimeLimit + s.bonusTimeSec,
            hit=s.hit(), R=s.cursorRadius, atk=s.attackInterval, startWave=s.startWave,
        ))
    return out


def project(cfg, curve, shop=15.0, cap_runs=400):
    """Estimate total runs/minutes from the income curve + cost ladder."""
    total_runs = 0.0
    total_sec = 0.0
    bank = 0.0
    rows = []
    for d in range(1, cfg.chain + 1):
        need = 9 * cfg.cost(d)
        g = curve[d - 1]["gold"]
        if g <= 0:
            return None
        runs = 0.0
        while bank < need and runs < cap_runs:
            bank += g
            runs += 1
            total_sec += curve[d - 1]["elapsed"] + shop
        bank -= need
        total_runs += runs
        rows.append(dict(d=d, cost1=cfg.cost(d), ring=need, income=g, runs=runs,
                         cum_min=total_sec / 60.0, wave=curve[d - 1]["wave"],
                         won=curve[d]["won"] if d < len(curve) else 0))
        if curve[d]["won"] >= 0.75:      # boss reliably dies at this depth -> game over
            break
    return dict(runs=total_runs, minutes=total_sec / 60.0, rows=rows)


def show(cfg, name=""):
    c = income_curve(cfg)
    print(f"\n===== income curve: {name} =====")
    print(f"{'d':>3} {'hit':>8} {'R':>5} {'atk':>6} {'sw':>3} {'run':>4} "
          f"{'wave':>5} {'kills':>6} {'gold/run':>11} {'x prev':>6} {'won':>4}")
    for i, r in enumerate(c):
        x = c[i]["gold"] / c[i - 1]["gold"] if i and c[i - 1]["gold"] > 0 else 0
        print(f"{r['d']:3d} {r['hit']:8,.0f} {r['R']:5.2f} {r['atk']:6.3f} "
              f"{r['startWave']:3d} {r['run_len']:4.0f} {r['wave']:5.1f} {r['kills']:6.0f} "
              f"{r['gold']:11,.0f} {x:6.2f} {r['won']:4.0%}")
    p = project(cfg, c)
    if p:
        print(f"\n{'d':>3} {'node$':>10} {'ring$':>11} {'income':>11} {'runs':>6} {'cum min':>8}")
        for r in p["rows"]:
            print(f"{r['d']:3d} {r['cost1']:10,d} {r['ring']:11,d} {r['income']:11,.0f} "
                  f"{r['runs']:6.1f} {r['cum_min']:8.1f}")
        print(f"  => projected {p['runs']:.0f} runs, {p['minutes']:.1f} min")
    return c, p
