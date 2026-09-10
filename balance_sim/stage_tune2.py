"""
스테이지 튜너 v2 — TierCost 를 '측정된 수입'에서 역산해 목표 클리어타임을 보장.

각 스테이지 T (idx = T-1):
  1. 진입 시점 스탯(이전 tier 전부) + 고정 goldRate 로 '스테이지 1런 수입' I 측정.
  2. farm 시간 = TARGET_MIN[idx]*0.62, 보스푸시 = *0.38.
  3. 살 노드 20개(tier idx). farm 런 수 R = farm_secs / (TLIM+SHOP).
     ring = I_avg * R * 0.9  (평균수입은 스탯 오르며 커지므로 초반 I 의 ~1.4배로 가정 -> *0.9 보정)
     TierCost[idx] = round(ring / 20) (읽기 좋게 반올림)
  4. bossHp: '이 tier 풀강 DPS' 로 (보스푸시초 * 0.9) 만에 잡히게. 풀강-5노드로는 확인 후 필요시 소폭(*1.1, 최대 2회) 상향.
  5. 확정값으로 실제 진행시켜 다음 스테이지 유산 생성.
"""
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
GOLDRATE = [0.9, 1.0, 1.1, 1.2, 1.35, 1.5, 1.7, 1.9]   # 완만한 스테이지 골드 램프


def _stats(nodes, bought):
    s = Stats()
    for n in nodes:
        if n.id in bought:
            n.apply(s)
    return s


def _wc(idx, bosshp, gr):
    from tree import CfgV2
    gc0 = CfgV2().goldCurve
    gc = [max(1, round(v * gr)) for v in gc0]
    w = WaveCfg(baseTimeLimit=TLIM[idx], hpGrowth=HPG[idx], baseEnemyHp=HP0[idx],
               totalWaves=WAVES[idx], bossHp=bosshp, baseQuota=QUOTA0[idx],
               quotaSlope=2, goldCurve=gc)
    w.startEnemies = SE[idx]
    w.spawnBase = 0.55
    return w


def nice(x):
    if x < 100: return max(10, int(round(x / 5) * 5))
    if x < 10000: return int(round(x / 50) * 50)
    if x < 1_000_000: return int(round(x / 1000) * 1000)
    return int(round(x / 10000) * 10000)


def tune(seed=0, verbose=True):
    tier_cost = [40] * 8
    boss_hp = [1000] * 8
    # 초기 트리로 시작(값 채워가며 rebuild)
    for idx in range(8):
        cfg = CfgV2(tier_cost=list(tier_cost))
        nodes = build_v2(cfg)
        T = idx + 1
        # 진입 상태: tier < idx 전부
        entry = {"root"}
        for n in nodes:
            if n.id != "root" and n.tier < idx:
                entry.add(n.id)
        s_entry = _stats(nodes, entry)
        # 이 tier 풀강 상태
        full = set(entry)
        for n in nodes:
            if n.id != "root" and n.tier == idx:
                full.add(n.id)
        s_full = _stats(nodes, full)
        dps_full = s_full.hit() * (1 / max(0.15, s_full.attackInterval)) * (1 + s_full.critChance * (s_full.critMult - 1))

        farm_secs = TARGET_MIN[idx] * 60 * 0.62
        push_secs = TARGET_MIN[idx] * 60 * 0.38
        boss_hp[idx] = nice(dps_full * push_secs * 0.9 / max(1, WAVES[idx] * 0.0 + 1))  # 단일타겟
        boss_hp[idx] = nice(dps_full * (push_secs * 0.5))   # 보스 1회 처치는 push_secs 절반쯤

        # 수입 측정: 진입 스탯 & 풀강 스탯 두 지점 평균 -> 그 스테이지 평균 런수입
        wc = _wc(idx, boss_hp[idx], GOLDRATE[idx])
        inc_lo = statistics.mean(round(run_once(s_entry, wc, seed=seed*13+k, dt=DT, **AIM)["gold"] * GOLD_FIX) for k in range(4))
        inc_hi = statistics.mean(round(run_once(s_full, wc, seed=seed*17+k, dt=DT, **AIM)["gold"] * GOLD_FIX) for k in range(4))
        inc_avg = (inc_lo * 0.45 + inc_hi * 0.55)
        R = farm_secs / (TLIM[idx] + SHOP)
        ring = inc_avg * R * 0.92
        tier_cost[idx] = max(tier_cost[idx-1] if idx else 30, nice(ring / 20))
        if verbose:
            print("  S%d  tierDPS(full) %8.0f  inc %6.0f~%6.0f  farmR %4.1f  -> ring $%s  TierCost $%s  bossHp $%s"
                  % (T, dps_full, inc_lo, inc_hi, R, f"{round(ring):,}", f"{tier_cost[idx]:,}", f"{boss_hp[idx]:,}"))
    return tier_cost, boss_hp, GOLDRATE


def verify(tier_cost, boss_hp, gr, seed=0):
    cfg = CfgV2(tier_cost=list(tier_cost))
    nodes = build_v2(cfg)
    bought = {"root"}
    gold = 0
    cum = 0
    out = []
    for idx in range(8):
        T = idx + 1
        wc = _wc(idx, boss_hp[idx], gr[idx])
        secs, runs = 0.0, 0
        while runs < 250:
            s = _stats(nodes, bought)
            r = run_once(s, wc, seed=seed*777+T*53+runs*9, dt=DT, **AIM)
            gold += round(r["gold"] * GOLD_FIX)
            secs += r["elapsed"]; runs += 1
            if r["won"]:
                break
            secs += SHOP
            while True:
                cand = [n for n in nodes if n.id not in bought and n.id != "root"
                        and n.parent in bought and n.tier < T and n.cost <= gold]
                if not cand: break
                pick = min(cand, key=lambda n: (n.cost, n.chain))
                gold -= pick.cost; bought.add(pick.id)
        cum += secs / 60
        out.append((T, secs/60, runs, len(bought)-1, gold, runs < 250))
    return out


if __name__ == "__main__":
    tc, bh, gr = tune(seed=0)
    print("\nTierCost =", tc)
    print("bossHp   =", bh)
    print("goldRate =", gr)
    print("\n=== 검증 ===")
    for sd in range(3):
        rows = verify(tc, bh, gr, seed=sd)
        cum = 0; parts = []
        for (T, m, runs, nb, gold, cl) in rows:
            cum += m
            parts.append("S%d %.0f(누적%.0f)%s" % (T, m, cum, "" if cl else "✗"))
        print("seed%d  " % sd + "  ".join(parts))
