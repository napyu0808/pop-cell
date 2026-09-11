"""Replica of UpgradeTree.BuildAll() with every balance number exposed for tuning."""
import math
from sim import Stats, WaveCfg

CHAINS = ["cf", "cm", "cs", "cc", "cx", "cr", "ug", "ut", "up"]
LABEL = {"cf": "공격력", "cm": "공격배수", "cs": "공격속도", "cc": "치명확률",
         "cx": "치명배수", "cr": "공격범위", "ug": "골드", "ut": "시간/스킵", "up": "소환"}


class Cfg:
    """All tunable balance knobs in one place."""
    def __init__(self, **kw):
        # --- tree shape / cost
        self.chain = 20
        self.cost_a, self.cost_b = 8.0, 1.6
        self.cost_table = None      # explicit per-depth cost (index 0 = depth 1)
        self.root_flat = 3.0
        # --- per-node effects
        self.flat = lambda j: 6 + j * 3        # 공격력
        self.mult_pp = 9.0                     # 공격배수 (%p, cap 200)
        self.interval_mul = 0.95               # 공격속도 (floor 0.15)
        self.crit_pp = 0.03                    # 치명확률 (cap 0.75)
        self.critmult_add = 0.2                # 치명배수
        self.cursor_add = 0.10                 # 공격범위
        self.gold_pp = 6.0                     # 골드 (%p)
        self.time_add = 3.0                    # 시간 (+초)
        self.time_cap = 55.0
        self.skip_at = {3: 5, 7: 10, 11: 15, 15: 20, 19: 25}
        self.spawn_mul = 0.94                  # 소환 간격 (floor 0.25)
        self.spawn_count_at = (4, 9, 14)
        # --- wave config
        self.baseTimeLimit = 40.0
        self.hpGrowth = 1.155
        self.baseEnemyHp = 10.0
        self.totalWaves = 40
        self.bossHp = 1_000_000
        self.baseQuota = 8
        self.quotaSlope = 2
        self.goldCurve = None                  # None -> auto from WaveCfg default
        self.goldMultApplies = True            # current code multiplies kill gold
        for k, v in kw.items():
            setattr(self, k, v)

    def cost(self, depth):                     # depth = 1..chain
        if self.cost_table is not None and 1 <= depth <= len(self.cost_table):
            return self.cost_table[depth - 1]
        return round(self.cost_a * self.cost_b ** depth)

    def wavecfg(self):
        kw = dict(baseTimeLimit=self.baseTimeLimit, hpGrowth=self.hpGrowth,
                  baseEnemyHp=self.baseEnemyHp, totalWaves=self.totalWaves,
                  bossHp=self.bossHp, baseQuota=self.baseQuota,
                  quotaSlope=self.quotaSlope)
        if self.goldCurve is not None:
            kw["goldCurve"] = list(self.goldCurve)
        return WaveCfg(**kw)


class Node:
    __slots__ = ("id", "chain", "j", "cost", "apply", "desc")

    def __init__(self, id, chain, j, cost, apply, desc):
        self.id, self.chain, self.j = id, chain, j
        self.cost, self.apply, self.desc = cost, apply, desc


