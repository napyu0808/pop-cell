# -*- coding: utf-8 -*-
"""
출시 중인 C# 상수의 단일 출처(single source of truth).

  UpgradeTree.cs / GameConfig.cs 를 고치면 **이 파일만** 같이 고치면 된다.
  아래 self_check() 가 에디터에서 뽑은 실측값과 대조해주므로, 값이 어긋나면 바로 잡힌다.

  round38 기준. 이전 tree.py(CfgV2) 는 가지별 타입 순환(cyc)을 쓰던 구버전 트리라 더는 맞지 않는다.
"""

# ---- UpgradeTree.cs (round44: 150노드 + 가지 끝 반복 강화) -----------------
TIERS = 8
NODE_TIERS = 6          # 일반 노드는 티어 0~5 에 퍼진다
REPEAT_TIER = 5         # 반복 강화는 5지역 클리어 후

FLAT_BY_TIER = [20, 24, 30, 50, 88, 156]
TIER_COST    = [15, 20419, 386738, 2784988, 15023775, 226535461]

ROOT_FLAT    = 8.0
MULT_PP, MULT_CAP        = 21.0, 300.0
SPEED_MUL, INTERVAL_FLOOR = 0.826, 0.06
CRITC_PP, CRIT_CAP       = 0.08, 0.80
CRITX_ADD    = 0.18
RANGE_ADD    = 0.21
GOLD_PP      = 18.0
TIME_ADD, TIME_CAP       = 15.0, 120.0
SPAWN_MUL, SPAWN_FLOOR   = 0.85, 0.22
SCOUNT_MAX   = 10
SKIP_ADD     = 2
BOMB_DMG_ADD, BOMB_DMG_MAX = 0.5, 5.0
BOMB_RAD_ADD, BOMB_RAD_MAX = 0.10, 2.0
BOMB_INT_SUB, BOMB_INT_MIN = 2.0, 10.0
BOMB_UNLOCK_TIER = 1

# 반복 강화 1회당 (UpgradeTree.Rep*) — 가격은 flat, mult, speed, range, critx, gold 순
REP_FLAT, REP_MULT, REP_APS, REP_RANGE, REP_CRITX, REP_GOLD = 10.0, 1.0, 0.05, 0.01, 0.02, 1.0
REP_INTERVAL_FLOOR, REP_RANGE_MAX = 0.035, 5.0
REP_COST = [724381438, 724381438, 724381438, 724381438, 724381438, 724381438]

# 가지 정의 (타입, 길이, 시작 티어) — UpgradeTree.L* 와 같은 값
LANES = {
    "flat":  ("Flat", 17, 0),  "mult":  ("Mult", 14, 0),  "speed": ("Speed", 13, 0),
    "critc": ("CritC", 10, 0), "range": ("Range", 13, 0), "critx": ("CritX", 15, 0),
    "time":  ("Time", 8, 0),   "gold":  ("Gold", 15, 0),  "skip":  ("Skip", 3, 0),
    "scount": ("SCount", 9, 0), "spawn": ("Spawn", 9, 0),
    "bombdmg": ("BombDmg", 6, 1), "bombrad": ("BombRad", 10, 1), "bombfreq": ("BombFreq", 5, 1),
}

# ---- GameConfig.cs : Stats 기본값 --------------------------------------
BASE_ATTACK   = 10.0
BASE_INTERVAL = 0.72
BASE_CRITMULT = 1.5
BASE_CURSOR   = 0.45
BASE_SPAWNCNT = 1   # round42

