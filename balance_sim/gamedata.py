# -*- coding: utf-8 -*-
"""
출시 중인 C# 상수의 단일 출처(single source of truth).

  UpgradeTree.cs / GameConfig.cs 를 고치면 **이 파일만** 같이 고치면 된다.
  아래 self_check() 가 에디터에서 뽑은 실측값과 대조해주므로, 값이 어긋나면 바로 잡힌다.

  round38 기준. 이전 tree.py(CfgV2) 는 가지별 타입 순환(cyc)을 쓰던 구버전 트리라 더는 맞지 않는다.
"""

# ---- UpgradeTree.cs ----------------------------------------------------
NODES_PER_TIER = 20
TIERS = 8

# 티어별 노드 구성 (합계는 항상 20) — UpgradeTree.TierMix 와 순서까지 동일하게
#        Flat Mult Speed CritC CritX Range Gold Time Spawn SCount Auto
TIER_MIX = [
    [5, 3, 4, 1, 1, 2, 2, 1, 0, 0, 1],   # t0
    [4, 3, 3, 1, 1, 3, 3, 1, 1, 0, 0],   # t1
    [4, 3, 3, 1, 1, 3, 3, 1, 1, 0, 0],   # t2
    [4, 3, 2, 1, 1, 3, 3, 1, 1, 1, 0],   # t3
    [5, 3, 1, 1, 1, 3, 3, 1, 1, 1, 0],   # t4
    [5, 4, 1, 1, 1, 3, 3, 0, 1, 1, 0],   # t5
    [5, 4, 1, 1, 1, 3, 3, 1, 1, 0, 0],   # t6
    [5, 4, 1, 1, 1, 3, 3, 1, 0, 1, 0],   # t7
]
MIX_ORDER = ["Flat", "Mult", "Speed", "CritC", "CritX",
             "Range", "Gold", "Time", "Spawn", "SCount", "Auto"]

FLAT_BY_TIER = [10, 12, 15, 25, 44, 78, 145, 280]
TIER_COST    = [15, 700, 2300, 9000, 38000, 145000, 380000, 880000]

ROOT_FLAT    = 8.0
MULT_PP      = 11.0
MULT_CAP     = 300.0
SPEED_MUL    = 0.90
INTERVAL_FLOOR = 0.06
CRITC_PP     = 0.06
CRIT_CAP     = 0.75
CRITX_ADD    = 0.06
CURSOR_ADD   = 0.12
GOLD_PP      = 8.0
TIME_ADD     = 4.0
TIME_CAP     = 60.0
SPAWN_MUL    = 0.93
SPAWN_FLOOR  = 0.22
SCOUNT_MAX   = 6

# ---- GameConfig.cs : Stats 기본값 --------------------------------------
BASE_ATTACK   = 10.0
BASE_INTERVAL = 0.72
BASE_CRITMULT = 1.5
BASE_CURSOR   = 0.45
BASE_SPAWNCNT = 2

# ---- GameConfig.cs : StageConfig.Stages --------------------------------
#            S1    S2    S3    S4    S5    S6    S7    S8
WAVES    = [   4,    5,    7,    9,   11,   13,   15,   18]
TLIM     = [  28,   34,   44,   54,   64,   74,   88,  108]
HP0      = [  46,   92,  245,  210,  205,  190,  335,  705]
HPG      = [1.130, 1.136, 1.142, 1.148, 1.153, 1.158, 1.163, 1.168]
BOSSHP   = [1400, 6700, 22700, 66100, 188100, 531600, 1544500, 4695300]
QUOTA0   = [   4,    5,    6,    7,    8,    9,   10,   11]
SE       = [   8,    9,   10,   11,   12,   13,   14,   15]
GOLDRATE = [1.00, 0.70, 0.82, 0.95, 1.05, 1.15, 1.30, 1.55]

GOLD_CURVE = [
    1, 2, 5, 10, 12, 15, 20,
    21, 23, 24, 26, 27, 29, 31, 33, 35, 38, 40, 43,
    45, 48, 51, 55, 58, 62, 66, 70, 75, 80, 85,
    90, 96, 102, 109, 116, 123, 131, 140, 149, 158,
]

