# -*- coding: utf-8 -*-
"""
round56 — 골드 배율은 그대로 두고 체력만 지역별로 조정한다.

  사용법: python apply_hp.py "1,1,1,1,1.2,1.3,1.15,1.0"

  측정으로 확인한 것: 체력과 골드를 같이 올리면 플레이타임이 거의 안 변하고
  (479분 -> 479분), 골드만 12% 깎아도 +6% 뿐이다. 길이를 좌우하는 건 체력 대비
  DPS 쪽이라, 플레이타임을 늘릴 때는 골드를 건드리지 않고 체력만 올린다.
  단 과하면 벽이 된다 — 8지역 x2.0 은 400판 안에 못 깼다.
  한 번만 돌릴 것(배율이 누적된다).
"""
import io, re, sys
import gamedata as G

ROOT = r"C:\Users\Administrator\Desktop\AutoClickerGame"

mul = [float(v) for v in sys.argv[1].split(",")]
assert len(mul) == 8, "지역 8개의 배율이 필요하다"

hp0 = [int(round(G.HP0[i] * mul[i])) for i in range(8)]
boss = [int(round(G.BOSSHP[i] * mul[i])) for i in range(8)]
print("HP0   ", G.HP0, "->", hp0)
print("BOSSHP", G.BOSSHP, "->", boss)

p = ROOT + r"\balance_sim\gamedata.py"
s = io.open(p, encoding="utf-8").read()
for name, vals in (("HP0", hp0), ("BOSSHP", boss)):
    pat = re.compile(r"^%s(\s*)= \[[^\]]*\]" % name, re.M)
    m = pat.search(s)
    assert m, "gamedata 의 %s 를 못 찾음" % name
    s = s[:m.start()] + "%s%s= [%s]" % (name, m.group(1), ", ".join(str(v) for v in vals)) + s[m.end():]
io.open(p, "w", encoding="utf-8", newline="").write(s)

# StageConfig(i, "s.i", waves, spawn, HP0, growth, BOSSHP, ...)
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\GameConfig.cs"
s = io.open(p, encoding="utf-8").read()
for i in range(8):
    pat = re.compile(r'(new StageConfig\(%d, "s\.%d",\s*\d+,\s*[\d.]+f,\s*)[\d.]+f(,\s*[\d.]+f,\s*)\d+(,)' % (i, i))
    m = pat.search(s)
    assert m, "StageConfig %d 를 못 찾음" % i
    s = s[:m.start()] + m.group(1) + ("%df" % hp0[i]) + m.group(2) + str(boss[i]) + m.group(3) + s[m.end():]
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("apply_hp ok")