# round42: 지역 체력 ×[1,1.8,3,3,3,3.5,4.2,9] (새 트리가 세서 지역 k 가 티어 k-1 을 필요로 하게)
# round43: 보스만 ×[1,1.3,1.3,3.2,4,5,6,8] — 보스가 안 맞던 버그가 고쳐져 보스전만 짧아진 만큼
# round44: 지역 골드 = '골드/체력' 효율이 지역마다 ×1.2 (tune42.gold_rates), 체력·가격은 autotune44(b)
# ---- GameConfig.cs : StageConfig.Stages --------------------------------
#            S1    S2    S3    S4    S5    S6    S7    S8
WAVES    = [   4,    5,    7,    9,   11,   13,   15,   18]
TLIM     = [  28,   34,   44,   54,   64,   74,   88,  108]
HP0      = [30, 303, 1808, 1550, 2758, 7337, 34965, 95492]
HPG      = [1.130, 1.136, 1.142, 1.148, 1.153, 1.158, 1.163, 1.168]
BOSSHP   = [1400, 28573, 217817, 1561256, 10121360, 102634452, 967233279, 5087827080]
QUOTA0   = [   4,    5,    6,    7,    8,    9,   10,   11]
SE       = [   8,    9,   10,   11,   12,   13,   14,   15]
GOLDRATE = [1.00, 7.56, 38.94, 34.13, 74.01, 259.15, 1696.56, 7003.12]

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


# ---- 노드 ----------------------------------------------------------------
def cost_of(tier):
    if 0 <= tier < len(TIER_COST):
        return TIER_COST[tier]
    return round(TIER_COST[-1] * 2.6 ** (tier - len(TIER_COST) + 1))


def flat_at(tier):
    if 0 <= tier < len(FLAT_BY_TIER):
        return FLAT_BY_TIER[tier]
    return round(FLAT_BY_TIER[-1] * 1.9 ** (tier - len(FLAT_BY_TIER) + 1))


def tier_of(key, k):
    _, ln, t0 = LANES[key]
    return t0 + (k * (NODE_TIERS - t0)) // ln


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
        s.cursorRadius += RANGE_ADD
    elif t == "Gold":
        s.goldMultPercent += GOLD_PP
    elif t == "Time":
        s.bonusTimeSec = min(TIME_CAP, s.bonusTimeSec + TIME_ADD)
    elif t == "Spawn":
        s.spawnIntervalMult = max(SPAWN_FLOOR, s.spawnIntervalMult * SPAWN_MUL)
    elif t == "SCount":
        s.spawnCount = min(SCOUNT_MAX, max(1, s.spawnCount + 1))
    elif t == "Skip":
        s.startWave += SKIP_ADD
    elif t == "Auto":
        s.autoAttack = True
    elif t == "Bomb":
        s.bombUnlocked = True
    elif t == "BombDmg":
        s.bombDmgMul = min(BOMB_DMG_MAX, s.bombDmgMul + BOMB_DMG_ADD)
    elif t == "BombRad":
        s.bombRadiusMul = min(BOMB_RAD_MAX, s.bombRadiusMul + BOMB_RAD_ADD)
    elif t == "BombFreq":
        s.bombInterval = max(BOMB_INT_MIN, s.bombInterval - BOMB_INT_SUB)
    # ---- 반복 강화 ----
    elif t == "RFlat":
        s.flatBonus += REP_FLAT
    elif t == "RMult":
        s.multBucketPercent += REP_MULT
    elif t == "RSpeed":
        s.attackInterval = max(REP_INTERVAL_FLOOR, 1.0 / (1.0 / s.attackInterval + REP_APS))
    elif t == "RRange":
        s.cursorRadius = min(REP_RANGE_MAX, s.cursorRadius + REP_RANGE)
    elif t == "RCritX":
        s.critMult += REP_CRITX
    elif t == "RGold":
        s.goldMultPercent += REP_GOLD


class Node:
    __slots__ = ("id", "parent", "tier", "cost", "type", "lane", "repeat")

    def __init__(self, nid, parent, tier, t, lane, repeat=False, cost=None):
        self.id, self.parent, self.tier, self.type, self.lane = nid, parent, tier, t, lane
        self.repeat = repeat
        self.cost = cost if cost is not None else cost_of(tier)

    def apply(self, s):
        apply_type(self.type, self.tier, s)


