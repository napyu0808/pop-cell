"""정밀 8스테이지 튜너 (빠른 버전). stage_sim 개념 재사용, dt 완화, 스테이지별 국소 시뮬."""
import statistics
from sim import Stats, run_once, WaveCfg
from tree import CfgV2, build_v2
from prog import AIM, GOLD_FIX

SHOP = 15.0
DT = 1 / 45.0
TARGET_MIN = [15, 30, 40, 40, 50, 55, 60, 65]

WAVES  = [4, 5, 6, 8, 10, 12, 14, 18]
TLIM   = [26, 32, 40, 48, 56, 64, 72, 84]
HP0    = [7, 12, 22, 42, 82, 160, 320, 640]
HPG    = [1.13, 1.14, 1.15, 1.155, 1.16, 1.165, 1.17, 1.175]
QUOTA0 = [4, 5, 6, 7, 8, 9, 10, 11]
SE     = [8, 9, 10, 11, 12, 13, 14, 15]


def _stats(nodes, bought):
    s = Stats()
    for n in nodes:
        if n.id in bought:
            n.apply(s)
    return s


def _wc(idx, bosshp, gr, gc0):
    gc = [max(1, round(v * gr)) for v in gc0]
    w = WaveCfg(baseTimeLimit=TLIM[idx], hpGrowth=HPG[idx], baseEnemyHp=HP0[idx],
               totalWaves=WAVES[idx], bossHp=bosshp, baseQuota=QUOTA0[idx],
               quotaSlope=2, goldCurve=gc)
    w.startEnemies = SE[idx]
    w.spawnBase = 0.55
    return w


def run_stage(cfg, nodes, bought, gold, idx, gr, bh, seed=0, cap=180):
    """스테이지 하나. bought/gold 는 넘겨받은 상태에서 시작(이전 스테이지 유산). in-place 로 bought 갱신."""
    T = idx + 1
    gc0 = cfg.goldCurve
    wc = _wc(idx, bh, gr, gc0)
    secs, runs = 0.0, 0
    while runs < cap:
        s = _stats(nodes, bought)
        r = run_once(s, wc, seed=seed * 911 + T * 41 + runs * 7, dt=DT, **AIM)
        gold += round(r["gold"] * GOLD_FIX)
        secs += r["elapsed"]
        runs += 1
        if r["won"]:
            return secs, runs, gold, True
        secs += SHOP
        while True:
            cand = [n for n in nodes if n.id not in bought and n.id != "root"
                    and n.parent in bought and n.tier < T and n.cost <= gold]
            if not cand:
                break
            pick = min(cand, key=lambda n: (n.cost, n.chain))
            gold -= pick.cost
            bought.add(pick.id)
    return secs, runs, gold, False


def tier_dps(nodes, T):
    s = Stats()
    for n in nodes:
        if n.id != "root" and n.tier < T:
            n.apply(s)
    return s.hit() * (1 / max(0.15, s.attackInterval)) * (1 + s.critChance * (s.critMult - 1))


def tune(cfg, seed=0, verbose=True):
    nodes = build_v2(cfg)
    gr = [1.0] * 8
    bh = [1000] * 8
    # 스테이지 순차: 이전 스테이지 클리어 상태를 그대로 물려줌
    bought = {"root"}
    gold = 0
    carry = []          # 각 스테이지 진입 시점의 (bought snapshot, gold)
    for idx in range(8):
        T = idx + 1
        D = tier_dps(nodes, T)
        bh[idx] = round(D * TLIM[idx] * 0.90)
        entry_bought = set(bought)
        entry_gold = gold
        lo, hi = 0.004, 3000.0
        for _ in range(14):
            gr[idx] = (lo * hi) ** 0.5
            b = set(entry_bought)
            secs, runs, g, cl = run_stage(cfg, nodes, b, entry_gold, idx, gr[idx], bh[idx], seed=seed)
            m = secs / 60
            if not cl:
                lo = gr[idx]
            elif m > TARGET_MIN[idx]:
                lo = gr[idx]
            else:
                hi = gr[idx]
        gr[idx] = (lo * hi) ** 0.5
        # 풀강-이 스테이지에서 마지막 5노드 빼면 못 깨야 함(= tier 강제)
        for _bump in range(6):
            b5 = set(entry_bought)
            tiernodes = sorted([n for n in nodes if n.id != "root" and n.tier == idx], key=lambda n:(n.chain, n.j))
            for n in tiernodes[:-5]:
                b5.add(n.id)
            wc5 = _wc(idx, bh[idx], 999.0, cfg.goldCurve)
            s5 = _stats(nodes, b5)
            won5 = False
            for k in range(3):
                if run_once(s5, wc5, seed=idx*7+k, dt=DT, **AIM)["won"]:
                    won5 = True; break
            if not won5:
                break
            bh[idx] = round(bh[idx] * 1.18)   # 너무 쉬움 -> 보스 강화
        # 확정값으로 실제 진행시켜 다음 스테이지 유산 만들기
        bought = set(entry_bought)
        gold = entry_gold
        secs, runs, gold, cl = run_stage(cfg, nodes, bought, gold, idx, gr[idx], bh[idx], seed=seed)
        if verbose:
            print("  S%d  목표%2d분  ->  %5.1f분  runs %3d  노드 %3d  gr %.2f  bossHp %s  %s"
                  % (T, TARGET_MIN[idx], secs / 60, runs, len(bought) - 1, gr[idx],
                     f"{bh[idx]:,}", "OK" if cl else "FAIL"))
    return gr, bh


if __name__ == "__main__":
    cfg = CfgV2()
    print("TierCost:", cfg.tier_cost, " rings:", [20 * c for c in cfg.tier_cost])
    gr, bh = tune(cfg, seed=0)
    print("\ngoldRate =", [round(x, 3) for x in gr])
    print("bossHp   =", bh)
