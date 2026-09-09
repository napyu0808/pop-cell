"""Validate the simulator against the user's real playtest reports."""
from sim import Stats, WaveCfg, run_avg

CURVE30 = [1, 2, 5, 10, 12, 15, 20,
           23, 27, 31, 36, 41, 46, 51, 56, 62, 69, 77, 86,
           96, 108, 121, 135, 151, 168, 187, 208, 231, 258, 288]


def chain_sum(f, n=15):
    return sum(f(j) for j in range(n))


# ---- Anchor A: round-11 config, FULL upgrade -> wave 13, 239 kills, $4,519
A_w = WaveCfg(baseTimeLimit=20.0, hpGrowth=1.20, totalWaves=30,
              bossHp=100_000, goldCurve=CURVE30)
A_s = Stats(flatBonus=chain_sum(lambda j: 2 + j * 0.5),   # 82.5
            multBucketPercent=min(200, 3 * 15),           # 45
            critChance=min(0.75, 0.02 * 15),              # 0.30
            critMult=2.0 + 0.06 * 15,                     # 2.9
            attackInterval=max(0.15, 0.985 ** 15),        # 0.797
            goldMultPercent=6 * 15,                       # 90
            cursorRadius=0.45 + 0.03 * 15,                # 0.90
            bonusTimeSec=15.0,
            spawnIntervalMult=max(0.25, 0.94 ** 12),      # 0.476
            spawnCount=5, startWave=1)

# ---- Anchor B: round-12 config, FULL upgrade -> wave 23, 675 kills, $57,832
B_w = WaveCfg(baseTimeLimit=20.0, hpGrowth=1.14, totalWaves=30,
              bossHp=100_000, goldCurve=CURVE30)
B_s = Stats(flatBonus=chain_sum(lambda j: 4 + j * 2),     # 270
            multBucketPercent=min(200, 7 * 15),           # 105
            critChance=min(0.75, 0.04 * 15),              # 0.60
            critMult=2.0 + 0.16 * 15,                     # 4.4
            attackInterval=max(0.15, 0.96 ** 15),         # 0.542
            goldMultPercent=6 * 15,
            cursorRadius=0.45 + 0.10 * 15,                # 1.95
            bonusTimeSec=15.0,
            spawnIntervalMult=max(0.25, 0.94 ** 12),
            spawnCount=5, startWave=1)

REAL = {
    "A (round-11 full)": dict(wave=13, kills=239, gold=4519),
    "B (round-12 full)": dict(wave=23, kills=675, gold=57832),
}

for name, (s, w) in {"A (round-11 full)": (A_s, A_w),
                     "B (round-12 full)": (B_s, B_w)}.items():
    r = run_avg(s, w, seeds=8)
    real = REAL[name]
    print(f"\n=== {name} ===")
    print(f"  hit dmg {s.hit():7.1f}   atkInt {s.attackInterval:.3f}   R {s.cursorRadius:.2f}"
          f"   spawnInt x{s.spawnIntervalMult:.3f}  run {w.baseTimeLimit + s.bonusTimeSec:.0f}s")
    for k in ("wave", "kills", "gold"):
        sim, act = r[k], real[k]
        print(f"  {k:6s} sim {sim:9.1f}   real {act:8d}   ratio {sim/act:5.2f}")
