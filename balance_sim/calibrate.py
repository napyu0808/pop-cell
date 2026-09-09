"""Calibrate the cursor/aim model so the sim reproduces both real playtests."""
import itertools
from sim import Stats, WaveCfg, run_avg

CURVE30 = [1, 2, 5, 10, 12, 15, 20,
           23, 27, 31, 36, 41, 46, 51, 56, 62, 69, 77, 86,
           96, 108, 121, 135, 151, 168, 187, 208, 231, 258, 288]


def cs(f, n=15):
    return sum(f(j) for j in range(n))


# goldMultPercent = 0: the build at the time did NOT multiply kill gold
A_w = WaveCfg(baseTimeLimit=20.0, hpGrowth=1.20, totalWaves=30, goldCurve=CURVE30)
A_s = Stats(flatBonus=cs(lambda j: 2 + j * 0.5), multBucketPercent=45,
            critChance=0.30, critMult=2.9, attackInterval=0.985 ** 15,
            goldMultPercent=0, cursorRadius=0.90, bonusTimeSec=15.0,
            spawnIntervalMult=0.94 ** 12, spawnCount=5)

B_w = WaveCfg(baseTimeLimit=20.0, hpGrowth=1.14, totalWaves=30, goldCurve=CURVE30)
B_s = Stats(flatBonus=cs(lambda j: 4 + j * 2), multBucketPercent=105,
            critChance=0.60, critMult=4.4, attackInterval=0.96 ** 15,
            goldMultPercent=0, cursorRadius=1.95, bonusTimeSec=15.0,
            spawnIntervalMult=0.94 ** 12, spawnCount=5)

ANCHORS = [("A", A_s, A_w, dict(wave=13, kills=239, gold=4519)),
           ("B", B_s, B_w, dict(wave=23, kills=675, gold=57832))]

best = None
print(f"{'spd':>5} {'react':>6} {'jit':>5} | {'A k':>7} {'A g':>8} | {'B k':>7} {'B g':>8} | err")
for spd, react, jit in itertools.product([4.0, 6.0, 8.0, 10.0], [0.18, 0.30, 0.45], [0.0, 0.35, 0.7]):
    kw = dict(cursor_speed=spd, react=react, aim_jitter=jit)
    err = 0.0
    row = []
    for _, s, w, real in ANCHORS:
        r = run_avg(s, w, seeds=6, **kw)
        for k in ("kills", "gold"):
            import math
            err += math.log(r[k] / real[k]) ** 2
        row += [r["kills"], r["gold"]]
    print(f"{spd:5.1f} {react:6.2f} {jit:5.2f} | {row[0]:7.0f} {row[1]:8.0f} | "
          f"{row[2]:7.0f} {row[3]:8.0f} | {err:.4f}")
    if best is None or err < best[0]:
        best = (err, spd, react, jit)

print("\nBEST:", best)
