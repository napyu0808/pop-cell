"""
POP Cell — game-loop simulator.
Replicates GameManager.Update / AttackPulse / SpawnEnemy exactly enough to
predict kills, gold and reached wave for a given Stats + WaveConfig.
"""
import math, random
import numpy as np

FIELD_W, FIELD_H = 16.0, 10.0
XMIN, XMAX = -FIELD_W / 2, FIELD_W / 2
YMIN, YMAX = -FIELD_H / 2, FIELD_H / 2


# ---------------------------------------------------------------- config
class WaveCfg:
    def __init__(self, **kw):
        self.baseQuota = 8
        self.baseTimeLimit = 40.0
        self.baseEnemyHp = 10.0
        self.hpGrowth = 1.155
        self.startEnemies = 14
        self.totalWaves = 40
        self.maxEnemies = 150
        self.bossHp = 1_000_000
        self.enemyRadius = 0.28
        self.sizeLo, self.sizeHi = 1.0, 2.0
        self.goldRate = 1.0
        self.goldCurve = [
            1, 2, 5, 10, 12, 15, 20,
            23, 27, 31, 36, 41, 46, 51, 56, 62, 69, 77, 86,
            96, 108, 121, 135, 151, 168, 187, 208, 231, 258, 288,
            320, 356, 396, 440, 489, 543, 603, 669, 742, 823,
        ]
        self.spawnBase = 0.50
        self.spawnSlope = 0.013
        self.spawnFloor = 0.16
        self.quotaSlope = 2
        for k, v in kw.items():
            setattr(self, k, v)

    def quota(self, w):
        return self.baseQuota + (w - 1) * self.quotaSlope

    def hp(self, w):
        return self.baseEnemyHp * (self.hpGrowth ** (w - 1))

    def gold(self, w):
        n = len(self.goldCurve)
        if w <= n:
            g = self.goldCurve[max(1, w) - 1]
        else:
            g = self.goldCurve[-1] * (1.10 ** (w - n))
        return max(1, round(g * self.goldRate))

    def spawn_interval(self, w):
        return max(self.spawnFloor, self.spawnBase - (w - 1) * self.spawnSlope)


class Stats:
    def __init__(self, **kw):
        self.baseAttack = 10.0
        self.flatBonus = 0.0
        self.multBucketPercent = 0.0
        self.critChance = 0.0
        self.critMult = 2.0
        self.attackInterval = 1.0
        self.goldMultPercent = 0.0
        self.cursorRadius = 0.45
        self.bonusTimeSec = 0.0
        self.spawnIntervalMult = 1.0
        self.spawnCount = 2
        self.startWave = 1
        for k, v in kw.items():
            setattr(self, k, v)

    def hit(self):
        return (self.baseAttack + self.flatBonus) * (1 + self.multBucketPercent / 100.0)

    def clone(self):
        s = Stats()
        s.__dict__.update(self.__dict__)
        return s


