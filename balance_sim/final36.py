# -*- coding: utf-8 -*-
"""round36 최종 상수 — C# 에 그대로 박아 넣을 값."""
NPT = 20
#           F  M  S  C  X  R  G  T  P  N  A
TIER_MIX = [[5, 3, 4, 1, 1, 2, 2, 1, 0, 0, 1],
            [4, 3, 3, 1, 1, 3, 3, 1, 1, 0, 0],
            [4, 3, 3, 1, 1, 3, 3, 1, 1, 0, 0],
            [4, 3, 2, 1, 1, 3, 3, 1, 1, 1, 0],
            [5, 3, 1, 1, 1, 3, 3, 1, 1, 1, 0],
            [5, 4, 1, 1, 1, 3, 3, 0, 1, 1, 0],
            [5, 4, 1, 1, 1, 3, 3, 1, 1, 0, 0],
            [5, 4, 1, 1, 1, 3, 3, 1, 0, 1, 0]]
FLATV = [10, 12, 15, 25, 44, 78, 145, 280]     # 손으로 다듬은 티어별 Flat 값(단조 증가)
MULT_CAP, CRIT_CAP, INT_FLOOR = 300.0, 0.75, 0.06
BASE_ATK, CORE_FLAT, CRITX_BASE = 10.0, 8.0, 1.5
MULT_PER, SPEED_MUL, CRITC_PER, CRITX_PER = 11.0, 0.90, 0.06, 0.06

def sim():
    flat, mult, critc, critx, itv = CORE_FLAT, 0.0, 0.0, CRITX_BASE, 0.72
    out = []
    for t in range(8):
        m = TIER_MIX[t]
        flat += m[0] * FLATV[t]
        mult = min(MULT_CAP, mult + m[1] * MULT_PER)
        itv = max(INT_FLOOR, itv * (SPEED_MUL ** m[2]))
        critc = min(CRIT_CAP, critc + m[3] * CRITC_PER)
        critx += m[4] * CRITX_PER
        hit = (BASE_ATK + flat) * (1 + mult / 100.0)
        out.append(dict(T=t+1, hit=hit, crit=hit*critx, critc=critc, critx=critx, itv=itv,
                        avg=hit*(1+critc*(critx-1)), dps=hit*(1+critc*(critx-1))/itv))
    return out

rows = sim()
OLD_DPS = [210, 699, 1141, 3980, 11541, 36293, 48581, 48581]
TL   = [28, 34, 44, 54, 64, 74, 88, 108]
HP0  = [49, 107, 182, 224, 267, 312, 306, 260]
BOSS = [4400, 17600, 55000, 149600, 385000, 902000, 1870000, 3960000]
BOSS_FRAC = 0.33          # 보스가 제한시간에서 차지할 목표 비중 (기존 0.34~1.10)

print(" T |   일반타격    치명   편차 |     DPS   |  구DPS  | 배율")
for r in rows:
    print("T%d | %9.0f %8.0f x%.2f | %9.0f | %7d | %.2f" %
          (r['T'], r['hit'], r['crit'], r['critx'], r['dps'], OLD_DPS[r['T']-1], r['dps']/OLD_DPS[r['T']-1]))

print("\n=== 새 StageConfig ===")
print("S | enemyHp0 (구->신)   bossHp (구->신)        보스킬초  비중")
new_hp0, new_boss = [], []
for S in range(8):
    dps = rows[S]['dps']
    scale = dps / OLD_DPS[S]
    hp0 = int(round(HP0[S] * scale))
    bhp = int(round(BOSS_FRAC * TL[S] * dps / 100.0) * 100)
    new_hp0.append(hp0); new_boss.append(bhp)
    print("S%d| %4d -> %4d (x%.2f)   %9d -> %9d   %5.1f초  %2.0f%%" %
          (S+1, HP0[S], hp0, scale, BOSS[S], bhp, bhp/dps, 100*bhp/dps/TL[S]))
print("\nenemyHp0:", new_hp0)
print("bossHp  :", new_boss)

print("\n=== 환생 샤드 (클리어 지역 수 기준) ===")
SHARD = [0, 0, 2, 5, 10, 18, 30, 46, 70]
print("지역수:", list(range(9)))
print("샤드  :", SHARD)
print("지역당 증가:", [SHARD[i+1]-SHARD[i] for i in range(8)])