# 변이(Ascension) 배율 — WaveConfig.AscMobHpMul / AscBossHpMul / AscGoldMul
ASC_MOB, ASC_BOSS, ASC_GOLD = 1.12, 1.05, 0.95

# 보스 웨이브에도 잡몹이 나온다(round36). 소환 간격 배율.
BOSS_WAVE_SPAWN_SLOW = 2.0


# ---- 티어 구성표 -> 실제 노드 타입 배열 --------------------------------
def build_tier_types(tier):
    """UpgradeTree.BuildTierTypes 와 같은 '고르게 흩뿌리기' 배치."""
    res = [None] * NODES_PER_TIER
    used = [False] * NODES_PER_TIER
    for k, name in enumerate(MIX_ORDER):
        c = TIER_MIX[tier][k]
        for i in range(c):
            want = int((i + 0.5) * NODES_PER_TIER / c)
            want = max(0, min(NODES_PER_TIER - 1, want))
            p = want
            step = 1
            while used[p]:
                p = (want + step) % NODES_PER_TIER
                step += 1
            used[p] = True
            res[p] = name
    if tier == 0:                                  # 자동공격은 코어 바로 옆
        for i, t in enumerate(res):
            if t == "Auto":
                res[i] = res[0]
                res[0] = "Auto"
                break
    return res


