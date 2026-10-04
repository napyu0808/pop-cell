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
        self.bossWaveSpawnSlow = 2.0   # round36: 보스 웨이브에도 잡몹이 나온다(간격 x2)
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
        self.critMult = 1.5          # GameConfig.cs Stats 기본값(round36 에서 2.0 -> 1.5)
        self.attackInterval = 0.72   # GameConfig.cs Stats 기본값과 일치(round23부터 0.72)
        self.goldMultPercent = 0.0
        self.cursorRadius = 0.45
        self.bonusTimeSec = 0.0
        self.spawnIntervalMult = 1.0
        self.spawnCount = 1          # round42: 기본 1 (노드로 최대 10)
        self.startWave = 1
        self.autoAttack = False
        # round42 백신 — GameConfig.cs Stats 와 동일
        self.bombUnlocked = False
        self.bombDmgMul = 2.0
        self.bombRadiusMul = 1.0
        self.bombInterval = 20.0
        # round47 자동 타워 — GameConfig.cs Stats 와 동일
        self.towerOn = [False, False, False]
        self.towerDmgMul = [0.5, 0.5, 0.5]
        self.towerInterval = [1.1, 1.1, 1.1]
        self.towerSpin = [52.0, 52.0, 52.0]
        self.towerPattern = [0, 0, 0]
        self.towerPatMask = [1, 1, 1]
        self.towerBeams = [1, 1, 1]
        self.towerTargets = [1, 1, 1]
        for k, v in kw.items():
            setattr(self, k, v)

    def hit(self):
        return (self.baseAttack + self.flatBonus) * (1 + self.multBucketPercent / 100.0)

    def bomb_boss_frac(self):
        return 0.01 + min(1.0, max(0.0, (self.bombDmgMul - 2.0) / 3.0)) * 0.04

    def clone(self):
        s = Stats()
        s.__dict__.update(self.__dict__)
        s.towerOn = list(self.towerOn)
        s.towerDmgMul = list(self.towerDmgMul)
        s.towerInterval = list(self.towerInterval)
        s.towerSpin = list(self.towerSpin)
        s.towerPattern = list(self.towerPattern)
        s.towerPatMask = list(self.towerPatMask)
        s.towerBeams = list(self.towerBeams)
        s.towerTargets = list(self.towerTargets)
        return s

    def any_tower(self):
        return any(self.towerOn)


# ---------------------------------------------------------------- sim
# round42: GameManager 상수 미러
ELITE_CHANCE, ELITE_HP, ELITE_GOLD = 0.07, 3.0, 5
BOMB_BASE_R, BOMB_MAX_ALIVE, BOMB_SIZE, BOMB_HP = 2.6, 2, 1.9, 0.6
# round47 자동 타워 — GameManager.cs 의 상수 미러
TOWER_POS = [(-5.2, 0.0), (0.0, 0.0), (5.2, 0.0)]
TOWER_SPIN, BULLET_SPEED, BULLET_LIFE, BULLET_R = 52.0, 9.0, 2.2, 0.16
BURST_SPREAD = 11.0      # 연발 부채꼴 각도
PAT_SHOT, PAT_BURST, PAT_FLAME, PAT_LASER, PAT_SNIPER = 0, 1, 2, 3, 4
FLAME_RANGE, FLAME_HALF_ANG = 4.2, 26.0
LASER_RANGE, LASER_HALF_W = 11.0, 0.42
MAX_BULLETS = 160        # C# 은 260 — 동시에 그만큼 뜨는 일은 없어 메모리만 줄였다