# ---------------------------------------------------------------- sim
class Run:
    """One cycle: StartRun() .. timeout or boss kill."""

    CAP = 400  # array capacity

    def __init__(self, stats, wcfg, rng, dt=1 / 60.0, cursor_speed=10.0, react=0.18,
                 aim_jitter=0.0):
        self.s, self.w, self.rng, self.dt = stats, wcfg, rng, dt
        self.cursor_speed, self.react = cursor_speed, react
        self.aim_jitter = aim_jitter   # 사람은 최적 중심을 정확히 못 짚는다 (월드 단위 오차)

    def _alloc(self):
        C = self.CAP
        self.px = np.zeros(C); self.py = np.zeros(C)
        self.vx = np.zeros(C); self.vy = np.zeros(C)
        self.er = np.zeros(C); self.hp = np.zeros(C)
        self.sw = np.zeros(C, dtype=np.int32)
        self.n = 0

    def _spawn(self, wave, boss=False):
        if boss:
            if self.n >= self.CAP:
                return
            i = self.n; self.n += 1
            self.px[i] = 0.0; self.py[i] = 0.0
            a = self.rng.random() * 2 * math.pi
            self.vx[i] = math.cos(a) * 0.35; self.vy[i] = math.sin(a) * 0.35
            self.er[i] = 2.2
            self.hp[i] = self.w.bossHp
            self.sw[i] = self.w.totalWaves
            self.boss_idx = i
            return
        if self.n >= self.w.maxEnemies or self.n >= self.CAP:
            return
        i = self.n; self.n += 1
        size = self.rng.uniform(self.w.sizeLo, self.w.sizeHi)
        r = self.w.enemyRadius * size
        self.er[i] = r
        self.hp[i] = math.ceil(self.w.hp(wave) * size)
        self.sw[i] = wave
        self.px[i] = self.rng.uniform(XMIN + r, XMAX - r)
        self.py[i] = self.rng.uniform(YMIN + r, YMAX - r)
        sp = self.rng.uniform(0.25, 0.65)
        a = self.rng.random() * 2 * math.pi
        self.vx[i] = math.cos(a) * sp; self.vy[i] = math.sin(a) * sp

    def _remove(self, i):
        """Mirror List.RemoveAt(i): shift the tail down by one."""
        last = self.n - 1
        if i != last:
            for arr in (self.px, self.py, self.vx, self.vy, self.er, self.hp, self.sw):
                arr[i:last] = arr[i + 1:last + 1]
            if getattr(self, "boss_idx", -1) > i:
                self.boss_idx -= 1
        self.n = last

    def _pick_target(self):
        n = self.n
        if n == 0:
            return 0.0, 0.0
        bi = getattr(self, "boss_idx", -1)
        if bi >= 0:
            return self.px[bi], self.py[bi]
        X = self.px[:n]; Y = self.py[:n]
        R = self.s.cursorRadius + self.er[:n]
        # count[j] = #enemies i within (R_i) of candidate j  (candidates = enemy positions)
        dx = X[:, None] - X[None, :]
        dy = Y[:, None] - Y[None, :]
        d2 = dx * dx + dy * dy
        inside = d2 <= (R[:, None] ** 2)
        cnt = inside.sum(axis=0).astype(float)
        # discount travel time so the cursor doesn't teleport across the field
        dist = np.hypot(X - self.cx, Y - self.cy)
        score = cnt / (1.0 + (dist / self.cursor_speed) / 0.45)
        j = int(np.argmax(score))
        tx, ty = X[j], Y[j]
        if self.aim_jitter > 0:
            a = self.rng.random() * 2 * math.pi
            m = self.aim_jitter * math.sqrt(self.rng.random())
            tx += math.cos(a) * m; ty += math.sin(a) * m
        return tx, ty

    def play(self, verbose=False):
        s, w, rng = self.s, self.w, self.rng
        self._alloc()
        self.boss_idx = -1
        wave = min(max(s.startWave, 1), w.totalWaves)
        time_left = w.baseTimeLimit + s.bonusTimeSec
        kills = 0
        quota = w.quota(wave)
        spawn_interval = w.spawn_interval(wave) * s.spawnIntervalMult
        atk_t = 0.0; spawn_t = 0.0; react_t = 0.0
        self.cx = self.cy = 0.0
        self.tx = self.ty = 0.0
        total_kills = 0; total_gold = 0
        hit = s.hit()
        gmul = 1 + s.goldMultPercent / 100.0
        per_spawn = max(2, s.spawnCount)
        won = False
        elapsed = 0.0

        if wave >= w.totalWaves:
            self._spawn(wave, boss=True)
        else:
            for _ in range(w.startEnemies):
                self._spawn(wave)

        dt = self.dt
        while time_left > 0:
            time_left -= dt; elapsed += dt

            # --- spawn
            if wave < w.totalWaves:
                spawn_t += dt
                g = 0
                while spawn_t >= spawn_interval and g < 24:
                    spawn_t -= spawn_interval; g += 1
                    for _ in range(per_spawn):
                        self._spawn(wave)
                if self.n >= w.maxEnemies:
                    spawn_t = min(spawn_t, spawn_interval)

            # --- move + bounce (vectorized)
            n = self.n
            if n:
                self.px[:n] += self.vx[:n] * dt
                self.py[:n] += self.vy[:n] * dt
                r = self.er[:n]
                lo = XMIN + r; hi = XMAX - r
                m = self.px[:n] < lo
                self.px[:n][m] = lo[m]; self.vx[:n][m] *= -1
                m = self.px[:n] > hi
                self.px[:n][m] = hi[m]; self.vx[:n][m] *= -1
                lo = YMIN + r; hi = YMAX - r
                m = self.py[:n] < lo
                self.py[:n][m] = lo[m]; self.vy[:n][m] *= -1
                m = self.py[:n] > hi
                self.py[:n][m] = hi[m]; self.vy[:n][m] *= -1

            # --- cursor
            react_t += dt
            if react_t >= self.react:
                react_t = 0.0
                self.tx, self.ty = self._pick_target()
            ddx = self.tx - self.cx; ddy = self.ty - self.cy
            d = math.hypot(ddx, ddy)
            step = self.cursor_speed * dt
            if d <= step or d == 0:
                self.cx, self.cy = self.tx, self.ty
            else:
                self.cx += ddx / d * step; self.cy += ddy / d * step

            # --- attack pulses
            atk_t += dt
            g = 0
            while atk_t >= s.attackInterval and g < 10:
                atk_t -= s.attackInterval; g += 1
                n = self.n
                if n == 0:
                    continue
                dx = self.px[:n] - self.cx
                dy = self.py[:n] - self.cy
                rr = s.cursorRadius + self.er[:n]
                inrange = np.nonzero(dx * dx + dy * dy <= rr * rr)[0]
                # AttackPulse walks i = count-1 .. 0
                for i in inrange[::-1]:
                    i = int(i)
                    dmg = hit * (s.critMult if rng.random() < s.critChance else 1.0)
                    self.hp[i] -= dmg
                    if self.hp[i] <= 0:
                        was_boss = (i == self.boss_idx)
                        total_gold += round(w.gold(int(self.sw[i])) * gmul)
                        self._remove(i)
                        kills += 1; total_kills += 1
                        if was_boss:
                            self.boss_idx = -1
                            won = True
                            break
                        if kills >= quota:
                            if wave < w.totalWaves:
                                wave += 1
                                kills = 0
                                quota = w.quota(wave)
                                spawn_interval = w.spawn_interval(wave) * s.spawnIntervalMult
                                if wave >= w.totalWaves and self.boss_idx < 0:
                                    self._spawn(wave, boss=True)
                            break
                if won:
                    break
            if won:
                break

        return dict(kills=total_kills, gold=total_gold, wave=wave, won=won,
                    elapsed=elapsed, enemies_left=self.n)


def run_once(stats, wcfg, seed=0, **kw):
    return Run(stats, wcfg, random.Random(seed), **kw).play()


def run_avg(stats, wcfg, seeds=5, **kw):
    rs = [run_once(stats, wcfg, seed=i, **kw) for i in range(seeds)]
    return dict(
        kills=sum(r["kills"] for r in rs) / len(rs),
        gold=sum(r["gold"] for r in rs) / len(rs),
        wave=sum(r["wave"] for r in rs) / len(rs),
        won=sum(1 for r in rs if r["won"]) / len(rs),
    )
