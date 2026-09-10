"""Progression sim for the POP Cell 2.0 tree (build_v2 / CfgV2)."""
import statistics
from sim import Stats, run_once
from tree import build_v2, CfgV2
from prog import AIM, GOLD_FIX, SHOP_SEC


def _stats_from(cfg, nodes, bought):
    s = Stats()
    for n in nodes:
        if n.id in bought:
            n.apply(s)
    if not cfg.goldMultApplies:
        s.goldMultPercent = 0.0
    return s


def _buyable(nodes, bought):
    return [n for n in nodes if n.id not in bought and n.id != "root" and n.parent in bought]


def simulate(cfg, policy="cheapest", seed=0, max_runs=500, dt=1 / 60.0,
             shop_sec=SHOP_SEC, verbose=False, priority=None):
    nodes = build_v2(cfg)
    bought = {"root"}
    gold, total_sec, runs = 0, 0.0, 0
    wcfg = cfg.wavecfg()
    log = []
    while runs < max_runs:
        s = _stats_from(cfg, nodes, bought)
        r = run_once(s, wcfg, seed=seed * 1000 + runs * 10, dt=dt, **AIM)
        g = round(r["gold"] * GOLD_FIX)
        gold += g
        total_sec += r["elapsed"]
        runs += 1
        log.append(dict(run=runs, wave=r["wave"], kills=r["kills"], gold=g,
                        bank=gold, nodes=len(bought), t=total_sec))
        if verbose:
            print("  run %3d w%5.1f kills %6.0f +$%10s  bank $%11s  nodes %3d  t %5.2fm"
                  % (runs, r["wave"], r["kills"], format(g, ","), format(gold, ","),
                     len(bought), total_sec / 60))
        if r["won"]:
            return dict(runs=runs, minutes=total_sec / 60.0, nodes=len(bought),
                        cleared=True, log=log)
        total_sec += shop_sec
        while True:
            cands = [n for n in _buyable(nodes, bought) if n.cost <= gold]
            if not cands:
                break
            if policy == "cheapest":
                pick = min(cands, key=lambda n: (n.cost, n.chain))
            else:
                order = priority or []
                pick = min(cands, key=lambda n: (order.index(n.chain) if n.chain in order else 99, n.cost))
            gold -= pick.cost
            bought.add(pick.id)
    return dict(runs=runs, minutes=total_sec / 60.0, nodes=len(bought), cleared=False, log=log)


def report(cfg, name="", seeds=3, **kw):
    res = [simulate(cfg, seed=i, **kw) for i in range(seeds)]
    ok = [r for r in res if r["cleared"]]
    if not ok:
        print("  %-24s NOT CLEARED (%d runs, %.0f min, %d nodes)"
              % (name, res[0]["runs"], res[0]["minutes"], res[0]["nodes"]))
        return None
    m = [r["minutes"] for r in ok]
    print("  %-24s %5.1f min (%4.1f-%4.1f)  runs %4.1f  nodes %5.1f/160  [%d/%d]"
          % (name, statistics.mean(m), min(m), max(m),
             statistics.mean([r["runs"] for r in ok]),
             statistics.mean([r["nodes"] for r in ok]), len(ok), seeds))
    return statistics.mean(m)