class Run:
    """One cycle: StartRun() .. timeout or boss kill.

    round42: C# AttackPulse 가 "죽은 적은 dead 표시만, 펄스 끝에 RemoveAll" 로 바뀌었다(백신 폭발 연쇄 대비).
    여기서도 똑같이 — 펄스 안에서는 인덱스가 안 움직이고, 펄스가 끝나면 _compact() 로 한꺼번에 뺀다.
    """

    CAP = 420  # array capacity (maxEnemies 150 + 캡슐 여유)

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
        self.el = np.zeros(C, dtype=bool)     # 변이개체
        self.bm = np.zeros(C, dtype=bool)     # 백신 캡슐
        self.dead = np.zeros(C, dtype=bool)
        self.n = 0
        # 타워 탄알
        B = MAX_BULLETS
        self.bux = np.zeros(B); self.buy = np.zeros(B)
        self.bvx = np.zeros(B); self.bvy = np.zeros(B)
        self.bulife = np.zeros(B); self.budmg = np.zeros(B)
        self.bn = 0
        self.tw_ang = [0.0, 0.0, 0.0]
        self.tw_t = [0.0, 0.0, 0.0]

    def _spawn(self, wave, boss=False, bomb=False):
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
            self.el[i] = False; self.bm[i] = False; self.dead[i] = False
            self.boss_idx = i
            return
        cap = self.w.maxEnemies + (BOMB_MAX_ALIVE if bomb else 0)
        if self.n >= cap or self.n >= self.CAP:
            return
        i = self.n; self.n += 1
        elite = (not bomb) and self.rng.random() < ELITE_CHANCE
        size = BOMB_SIZE if bomb else self.rng.uniform(self.w.sizeLo, self.w.sizeHi)
        r = self.w.enemyRadius * size
        self.er[i] = r
        self.hp[i] = math.ceil(self.w.hp(wave) * size * (ELITE_HP if elite else BOMB_HP if bomb else 1.0))
        self.sw[i] = wave
        self.el[i] = elite; self.bm[i] = bomb; self.dead[i] = False
        self.px[i] = self.rng.uniform(XMIN + r, XMAX - r)
        self.py[i] = self.rng.uniform(YMIN + r, YMAX - r)
        sp = self.rng.uniform(0.25, 0.65)
        a = self.rng.random() * 2 * math.pi
        self.vx[i] = math.cos(a) * sp; self.vy[i] = math.sin(a) * sp

    def _fire(self, ti, ang, dmg):
        if self.bn >= MAX_BULLETS:
            return
        k = self.bn; self.bn += 1
        rad = math.radians(ang)
        self.bux[k], self.buy[k] = TOWER_POS[ti]
        self.bvx[k] = math.sin(rad) * BULLET_SPEED
        self.bvy[k] = math.cos(rad) * BULLET_SPEED
        self.bulife[k] = BULLET_LIFE
        self.budmg[k] = dmg

    def _sniper_target(self):
        bi = getattr(self, "boss_idx", -1)
        if bi >= 0 and not self.dead[bi]:
            return bi
        best, best_hp = -1, 0.0
        for j in range(self.n):
            if self.dead[j] or self.bm[j]:
                continue
            if self.hp[j] > best_hp:
                best_hp = self.hp[j]; best = j
        return best

    def _area_hit(self, ti, dmg, shape, kill, ang_off=0.0):
        """화염(부채꼴) / 레이저(직선) — 닿는 적 전부. True = 펄스 중단."""
        tx, ty = TOWER_POS[ti]
        rad = math.radians(self.tw_ang[ti] + ang_off)
        dx_, dy_ = math.sin(rad), math.cos(rad)
        for j in range(self.n - 1, -1, -1):
            if self.dead[j]:
                continue
            ex, ey = self.px[j] - tx, self.py[j] - ty
            if shape == "cone":
                dist = math.hypot(ex, ey)
                if dist > FLAME_RANGE + self.er[j]:
                    continue
                if dist > 0.01:
                    cosang = (ex * dx_ + ey * dy_) / dist
                    if math.degrees(math.acos(max(-1.0, min(1.0, cosang)))) > FLAME_HALF_ANG:
                        continue
            else:
                along = ex * dx_ + ey * dy_
                if along < 0.0 or along > LASER_RANGE:
                    continue
                if abs(ex * dy_ - ey * dx_) > LASER_HALF_W + self.er[j]:
                    continue
            self.hp[j] -= dmg
            if self.hp[j] <= 0 and kill(j, False):
                return True
        return False

    def _sniper_shot(self, ti, dmg, kill, targets=1):
        j = self._sniper_target()
        if j < 0:
            return False
        tx, ty = TOWER_POS[ti]
        want = math.degrees(math.atan2(self.px[j] - tx, self.py[j] - ty))
        if abs((want - self.tw_ang[ti] + 540.0) % 360.0 - 180.0) > 6.0:
            return False
        picked = [j]
        while len(picked) < targets:
            best, best_hp = -1, 0.0
            for k in range(self.n):
                if self.dead[k] or self.bm[k] or k in picked:
                    continue
                if self.hp[k] > best_hp:
                    best_hp = self.hp[k]; best = k
            if best < 0:
                break
            picked.append(best)
        for k in picked:
            if self.dead[k]:
                continue
            self.hp[k] -= dmg
            if self.hp[k] <= 0 and kill(k, False):
                return True
        return False

    def _compact(self):
        """C# enemies.RemoveAll(x => x.dead) — 순서 유지."""
        n = self.n
        keep = np.nonzero(~self.dead[:n])[0]
        if len(keep) == n:
            return
        bi = getattr(self, "boss_idx", -1)
        new_bi = -1
        if bi >= 0 and not self.dead[bi]:
            new_bi = int(np.searchsorted(keep, bi))
        for arr in (self.px, self.py, self.vx, self.vy, self.er, self.hp, self.sw, self.el, self.bm, self.dead):
            arr[:len(keep)] = arr[keep]
        self.dead[:len(keep)] = False
        self.n = len(keep)
        self.boss_idx = new_bi

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
        # 백신 캡슐은 "터뜨리면 폭발 반경 안의 적 수"만큼의 가치 — 사람도 캡슐이 떼 근처면 노린다
        B = self.bm[:n]
        if B.any():
            brad = BOMB_BASE_R * self.s.bombRadiusMul
            within = d2 <= ((brad + self.er[:n])[:, None] ** 2)
            bval = within.sum(axis=0).astype(float)
            cnt = np.where(B, np.maximum(cnt, bval), cnt)
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
        st = {"wave": min(max(s.startWave, 1), w.totalWaves - 1),   # C#: 보스 웨이브로는 시작 안 함
              "kills": 0, "total_kills": 0, "total_gold": 0, "won": False}
        st["quota"] = w.quota(st["wave"])
        st["si"] = w.spawn_interval(st["wave"]) * s.spawnIntervalMult
        time_left = w.baseTimeLimit + s.bonusTimeSec
        atk_t = 0.0; spawn_t = 0.0; react_t = 0.0; bomb_t = 0.0
        self.cx = self.cy = 0.0
        self.tx = self.ty = 0.0
        hit = s.hit()
        gmul = 1 + s.goldMultPercent / 100.0
        per_spawn = max(1, s.spawnCount)
        elapsed = 0.0

        def kill(i, crit):
            """C# KillEnemy — True = 펄스 중단(보스 처치/웨이브 클리어)."""
            if self.dead[i]:
                return False
            self.dead[i] = True
            if self.bm[i]:
                return detonate(self.px[i], self.py[i])
            was_boss = (i == self.boss_idx)
            g = round(w.gold(int(self.sw[i])) * gmul)
            if self.el[i]:
                g *= ELITE_GOLD
            st["total_gold"] += g
            st["kills"] += 1; st["total_kills"] += 1
            if was_boss:
                st["won"] = True
                return True
            # round43: 실제로 웨이브가 넘어갈 때만 펄스 중단 — 보스 웨이브에서 잡몹 킬마다 펄스가 끊겨
            #   보스가 안 맞던 버그(C# KillEnemy 와 같이 고침)
            if st["kills"] >= st["quota"] and st["wave"] < w.totalWaves:
                st["wave"] += 1
                st["kills"] = 0
                st["quota"] = w.quota(st["wave"])
                st["si"] = w.spawn_interval(st["wave"]) * s.spawnIntervalMult
                if st["wave"] >= w.totalWaves and self.boss_idx < 0:
                    self._spawn(st["wave"], boss=True)
                return True
            return False

        def detonate(x, y):
            rad = BOMB_BASE_R * s.bombRadiusMul
            bhit = hit * s.bombDmgMul
            for j in range(self.n - 1, -1, -1):
                if self.dead[j]:
                    continue
                dx = self.px[j] - x; dy = self.py[j] - y
                rr = rad + self.er[j]
                if dx * dx + dy * dy > rr * rr:
                    continue
                if j == self.boss_idx:
                    self.hp[j] -= w.bossHp * s.bomb_boss_frac()
                else:
                    self.hp[j] -= bhit
                if self.hp[j] <= 0 and kill(j, False):
                    return True
            return False

        if st["wave"] >= w.totalWaves:
            self._spawn(st["wave"], boss=True)
        else:
            for _ in range(w.startEnemies):
                self._spawn(st["wave"])

        dt = self.dt
        while time_left > 0:
            time_left -= dt; elapsed += dt

            # --- spawn  (round36: 보스 웨이브에도 계속 나온다. 간격만 bossWaveSpawnSlow 배)
            si = st["si"] * (w.bossWaveSpawnSlow if st["wave"] >= w.totalWaves else 1.0)
            spawn_t += dt
            g = 0
            while spawn_t >= si and g < 24:
                spawn_t -= si; g += 1
                for _ in range(per_spawn):
                    self._spawn(st["wave"])
            if self.n >= w.maxEnemies:
                spawn_t = min(spawn_t, si)

            # --- 백신 캡슐 (노드로 해금된 뒤부터, 자체 타이머)
            if s.bombUnlocked:
                bomb_t += dt
                if bomb_t >= s.bombInterval:
                    alive = int(self.bm[:self.n].sum())
                    if alive < BOMB_MAX_ALIVE:
                        self._spawn(st["wave"], bomb=True); bomb_t = 0.0
                    else:
                        bomb_t = s.bombInterval

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

            # --- 자동 타워(round50) — 패턴마다 다르게 때린다
            if s.any_tower():
                for ti in range(3):
                    if not s.towerOn[ti]:
                        continue
                    pat = s.towerPattern[ti]
                    tx, ty = TOWER_POS[ti]

                    if pat == PAT_SNIPER:
                        j = self._sniper_target()
                        if j >= 0:
                            want = math.degrees(math.atan2(self.px[j] - tx, self.py[j] - ty))
                            step = max(90.0, s.towerSpin[ti] * 3.0) * dt
                            d = (want - self.tw_ang[ti] + 540.0) % 360.0 - 180.0
                            self.tw_ang[ti] += max(-step, min(step, d))
                    else:
                        self.tw_ang[ti] = (self.tw_ang[ti] + s.towerSpin[ti] * dt) % 360.0

                    iv = max(0.05, s.attackInterval) if pat in (PAT_FLAME, PAT_LASER)                          else max(0.08, s.towerInterval[ti])
                    self.tw_t[ti] += dt
                    guard = 0
                    while self.tw_t[ti] >= iv and guard < 8:
                        self.tw_t[ti] -= iv; guard += 1
                        dmg = hit * s.towerDmgMul[ti]
                        if pat == PAT_BURST:
                            a0 = self.tw_ang[ti]
                            for a in (a0 - BURST_SPREAD, a0, a0 + BURST_SPREAD):
                                self._fire(ti, a, dmg * 0.6)
                        elif pat in (PAT_FLAME, PAT_LASER):
                            shape = "cone" if pat == PAT_FLAME else "beam"
                            mul = 0.42 if pat == PAT_FLAME else 1.1
                            nb = max(1, min(3, s.towerBeams[ti]))
                            done = False
                            for b in range(nb):
                                off = 0.0 if nb <= 1 else b * (360.0 / nb)
                                if self._area_hit(ti, dmg * mul, shape, kill, off):
                                    done = True; break
                            if done:
                                break
                        elif pat == PAT_SNIPER:
                            if self._sniper_shot(ti, dmg * 20.0, kill, max(1, min(3, s.towerTargets[ti]))):
                                break
                        else:
                            self._fire(ti, self.tw_ang[ti], dmg)
                    if st["won"]:
                        break
                self._compact()
                if st["won"]:
                    break

            # --- 탄알 이동 + 명중 (한 발은 적 하나만 때리고 사라진다 — 관통 없음)
            if self.bn:
                m = self.bn
                self.bux[:m] += self.bvx[:m] * dt
                self.buy[:m] += self.bvy[:m] * dt
                self.bulife[:m] -= dt
                alive = ((self.bulife[:m] > 0)
                         & (self.bux[:m] > XMIN - 1.0) & (self.bux[:m] < XMAX + 1.0)
                         & (self.buy[:m] > YMIN - 1.0) & (self.buy[:m] < YMAX + 1.0))
                n = self.n
                if n:
                    dx = self.px[:n][None, :] - self.bux[:m][:, None]
                    dy = self.py[:n][None, :] - self.buy[:m][:, None]
                    rr = (self.er[:n] + BULLET_R)[None, :]
                    inside = ((dx * dx + dy * dy) <= rr * rr) & (~self.dead[:n])[None, :] & alive[:, None]
                    for bi_ in np.nonzero(inside.any(axis=1))[0]:
                        cols = np.nonzero(inside[bi_])[0]
                        if len(cols) == 0:
                            continue
                        j = int(cols[-1])     # C# 은 enemies 를 뒤에서부터 본다
                        alive[bi_] = False
                        if self.dead[j]:
                            continue
                        self.hp[j] -= self.budmg[bi_]
                        if self.hp[j] <= 0 and kill(j, False):
                            break             # 웨이브 전환/보스 처치 — 이번 프레임 명중 판정 중단
                    self._compact()
                keep = np.nonzero(alive)[0]
                if len(keep) != m:
                    for arr in (self.bux, self.buy, self.bvx, self.bvy, self.bulife, self.budmg):
                        arr[:len(keep)] = arr[keep]
                    self.bn = int(len(keep))
                if st["won"]:
                    break

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
                    if self.dead[i]:
                        continue
                    crit = rng.random() < s.critChance
                    self.hp[i] -= hit * (s.critMult if crit else 1.0)
                    if self.hp[i] <= 0 and kill(i, crit):
                        break
                self._compact()
                if st["won"]:
                    break
            if st["won"]:
                break

        return dict(kills=st["total_kills"], gold=st["total_gold"], wave=st["wave"], won=st["won"],
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
