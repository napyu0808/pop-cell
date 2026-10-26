# -*- coding: utf-8 -*-
"""
출시 중인 C# 상수의 단일 출처(single source of truth).

  UpgradeTree.cs / GameConfig.cs 를 고치면 **이 파일만** 같이 고치면 된다.
  아래 self_check() 가 에디터에서 뽑은 실측값과 대조해주므로, 값이 어긋나면 바로 잡힌다.

  round38 기준. 이전 tree.py(CfgV2) 는 가지별 타입 순환(cyc)을 쓰던 구버전 트리라 더는 맞지 않는다.
"""

# ---- UpgradeTree.cs (round44: 150노드 + 가지 끝 반복 강화) -----------------
TIERS = 8
NODE_TIERS = 8          # round45: 일반 노드는 티어 0~7 (지역마다 새 노드)
REPEAT_TIER = 7         # 반복 강화는 마지막 티어

FLAT_BY_TIER = [20, 24, 30, 50, 88, 156, 290, 560]
# 비용은 맨 왼쪽 1~3자리만 0이 아니고 나머지는 0이다(사용자 규칙). 최고가는 1,000만 미만.
TIER_COST    = [50, 1500, 9800, 44000, 170000, 700000, 2600000, 9500000]

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
BOMB_FIELD_ADD, BOMB_FIELD_MAX = 2.0, 20.0
BOMB_UNLOCK_TIER = 1

# ---- 자동 타워(round50 개편) — UpgradeTree.cs 의 Tw* 상수 미러 ----
#   줄기마다 해금 → [공격력/공속/회전] 3갈래 → 셋 다 뚫으면 변신(화염 → 레이저 → 저격).
TOWER_UNLOCK_TIER = 0
# round54: 공격력 갈래는 커서 배수가 아니라 고정 공격력. 커서 배수는 0.5 고정(노드 없음).
TOWER_FLAT_AT = [100.0, 600.0, 10000.0]
TOWER_BOSS_ADD, TOWER_BOSS_MAX = 0.24, 1.2
TOWER_UNLOCK_COST = 500
TOWER_SPEED_MUL, TOWER_INTERVAL_MIN = 0.85, 0.10
TOWER_SPIN_ADD, TOWER_SPIN_MAX = 14.0, 300.0
TW_PER_BRANCH = 2
TW_STAGE_TIER = [0, 4, 5, 7]
TW_MORPH_TIER = [0, 4, 5, 6]
TOWER_KEYS = ["twL", "twC", "twR"]
PAT_SHOT, PAT_BURST, PAT_FLAME, PAT_LASER, PAT_SNIPER = 0, 1, 2, 3, 4

# 반복 강화 1회당 (UpgradeTree.Rep*) — 가격은 flat, mult, speed, range, critx, gold 순
REP_FLAT, REP_MULT, REP_APS, REP_RANGE, REP_CRITX, REP_GOLD = 10.0, 1.0, 0.05, 0.01, 0.02, 1.0
REP_INTERVAL_FLOOR, REP_RANGE_MAX = 0.035, 5.0
REP_COST = [9800000, 9800000, 9800000, 9800000, 9800000, 9800000]