def type_of(index):
    tier = min(index // NODES_PER_TIER, TIERS - 1)
    return build_tier_types(tier)[index % NODES_PER_TIER]


def cost_of(tier):
    if 0 <= tier < len(TIER_COST):
        return TIER_COST[tier]
    return round(TIER_COST[-1] * 2.6 ** (tier - len(TIER_COST) + 1))


def flat_at(tier):
    if 0 <= tier < len(FLAT_BY_TIER):
        return FLAT_BY_TIER[tier]
    return round(FLAT_BY_TIER[-1] * 1.9 ** (tier - len(FLAT_BY_TIER) + 1))


def apply_type(t, tier, s):
    """노드 효과를 Stats 객체에 적용 (UpgradeTree.Effect 와 동일)."""
    if t == "Flat":
        s.flatBonus += flat_at(tier)
    elif t == "Mult":
        s.multBucketPercent = min(MULT_CAP, s.multBucketPercent + MULT_PP)
    elif t == "Speed":
        s.attackInterval = max(INTERVAL_FLOOR, s.attackInterval * SPEED_MUL)
    elif t == "CritC":
        s.critChance = min(CRIT_CAP, s.critChance + CRITC_PP)
    elif t == "CritX":
        s.critMult += CRITX_ADD
    elif t == "Range":
        s.cursorRadius += CURSOR_ADD
    elif t == "Gold":
        s.goldMultPercent += GOLD_PP
    elif t == "Time":
        s.bonusTimeSec = min(TIME_CAP, s.bonusTimeSec + TIME_ADD)
    elif t == "Spawn":
        s.spawnIntervalMult = max(SPAWN_FLOOR, s.spawnIntervalMult * SPAWN_MUL)
    elif t == "SCount":
        s.spawnCount = min(SCOUNT_MAX, s.spawnCount + 1)
    elif t == "Auto":
        s.autoAttack = True


class Node:
    """경제 시뮬용 노드. 기하(가지 각도)는 밸런스에 영향이 없어 단순한 체인으로 둔다 —
    중요한 건 티어별 구성·비용·부모 관계(경계 구매 규칙)뿐."""
    __slots__ = ("id", "parent", "tier", "cost", "type")

    def __init__(self, nid, parent, tier, cost, t):
        self.id, self.parent, self.tier, self.cost, self.type = nid, parent, tier, cost, t

    def apply(self, s):
        apply_type(self.type, self.tier, s)


BRANCHES = 9   # C# 은 가지가 계속 갈라지지만, 경제적으로는 '동시에 열려 있는 경계 수'만 의미가 있다


def build():
    """루트 + 160 노드. 20개씩 티어가 올라가고, 노드는 BRANCHES 개의 체인에 라운드로빈."""
    root = Node("root", None, 0, 0, None)
    nodes = [root]
    last = ["root"] * BRANCHES
    for i in range(NODES_PER_TIER * TIERS):
        tier = i // NODES_PER_TIER
        b = i % BRANCHES
        n = Node("u%d" % i, last[b], tier, cost_of(tier), type_of(i))
        last[b] = n.id
        nodes.append(n)
    return nodes


def new_stats():
    from sim import Stats
    s = Stats()
    s.baseAttack = BASE_ATTACK
    s.flatBonus = 0.0
    s.multBucketPercent = 0.0
    s.critChance = 0.0
    s.critMult = BASE_CRITMULT
    s.attackInterval = BASE_INTERVAL
    s.goldMultPercent = 0.0
    s.cursorRadius = BASE_CURSOR
    s.bonusTimeSec = 0.0
    s.spawnIntervalMult = 1.0
    s.spawnCount = BASE_SPAWNCNT
    s.startWave = 1
    s.autoAttack = False
    return s


def stats_upto(tier_exclusive):
    """티어 0..tier_exclusive-1 을 전부 산 상태의 스탯 (코어 포함)."""
    s = new_stats()
    s.flatBonus += ROOT_FLAT
    for i in range(NODES_PER_TIER * min(tier_exclusive, TIERS)):
        apply_type(type_of(i), i // NODES_PER_TIER, s)
    return s


def avg_hit(s):
    hit = (s.baseAttack + s.flatBonus) * (1 + s.multBucketPercent / 100.0)
    return hit * (1 + s.critChance * (s.critMult - 1))


def dps(s):
    return avg_hit(s) / s.attackInterval


def wave_cfg(stage, asc=0):
    """그 지역의 WaveCfg (변이 배율 반영). sim.WaveCfg 를 돌려준다."""
    from sim import WaveCfg
    gr = GOLDRATE[stage] * (ASC_GOLD ** asc)
    gc = [max(1, round(v * gr)) for v in GOLD_CURVE]
    w = WaveCfg(baseTimeLimit=TLIM[stage],
                hpGrowth=HPG[stage],
                baseEnemyHp=HP0[stage] * (ASC_MOB ** asc),
                totalWaves=WAVES[stage],
                bossHp=round(BOSSHP[stage] * (ASC_BOSS ** asc)),
                baseQuota=QUOTA0[stage],
                quotaSlope=2,
                goldCurve=gc)
    w.startEnemies = SE[stage]
    w.spawnBase = 0.50
    w.bossWaveSpawnSlow = BOSS_WAVE_SPAWN_SLOW
    return w


# ---- 유니티 에디터 실측값과 대조 ---------------------------------------
# GameManager 가 살아있는 Play 모드에서 UpgradeTree.BuildAll() 로 뽑은 값 (round38).
EXPECTED_DPS = [198, 601, 1566, 3708, 8906, 21767, 53185, 131742]
EXPECTED_HIT = [90, 193, 350, 640, 1314, 2738, 5687, 11954]


def self_check(verbose=True):
    ok = True
    lines = [" T |   타격   기대   |     DPS     기대   | 판정"]
    for t in range(1, TIERS + 1):
        s = stats_upto(t)
        h, d = avg_hit(s) / (1 + s.critChance * (s.critMult - 1)), dps(s)
        eh, ed = EXPECTED_HIT[t - 1], EXPECTED_DPS[t - 1]
        good = abs(h - eh) <= max(1.0, eh * 0.01) and abs(d - ed) <= max(1.0, ed * 0.01)
        ok &= good
        lines.append("T%d | %7.0f %6d  | %9.0f %7d | %s" % (t, h, eh, d, ed, "OK" if good else "*** 불일치 ***"))
    tot = sum(n.cost for n in build())
    lines.append("\n트리 총비용 %s (에디터 실측 29,100,300)" % format(tot, ","))
    ok &= (tot == 29_100_300)
    if verbose:
        print("\n".join(lines))
        print("\n=> 시뮬레이터가 현재 C# 트리와", "일치합니다." if ok else "어긋납니다!")
    return ok


if __name__ == "__main__":
    import sys
    sys.exit(0 if self_check() else 1)
