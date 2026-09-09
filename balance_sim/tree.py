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