def build():
    """UpgradeTree.BuildAll 과 같은 순서·부모·티어. 루트 + 149 일반 + 반복 6."""
    nodes = [Node("root", None, 0, None, "root", cost=0)]

    def make(nid, parent, t, tier, lane):
        nodes.append(Node(nid, parent, tier, t, lane))

    def lane(key, parent, frm=0):
        t = LANES[key][0]
        for k in range(frm, LANES[key][1]):
            nid = "%s%d" % (key, k)
            make(nid, parent, t, tier_of(key, k), key)
            parent = nid
        return parent

    def rep(nid, parent, t, ci):
        nodes.append(Node(nid, parent, REPEAT_TIER, t, nid, repeat=True, cost=REP_COST[ci]))

    make("auto", "root", "Auto", 0, "auto")
    rep("rmult", lane("mult", "auto"), "RMult", 1)
    rep("rflat", lane("flat", "auto"), "RFlat", 0)
    rep("rspeed", lane("speed", "auto"), "RSpeed", 2)
    make("range0", "root", "Range", tier_of("range", 0), "range")
    lane("critc", "range0")
    rep("rrange", lane("range", "range0", 1), "RRange", 3)
    rep("rcritx", lane("critx", "range0"), "RCritX", 4)
    make("gold0", "root", "Gold", tier_of("gold", 0), "gold")
    lane("time", "gold0")
    rep("rgold", lane("gold", "gold0", 1), "RGold", 5)
    lane("skip", "gold0")
    make("spawn0", "root", "Spawn", tier_of("spawn", 0), "spawn")
    lane("scount", "spawn0"); lane("spawn", "spawn0", 1)
    make("bomb", "spawn0", "Bomb", BOMB_UNLOCK_TIER, "bomb")
    lane("bombdmg", "bomb"); lane("bombrad", "bomb"); lane("bombfreq", "bomb")
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
    s.bombUnlocked = False
    s.bombDmgMul = 2.0
    s.bombRadiusMul = 1.0
    s.bombInterval = 20.0
    return s


def stats_upto(tier_exclusive):
    """티어 0..tier_exclusive-1 을 전부 산 상태의 스탯 (코어 포함)."""
    s = new_stats()
    s.flatBonus += ROOT_FLAT
    for n in build():
        if n.type is not None and not n.repeat and n.tier < tier_exclusive:
            n.apply(s)
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
EXPECTED_DPS = [365, 1608, 5574, 21465, 79720, 225825]
EXPECTED_HIT = [127, 308, 593, 1209, 2302, 3806]
EXPECTED_TOTAL_COST = 5_013_736_521   # 일반 노드만(반복 강화 제외)
EXPECTED_NODES = 156   # 일반 150 + 반복 6


def self_check(verbose=True):
    ok = True
    lines = [" T |   타격   기대   |     DPS     기대   | 판정"]
    for t in range(1, NODE_TIERS + 1):
        s = stats_upto(t)
        h, d = avg_hit(s) / (1 + s.critChance * (s.critMult - 1)), dps(s)
        eh, ed = EXPECTED_HIT[t - 1], EXPECTED_DPS[t - 1]
        good = abs(h - eh) <= max(1.0, eh * 0.01) and abs(d - ed) <= max(1.0, ed * 0.01)
        ok &= good
        lines.append("T%d | %7.0f %6d  | %9.0f %7d | %s" % (t, h, eh, d, ed, "OK" if good else "*** 불일치 ***"))
    nodes = build()
    tot = sum(n.cost for n in nodes if not n.repeat)
    lines.append("\n트리 노드 %d개 (에디터 %d) · 총비용 %s (에디터 %s)"
                 % (len(nodes), EXPECTED_NODES, format(tot, ","), format(EXPECTED_TOTAL_COST, ",")))
    ok &= (tot == EXPECTED_TOTAL_COST) and len(nodes) == EXPECTED_NODES
    if verbose:
        print("\n".join(lines))
        print("\n=> 시뮬레이터가 현재 C# 트리와", "일치합니다." if ok else "어긋납니다!")
    return ok


if __name__ == "__main__":
    import sys
    sys.exit(0 if self_check() else 1)
