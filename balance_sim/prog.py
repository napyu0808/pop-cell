"""Progression simulator: play run -> earn gold -> shop -> repeat, until the boss dies."""
import random
from sim import Stats, run_once
from tree import build, CHAINS

# calibrated against the user's two real playtests (see calibrate*.py)
AIM = dict(cursor_speed=8.0, react=0.45, aim_jitter=0.7)
GOLD_FIX = 1.11          # sim is a consistent 0.90x on both anchors
SHOP_SEC = 15.0          # time spent in the upgrade screen per visit


def _stats_from(cfg, bought):
    s = Stats()
    for n in build(cfg):
        if n.id in bought:
            n.apply(s)
    if not cfg.goldMultApplies:
        s.goldMultPercent = 0.0
    return s


def _buyable(cfg, nodes, bought):
    """A node is buyable when its parent (previous j in the chain, or root) is bought."""
    out = []
    for n in nodes:
        if n.id in bought:
            continue
        if n.chain == "root":
            out.append(n)
        elif n.j == 0:
            if "root" in bought:
                out.append(n)
        elif f"{n.chain}{n.j-1}" in bought:
            out.append(n)
    return out


def simulate(cfg, policy="cheapest", seed=0, max_runs=400, dt=1 / 60.0,
             shop_sec=SHOP_SEC, seeds_per_run=1, verbose=False, priority=None):
    nodes = build(cfg)
    bought = {"root"}
    gold = 0
    total_sec = 0.0
    runs = 0
    wcfg = cfg.wavecfg()
    log = []

    while runs < max_runs:
        s = _stats_from(cfg, bought)
        # --- play one cycle
        g = 0; won = False; elapsed = 0.0; wave = 0; kills = 0
        for k in range(seeds_per_run):
            r = run_once(s, wcfg, seed=seed * 1000 + runs * 10 + k, dt=dt, **AIM)
            g += r["gold"]; elapsed += r["elapsed"]; wave += r["wave"]; kills += r["kills"]
            won = won or r["won"]
        g = round(g / seeds_per_run * GOLD_FIX)
        elapsed /= seeds_per_run; wave /= seeds_per_run; kills /= seeds_per_run
        gold += g
        total_sec += elapsed
        runs += 1
        log.append(dict(run=runs, wave=wave, kills=kills, gold=g, bank=gold,
                        bought=len(bought), t=total_sec))
        if verbose:
            print(f"  run {runs:3d}  w{wave:5.1f}  kills {kills:6.0f}  +${g:9,d}"
                  f"  bank ${gold:10,d}  nodes {len(bought):3d}  t {total_sec/60:5.2f}m")
        if won:
            return dict(runs=runs, minutes=total_sec / 60.0, nodes=len(bought),
                        gold_spent=sum(n.cost for n in nodes if n.id in bought),
                        cleared=True, log=log)

        # --- shop
        total_sec += shop_sec
        bought_any = False
        while True:
            cands = [n for n in _buyable(cfg, nodes, bought) if n.cost <= gold]
            if not cands:
                break
            if policy == "cheapest":
                pick = min(cands, key=lambda n: (n.cost, n.chain))
            elif policy == "priority":
                order = priority or CHAINS
                pick = min(cands, key=lambda n: (order.index(n.chain)
                                                 if n.chain in order else 99, n.cost))
            else:
                raise ValueError(policy)
            gold -= pick.cost
            bought.add(pick.id)
            bought_any = True
        if not bought_any and runs > 3 and gold == 0:
            pass

    return dict(runs=runs, minutes=total_sec / 60.0, nodes=len(bought),
                gold_spent=sum(n.cost for n in nodes if n.id in bought),
                cleared=False, log=log)


def report(cfg, name="", policies=("cheapest",), seeds=3, **kw):
    print(f"\n--- {name} ---")
    for pol in policies:
        res = [simulate(cfg, policy=pol, seed=i, **kw) for i in range(seeds)]
        ok = [r for r in res if r["cleared"]]
        if not ok:
            print(f"  {pol:10s}: NOT CLEARED in {res[0]['runs']} runs "
                  f"({res[0]['minutes']:.0f} min, {res[0]['nodes']} nodes)")
            continue
        m = sum(r["minutes"] for r in ok) / len(ok)
        rr = sum(r["runs"] for r in ok) / len(ok)
        nn = sum(r["nodes"] for r in ok) / len(ok)
        print(f"  {pol:10s}: {m:5.1f} min   runs {rr:5.1f}   nodes {nn:5.0f}"
              f"   cleared {len(ok)}/{seeds}")
    return res