# 가지 정의 (타입, 길이, 시작 티어) — UpgradeTree.L* 와 같은 값
#   길이를 0.6배로 줄여 트리가 332 -> 191 노드가 됐다.
LANES = {
    "flat":  ("Flat", 10, 0),  "mult":  ("Mult", 8, 0),   "speed": ("Speed", 8, 0),
    "critc": ("CritC", 6, 0),  "range": ("Range", 8, 0),  "critx": ("CritX", 9, 0),
    "time":  ("Time", 5, 0),   "gold":  ("Gold", 9, 0),   "skip":  ("Skip", 3, 0),
    "scount": ("SCount", 5, 0), "spawn": ("Spawn", 5, 0),
    "bombdmg": ("BombDmg", 4, 1), "bombrad": ("BombRad", 6, 1), "bombfreq": ("BombFreq", 3, 1),
    "bombtime": ("BombTime", 3, 1),
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
# 체력은 더 이상 독립 사다리가 아니다 — 그 지역에 들어오는 화력과 나가는 화력의 기하평균에
# 묶여 있다. 예전 표는 S6->S7 에서 체력이 33배 뛰는데 화력은 3.4배라 넘을 수 없는 벽이었다.
HP0      = [28, 117, 329, 523, 1090, 3450, 14300, 83000]
HPG      = [1.130, 1.136, 1.142, 1.148, 1.153, 1.158, 1.163, 1.168]
BOSSHP   = [4170, 17600, 49400, 78500, 164000, 517000, 2150000, 12400000]
QUOTA0   = [   4,    5,    6,    7,    8,    9,   10,   11]
SE       = [   8,    9,   10,   11,   12,   13,   14,   15]
# 골드는 "그 지역에서 사야 할 티어 비용 / 목표 판수" 에서 역산했다. 지역 간 격차가
# 389,675배에서 1,900배로 줄어, 후반 한 판이 앞 지역 전체를 덮어쓰지 않는다.
GOLDRATE = [0.05, 0.52, 0.57, 1.00, 4.00, 12.00, 45.00, 95.00]

# 시뮬에 없는 골드 수입(round55~57). 측정할 때는 goldrate 를 이만큼 올려서 대신한다.
#   연속 클리어  최대 +48%  (6%/회, 8회 상한)
#   콤보         최대 +20%  (0.5%/연속, 40 상한 — 자주 끊기니 실효는 +10~15%)
#   배양 연구소  초반 +40%, S2~S7 +9~21%, S8 +0.4%
#   정수 S4      골드 획득 +2%/레벨 (4지역 클리어 수만큼)
# round56 측정은 ×1.35 로 했다. 콤보가 붙은 round57 부터는 ×1.50 쯤이 맞다.
GOLD_OUTSIDE_SIM = 1.50

# 시뮬에 없는 화력(round57 보스 정수). 지역 보스 1클리어 = 그 지역 정수 1개,
# lv->lv+1 비용 = lv+1 개(최대 20레벨)라 "그 지역을 돈 횟수"가 곧 레벨이다.
#   S1 고정공 +100  S2 공속 +1%   S3 치명 +0.5%  S4 골드 +2%
#   S5 범위 +1.5%   S6 타워공 +250 S7 보스피해 +1.5% S8 전피해 +1%
# round57 판수(2.8시간 기준 7/6/9/16/39/56/96/23)로 어림하면 후반 실화력 +5~10%,
# 초반(S1~S3)은 체감이 훨씬 크다. 플레이타임은 2.8 -> 2.5시간 안팎으로 내려갈 전망.
DAMAGE_OUTSIDE_SIM = 1.08

# ---- 배양 연구소(GameManager) 미러 -----------------------------------------
CU_MINER_MAX   = 10
CU_CYCLE_BASE  = 4.0
CU_CYCLE_STEP  = 0.15
CU_CYCLE_MIN   = 1.1
CU_LOAD_BASE   = 10.0
CU_LOAD_MUL    = 1.55
CU_COST0       = [500.0, 800.0, 2000.0]  # 연구원 / 산출 / 속도
CU_COST_MUL    = [2.4, 1.35, 1.32]
CU_MAX_LV      = [CU_MINER_MAX, 25, 22]


def cu_income(lv):
    """초당 골드. lv = (연구원, 산출, 속도) 레벨.

    연구원은 0명에서 시작한다 — 레벨이 곧 인원이고, 안 사면 수입도 0이다.
    """
    workers = lv[0]
    cycle = max(CU_CYCLE_MIN, CU_CYCLE_BASE - CU_CYCLE_STEP * lv[2])
    load = CU_LOAD_BASE * CU_LOAD_MUL ** lv[1]
    return workers / cycle * load


def nice_cost(c):
    """GameManager.NiceCost 미러 — 맨 왼쪽 두 자리만 0이 아니고 나머지는 0."""
    import math
    if c <= 0:
        return 0
    e = math.floor(math.log10(c))
    scale = 10 ** max(0, e - 1)
    return int(round(c / scale) * scale)


def cu_cost(track, lv):
    return nice_cost(CU_COST0[track] * CU_COST_MUL[track] ** lv)


# 연속 클리어(최대 +48%) + 콤보(최대 +20%) 를 합친 어림 배수. 둘 다 자주 끊겨서 실효는 이 정도.
STREAK_COMBO = 1.45

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
    elif t == "BombTime":
        s.bombFieldSec = min(BOMB_FIELD_MAX, s.bombFieldSec + BOMB_FIELD_ADD)
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
    __slots__ = ("id", "parent", "tier", "cost", "type", "lane", "repeat", "fn", "also")

    def __init__(self, nid, parent, tier, t, lane, repeat=False, cost=None, fn=None, also=None):
        self.id, self.parent, self.tier, self.type, self.lane = nid, parent, tier, t, lane
        self.repeat = repeat
        self.cost = cost if cost is not None else cost_of(tier)
        self.fn = fn            # 타워처럼 효과가 표로 안 떨어지는 노드
        self.also = also        # 추가 선행 조건(변신)

    def apply(self, s):
        if self.fn is not None:
            self.fn(s)
        else:
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

    # 코어에서 뻗는 줄기 순서 = 방향(오른쪽·아래·왼쪽·위). 오른쪽 첫 노드는 고정 공격력.
    make("flat0", "root", "Flat", tier_of("flat", 0), "flat")
    rep("rmult", lane("mult", "flat0"), "RMult", 1)
    rep("rflat", lane("flat", "flat0", 1), "RFlat", 0)
    rep("rspeed", lane("speed", "flat0"), "RSpeed", 2)
    make("range0", "root", "Range", tier_of("range", 0), "range")
    lane("critc", "range0")
    rep("rrange", lane("range", "range0", 1), "RRange", 3)
    rep("rcritx", lane("critx", "range0"), "RCritX", 4)
    make("auto", "root", "Auto", 0, "auto")     # 왼쪽 줄기의 첫 노드. 골드 1 은 한 칸 뒤.
    make("gold0", "auto", "Gold", tier_of("gold", 0), "gold")
    lane("time", "gold0")
    rep("rgold", lane("gold", "gold0", 1), "RGold", 5)
    lane("skip", "gold0")
    make("spawn0", "root", "Spawn", tier_of("spawn", 0), "spawn")
    lane("scount", "spawn0"); lane("spawn", "spawn0", 1)
    make("bomb", "spawn0", "Bomb", BOMB_UNLOCK_TIER, "bomb")
    lane("bombdmg", "bomb"); lane("bombrad", "bomb"); lane("bombfreq", "bomb"); lane("bombtime", "bomb")
    # 자동 타워 — 코어에서 좌/중앙/우 3줄기(별도 탭)
    for i in range(3):
        key = TOWER_KEYS[i]
        unlock = key + "0"

        def on(s, i=i):
            s.towerOn[i] = True
        nodes.append(Node(unlock, "root", TOWER_UNLOCK_TIER, "TwOn", key, fn=on, cost=TOWER_UNLOCK_COST))

        stem = unlock
        for st in range(4):
            if st > 0:
                pat = PAT_FLAME if st == 1 else PAT_LASER if st == 2 else PAT_SNIPER

                def morph_fn(s, i=i, pat=pat):
                    s.towerPatMask[i] |= 1 << pat
                    s.towerPattern[i] = pat
                morph = key + "m" + str(st)
                nodes.append(Node(morph, stem + "s", TW_MORPH_TIER[st], "TwMorph", key,
                                  fn=morph_fn, also=[stem + "d", stem + "p"]))

                def boss_fn(s, i=i):          # 변신마다 보스 피해 배율 한 칸
                    s.towerBossMul[i] = min(TOWER_BOSS_MAX, s.towerBossMul[i] + TOWER_BOSS_ADD)
                nodes.append(Node(key + "bs" + str(st), morph, TW_MORPH_TIER[st], "TwBoss", key, fn=boss_fn))
                stem = morph
                if st == 3:
                    prevt = morph
                    for k in range(2):      # 저격 표적 +1 둘
                        def tf(s, i=i):
                            s.towerTargets[i] = min(3, s.towerTargets[i] + 1)
                        ttier = min(NODE_TIERS - 1, TW_MORPH_TIER[3] + k)
                        tid = key + "tgt" + str(k)
                        nodes.append(Node(tid, prevt, ttier, "TwTgt", key, fn=tf))
                        prevt = tid
                    break

            tier0 = TW_STAGE_TIER[st]
            flat_add = TOWER_FLAT_AT[min(st, len(TOWER_FLAT_AT) - 1)]
            for b, bk in enumerate(("d", "s", "p")):
                bp = stem
                for k in range(TW_PER_BRANCH):
                    tier = min(NODE_TIERS - 1, tier0 + (1 if k >= TW_PER_BRANCH - 2 else 0))
                    nid = stem + bk + ("" if k == TW_PER_BRANCH - 1 else str(k))
                    if b == 0:
                        def f(s, i=i, a=flat_add):
                            s.towerFlat[i] += a
                    elif b == 1:
                        def f(s, i=i):
                            s.towerInterval[i] = max(TOWER_INTERVAL_MIN, s.towerInterval[i] * TOWER_SPEED_MUL)
                    else:
                        def f(s, i=i):
                            s.towerSpin[i] = min(TOWER_SPIN_MAX, s.towerSpin[i] + TOWER_SPIN_ADD)
                    nodes.append(Node(nid, bp, tier, "Tw" + bk, key, fn=f))
                    bp = nid
            if st == 0:
                def burst(s, i=i):
                    s.towerPatMask[i] |= 1 << PAT_BURST
                    s.towerPattern[i] = PAT_BURST
                nodes.append(Node(stem + "burst", stem + "s", TW_STAGE_TIER[0] + 1, "TwBurst", key, fn=burst))
            else:
                def bf(s, i=i):             # 화염·레이저 분사구 +1
                    s.towerBeams[i] = min(3, s.towerBeams[i] + 1)
                bt = min(NODE_TIERS - 1, TW_STAGE_TIER[st] + 1)
                nodes.append(Node(stem + "beam", stem + "d", bt, "TwBeam", key, fn=bf))
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
    s.bombFieldSec = 10.0
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
EXPECTED_DPS = [126, 276, 582, 1188, 3768, 8319, 19799, 46580]
EXPECTED_HIT = [70, 116, 183, 298, 693, 1116, 1936, 3602]
EXPECTED_TOTAL_COST = 225_212_250  # 일반 노드만(반복 제외) — 2시간 재밸런싱
EXPECTED_NODES = 191   # 루트+커서 95 + 타워 90 + 반복 6


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
