"""
8-stage ramp balance check for POP Cell 2.0.

각 스테이지 T (1..8): tier < T 노드만 해금됨. 그 노드들을 balance_sim 진행 루프로
사서 스테이지 보스를 잡을 때까지 = 그 스테이지의 '클리어 타임'. 초반은 짧고 후반은 길게.
"""
import statistics
from sim import Stats, run_once, WaveCfg
from tree import build_v2, CfgV2
from prog import AIM, GOLD_FIX, SHOP_SEC


# ---- 스테이지 테이블 (튜닝 대상) ----
class Stage:
    def __init__(self, idx, waves, tlimit, hp0, hpg, bosshp, quota0=6, qslope=2,
                 start_enemies=10, spawn_base=0.55):
        self.idx = idx; self.waves = waves; self.tlimit = tlimit
        self.hp0 = hp0; self.hpg = hpg; self.bosshp = bosshp
        self.quota0 = quota0; self.qslope = qslope
        self.start_enemies = start_enemies; self.spawn_base = spawn_base

    def wavecfg(self, goldCurve):
        w = WaveCfg(baseTimeLimit=self.tlimit, hpGrowth=self.hpg, baseEnemyHp=self.hp0,
                    totalWaves=self.waves, bossHp=self.bosshp, baseQuota=self.quota0,
                    quotaSlope=self.qslope, goldCurve=list(goldCurve))
        w.startEnemies = self.start_enemies
        w.spawnBase = self.spawn_base
        return w


def default_stages():
    # 초반: 웨이브 적고 시간 짧고 약함 / 후반: 길고 강함.
    # bossHp ≈ tier(T-1) 단일타겟 DPS × 목표 보스전 초 (12초 -> 55초 램프)
    return [
        Stage(1,  3, 20,   6, 1.15,     1_400, quota0=4, start_enemies=7,  spawn_base=0.62),
        Stage(2,  4, 25,  10, 1.15,     5_500, quota0=5, start_enemies=8),
        Stage(3,  5, 30,  18, 1.15,    16_000, quota0=6, start_enemies=9),
        Stage(4,  6, 36,  35, 1.15,    38_000, quota0=7, start_enemies=10),
        Stage(5,  8, 42,  70, 1.15,    90_000, quota0=8, start_enemies=11),
        Stage(6, 10, 48, 140, 1.15,   225_000, quota0=9, start_enemies=12),
        Stage(7, 12, 54, 260, 1.15,   520_000, quota0=10, start_enemies=13),
        Stage(8, 16, 62, 480, 1.15, 1_800_000, quota0=11, start_enemies=14),
    ]


# tier < T 노드만 산다. cheapest 정책.
def _buyable(nodes, bought, tier_cap):
    return [n for n in nodes if n.id not in bought and n.id != "root"
            and n.parent in bought and n.tier < tier_cap]


def _stats_from(cfg, nodes, bought):
    s = Stats()
    for n in nodes:
        if n.id in bought:
            n.apply(s)
    return s


def clear_stage(cfg, nodes, bought, gold, stage, goldCurve, seed=0, cap_runs=200):
    """스테이지 하나를 클리어할 때까지. 반환: (secs, runs, gold남음, bought갱신)"""
    wc = stage.wavecfg(goldCurve)
    secs = 0.0
    runs = 0
    tier_cap = stage.idx  # tier 0..idx-1
    while runs < cap_runs:
        s = _stats_from(cfg, nodes, bought)
        r = run_once(s, wc, seed=seed * 777 + stage.idx * 97 + runs * 3, dt=1/60.0, **AIM)
        g = round(r["gold"] * GOLD_FIX)
        gold += g
        secs += r["elapsed"]
        runs += 1
        if r["won"]:
            return secs, runs, gold, bought
        secs += SHOP_SEC
        while True:
            cand = [n for n in _buyable(nodes, bought, tier_cap) if n.cost <= gold]
            if not cand:
                break
            pick = min(cand, key=lambda n: (n.cost, n.chain))
            gold -= pick.cost
            bought.add(pick.id)
    return secs, runs, gold, bought  # 못 깸


def run_campaign(cfg, stages, seed=0, verbose=True):
    nodes = build_v2(cfg)
    bought = {"root"}
    gold = 0
    total = 0.0
    rows = []
    gc = cfg.goldCurve
    for st in stages:
        secs, runs, gold, bought = clear_stage(cfg, nodes, bought, gold, st, gc, seed=seed)
        total += secs
        nb = len([b for b in bought if b != "root"])
        rows.append(dict(stage=st.idx, min=secs/60, runs=runs, cum=total/60,
                         nodes=nb, gold=gold, cleared=(runs < 200)))
        if verbose:
            c = "OK " if runs < 200 else "FAIL"
            print(f"  스테이지 {st.idx}: {c} {secs/60:5.2f}분  runs {runs:3d}  누적 {total/60:5.1f}분"
                  f"  노드 {nb:3d}/160  뱅크 ${gold:,}")
    return rows


if __name__ == "__main__":
    cfg = CfgV2()
    print("=== 현재 트리 + 기본 스테이지 테이블 ===")
    run_campaign(cfg, default_stages(), seed=0)
