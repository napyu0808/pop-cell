"""
분석적 스테이지 밸런싱 (빠름).
  boss 는 retry 간 회복 -> 한 런 안에 잡아야 함 -> bossHp = DPS(tier T-1 full) * (TLIM * BOSS_FRAC).
  farming(ring 자금 + 풀강 도달) 시간이 TARGET_MIN 을 채운다:
    ring(T-1) = income_per_run(stage T) * (TARGET_MIN*0.85*60 / (TLIM+SHOP))
    TierCost[T-1] = ring / 20
  income_per_run 은 '중간 tier(진입+절반)' 스탯으로 sim 몇 번만 측정.
"""
import statistics
from sim import Stats, run_once, WaveCfg
from tree import CfgV2, build_v2
from prog import AIM, GOLD_FIX

SHOP = 15.0
DT = 1 / 40.0
TARGET_MIN = [15, 30, 40, 40, 50, 55, 60, 65]
BOSS_FRAC = 0.50

WAVES  = [4, 5, 6, 8, 10, 12, 14, 18]
TLIM   = [26, 32, 40, 48, 56, 64, 72, 84]
HP0    = [7, 12, 22, 42, 82, 160, 320, 640]
HPG    = [1.13, 1.14, 1.15, 1.155, 1.16, 1.165, 1.17, 1.175]
QUOTA0 = [4, 5, 6, 7, 8, 9, 10, 11]
SE     = [8, 9, 10, 11, 12, 13, 14, 15]
GOLDRATE = [0.9, 1.05, 1.2, 1.4, 1.6, 1.85, 2.1, 2.4]

GC0 = CfgV2().goldCurve


def _stats(nodes, ids):
    s = Stats()
    for n in nodes:
        if n.id in ids:
            n.apply(s)
    return s


def _wc(idx, bosshp, gr):
    gc = [max(1, round(v * gr)) for v in GC0]
    w = WaveCfg(baseTimeLimit=TLIM[idx], hpGrowth=HPG[idx], baseEnemyHp=HP0[idx],
               totalWaves=WAVES[idx], bossHp=bosshp, baseQuota=QUOTA0[idx],
               quotaSlope=2, goldCurve=gc)
    w.startEnemies = SE[idx]
    w.spawnBase = 0.55
    return w


def nice(x):
    if x < 100: return max(10, int(round(x / 5) * 5))
    if x < 10000: return int(round(x / 50) * 50)
    if x < 1_000_000: return int(round(x / 500) * 500)
    return int(round(x / 5000) * 5000)


def dps_of(nodes, ids):
    s = _stats(nodes, ids)
    # 0.01 은 0으로 나누기 방지용일 뿐 — 실제 하한은 노드 적용 시(interval_floor)에 이미 걸려 있음
    return s.hit() * (1 / max(0.01, s.attackInterval)) * (1 + s.critChance * (s.critMult - 1))


def compute(seed=0):
    tier_cost = [30] * 8
    boss_hp = [0] * 8
    for idx in range(8):
        cfg = CfgV2(tier_cost=list(tier_cost))
        nodes = build_v2(cfg)
        T = idx + 1
        below = {n.id for n in nodes if n.id != "root" and n.tier < idx} | {"root"}
        tier_ids = sorted([n for n in nodes if n.id != "root" and n.tier == idx], key=lambda n: (n.chain, n.j))
        full = below | {n.id for n in tier_ids}
        half = below | {n.id for n in tier_ids[::2]}   # 절반

        dps_full = dps_of(nodes, full)
        boss_hp[idx] = nice(dps_full * TLIM[idx] * BOSS_FRAC)

        # income: 절반 스탯으로 몇 번
        s_half = _stats(nodes, half)
        wc = _wc(idx, boss_hp[idx], GOLDRATE[idx])
        inc = statistics.mean(round(run_once(s_half, wc, seed=seed*29+k, dt=DT, **AIM)["gold"] * GOLD_FIX)
                              for k in range(5))
        farm_runs = (TARGET_MIN[idx] * 0.85 * 60) / (TLIM[idx] + SHOP)
        ring = inc * farm_runs
        tc = nice(ring / 20)
        tier_cost[idx] = max(tc, tier_cost[idx - 1] * 2 if idx else 30)
        print("S%d  dpsFull %9.0f  bossHp $%-11s  inc/run $%-7s  farmRuns %4.1f  ring $%-12s  TierCost $%s"
              % (T, dps_full, f"{boss_hp[idx]:,}", f"{round(inc):,}", farm_runs,
                 f"{round(ring):,}", f"{tier_cost[idx]:,}"), flush=True)
    return tier_cost, boss_hp


def verify(tc, bh, seed=0, verbose=False):
    cfg = CfgV2(tier_cost=list(tc)); nodes = build_v2(cfg)
    bought = {"root"}; gold = 0; cum = 0; parts = []
    for idx in range(8):
        T = idx + 1
        wc = _wc(idx, bh[idx], GOLDRATE[idx])
        secs = 0.0; runs = 0
        while runs < 300:
            s = _stats(nodes, bought)
            r = run_once(s, wc, seed=seed*991 + T*61 + runs*11, dt=DT, **AIM)
            gold += round(r["gold"] * GOLD_FIX); secs += r["elapsed"]; runs += 1
            if r["won"]: break
            secs += SHOP
            while True:
                cand = [n for n in nodes if n.id not in bought and n.id != "root"
                        and n.parent in bought and n.tier < T and n.cost <= gold]
                if not cand: break
                pick = min(cand, key=lambda n: (n.cost, n.chain))
                gold -= pick.cost; bought.add(pick.id)
        cum += secs / 60
        parts.append("S%d %.0f분(누적%.0f,%d런,%d노드)%s" % (T, secs/60, cum, runs, len(bought)-1, "" if runs < 300 else "✗"))
    return parts


if __name__ == "__main__":
    print("=== 계산 ===", flush=True)
    tc, bh = compute(seed=0)
    print("\nTierCost =", tc, flush=True)
    print("bossHp   =", bh, flush=True)
    print("\n=== 검증 ===", flush=True)
    for sd in range(3):
        print("seed%d  " % sd + "  ".join(verify(tc, bh, seed=sd)), flush=True)