def build(cfg):
    """-> list[Node]; chain 'root' is index 0."""
    L = [Node("root", "root", -1, 0,
              lambda s: setattr(s, "flatBonus", s.flatBonus + cfg.root_flat), "코어")]

    def mk(chain, j, apply, desc):
        L.append(Node(f"{chain}{j}", chain, j, cfg.cost(j + 1), apply, desc))

    for j in range(cfg.chain):
        v = cfg.flat(j)
        mk("cf", j, (lambda v: lambda s: setattr(s, "flatBonus", s.flatBonus + v))(v),
           f"고정 공격력 +{v}")
    for j in range(cfg.chain):
        mk("cm", j, lambda s: setattr(s, "multBucketPercent",
                                      min(200.0, s.multBucketPercent + cfg.mult_pp)),
           f"공격력 ×{1 + cfg.mult_pp/100:.2f}배")
    for j in range(cfg.chain):
        mk("cs", j, lambda s: setattr(s, "attackInterval",
                                      max(0.15, s.attackInterval * cfg.interval_mul)),
           f"타격 간격 ×{cfg.interval_mul}")
    for j in range(cfg.chain):
        mk("cc", j, lambda s: setattr(s, "critChance",
                                      min(0.75, s.critChance + cfg.crit_pp)),
           f"치명타 확률 +{cfg.crit_pp*100:.0f}%p")
    for j in range(cfg.chain):
        mk("cx", j, lambda s: setattr(s, "critMult", s.critMult + cfg.critmult_add),
           f"치명타 배수 +{cfg.critmult_add}")
    for j in range(cfg.chain):
        mk("cr", j, lambda s: setattr(s, "cursorRadius", s.cursorRadius + cfg.cursor_add),
           f"커서 범위 +{cfg.cursor_add}")
    for j in range(cfg.chain):
        mk("ug", j, lambda s: setattr(s, "goldMultPercent", s.goldMultPercent + cfg.gold_pp),
           f"골드 +{cfg.gold_pp:.0f}%")
    for j in range(cfg.chain):
        if j in cfg.skip_at:
            w = cfg.skip_at[j]
            mk("ut", j, (lambda w: lambda s: setattr(s, "startWave", max(s.startWave, w)))(w),
               f"웨이브 {w}부터 시작")
        else:
            mk("ut", j, lambda s: setattr(s, "bonusTimeSec",
                                          min(cfg.time_cap, s.bonusTimeSec + cfg.time_add)),
               f"제한시간 +{cfg.time_add:.0f}초")
    for j in range(cfg.chain):
        if j in cfg.spawn_count_at:
            mk("up", j, lambda s: setattr(s, "spawnCount", min(5, s.spawnCount + 1)),
               "동시 소환 +1")
        else:
            mk("up", j, lambda s: setattr(s, "spawnIntervalMult",
                                          max(0.25, s.spawnIntervalMult * cfg.spawn_mul)),
               f"소환 간격 ×{cfg.spawn_mul}")
    return L


def full_stats(cfg):
    """Stats with every node bought (F12 cheat equivalent)."""
    s = Stats()
    for n in build(cfg):
        n.apply(s)
    if not cfg.goldMultApplies:
        s.goldMultPercent = 0.0
    return s


def tree_total(cfg):
    return sum(n.cost for n in build(cfg))


# ============================================================
# POP Cell 2.0 — 새 트리(클러스터 + 혼합형 갈래, 160노드, tier별 20).
#   C# UpgradeTree.BuildAll() 을 그대로 복제. 비용은 tier_cost[tier].
# ============================================================
_BRANCHES_V2 = [
    ("ca", ["Flat", "Mult", "Speed", "CritC"]),
    ("cb", ["Mult", "CritX", "Flat", "Speed"]),
    ("cc", ["Speed", "Flat", "CritC", "Range"]),
    ("cd", ["Flat", "Range", "CritX", "Mult"]),
    ("ce", ["CritC", "Mult", "Flat", "Speed"]),
    ("ua", ["Auto", "Gold", "Time", "Range"]),
    ("ub", ["Time", "Spawn", "Gold", "CritC"]),
    ("uc", ["Gold", "Time", "SCount", "Range"]),
    ("ud", ["Spawn", "Gold", "Range", "Time"]),
]
NODES_PER_TIER = 20
TIERS = 8


class CfgV2:
    def __init__(self, **kw):
        self.tier_cost = [45, 420, 1400, 5500, 20000, 75000, 200000, 520000]
        self.root_flat = 8.0
        # per-node effect magnitudes (match UpgradeTree.cs Effect())
        self.flat = lambda d: 12 + d * 5
        self.mult_pp = 11.0
        self.interval_mul = 0.94
        self.interval_floor = 0.15
        self.crit_pp = 0.04
        self.critmult_add = 0.25
        self.cursor_add = 0.12
        self.gold_pp = 8.0
        self.time_add = 4.0
        self.time_cap = 60.0
        self.spawn_mul = 0.93
        self.spawn_floor = 0.22
        self.mult_cap = 240.0
        # wave config (Phase 1 still ends at the 40-wave boss)
        self.baseTimeLimit = 40.0
        self.hpGrowth = 1.17
        self.baseEnemyHp = 10.0
        self.totalWaves = 40
        self.bossHp = 1_000_000
        self.baseQuota = 8
        self.quotaSlope = 2
        self.goldCurve = [1, 2, 5, 10, 12, 15, 20,
                          21, 23, 24, 26, 27, 29, 31, 33, 35, 38, 40, 43,
                          45, 48, 51, 55, 58, 62, 66, 70, 75, 80, 85,
                          90, 96, 102, 109, 116, 123, 131, 140, 149, 158]
        self.goldMultApplies = True
        for k, v in kw.items():
            setattr(self, k, v)

    def wavecfg(self):
        return WaveCfg(baseTimeLimit=self.baseTimeLimit, hpGrowth=self.hpGrowth,
                       baseEnemyHp=self.baseEnemyHp, totalWaves=self.totalWaves,
                       bossHp=self.bossHp, baseQuota=self.baseQuota,
                       quotaSlope=self.quotaSlope, goldCurve=list(self.goldCurve))

    def cost(self, tier):
        if 0 <= tier < len(self.tier_cost):
            return self.tier_cost[tier]
        return round(self.tier_cost[-1] * 2.6 ** (tier - len(self.tier_cost) + 1))

    def _effect(self, t, d):
        c = self
        if t == "Flat":
            v = c.flat(d); return (f"공격력 +{v}", lambda s: setattr(s, "flatBonus", s.flatBonus + v))
        if t == "Mult":
            return ("배수", lambda s: setattr(s, "multBucketPercent", min(c.mult_cap, s.multBucketPercent + c.mult_pp)))
        if t == "Speed":
            return ("공속", lambda s: setattr(s, "attackInterval", max(c.interval_floor, s.attackInterval * c.interval_mul)))
        if t == "CritC":
            return ("치확", lambda s: setattr(s, "critChance", min(0.75, s.critChance + c.crit_pp)))
        if t == "CritX":
            return ("치배", lambda s: setattr(s, "critMult", s.critMult + c.critmult_add))
        if t == "Range":
            return ("범위", lambda s: setattr(s, "cursorRadius", s.cursorRadius + c.cursor_add))
        if t == "Gold":
            return ("골드", lambda s: setattr(s, "goldMultPercent", s.goldMultPercent + c.gold_pp))
        if t == "Time":
            return ("시간", lambda s: setattr(s, "bonusTimeSec", min(c.time_cap, s.bonusTimeSec + c.time_add)))
        if t == "Spawn":
            return ("소환가속", lambda s: setattr(s, "spawnIntervalMult", max(c.spawn_floor, s.spawnIntervalMult * c.spawn_mul)))
        if t == "SCount":
            return ("소환수", lambda s: setattr(s, "spawnCount", min(6, s.spawnCount + 1)))
        if t == "Skip":
            return ("스킵", lambda s: None)   # startWave: 아래 build_v2 에서 인덱스로 처리
        return ("auto", lambda s: setattr(s, "autoAttack", True))


class NodeV2(Node):
    __slots__ = ("tier", "parent")


def build_v2(cfg):
    L = [NodeV2("root", "root", -1, 0,
              (lambda v: lambda s: setattr(s, "flatBonus", s.flatBonus + v))(cfg.root_flat), "코어")]
    last = {b[0]: "root" for b in _BRANCHES_V2}
    added = 0
    skip_i = 0
    total = NODES_PER_TIER * TIERS
    depth = 0
    while added < total:
        for prefix, cycle in _BRANCHES_V2:
            if added >= total:
                break
            t = cycle[depth % len(cycle)]
            if t == "Auto" and depth > 0:
                t = "Range"
            desc, apply = cfg._effect(t, depth)
            if t == "Skip":
                skip_i += 1
                w = skip_i * 5
                apply = (lambda w: lambda s: setattr(s, "startWave", max(s.startWave, w)))(w)
                desc = f"웨이브 {w}부터"
            tier = added // NODES_PER_TIER
            nid = f"{prefix}{depth}"
            n = NodeV2(nid, prefix, depth, cfg.cost(tier), apply, desc)
            n.tier = tier
            n.parent = last[prefix]
            L.append(n)
            last[prefix] = nid
            added += 1
        depth += 1
    return L


def full_stats_v2(cfg):
    s = Stats()
    for n in build_v2(cfg):
        n.apply(s)
    if not cfg.goldMultApplies:
        s.goldMultPercent = 0.0
    return s


def tree_total_v2(cfg):
    return sum(n.cost for n in build_v2(cfg))
