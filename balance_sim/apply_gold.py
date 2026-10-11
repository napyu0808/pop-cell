# -*- coding: utf-8 -*-
"""
round56 — 체력은 그대로 두고 골드 배율만 지역별로 조정한다.

  사용법: python apply_gold.py "1,1,1,1,0.893,0.893,0.893,0.893"
  (지역별 배율. 체력 대비 골드를 낮추면 플레이타임이 늘어난다.)

  apply51.py 는 체력을 올리면 골드도 공식으로 같이 올려서 플레이타임이 거의
  안 변한다 — 길이를 늘리려면 체력 대비 골드를 깎아야 해서 이 스크립트가 따로 있다.
  한 번만 돌릴 것(배율이 누적된다).
"""
import io, re, sys
import gamedata as G

ROOT = r"C:\Users\Administrator\Desktop\AutoClickerGame"

mul = [float(v) for v in sys.argv[1].split(",")]
assert len(mul) == 8, "지역 8개의 배율이 필요하다"

rates = [round(G.GOLDRATE[i] * mul[i], 2) for i in range(8)]
print("GOLDRATE", G.GOLDRATE, "->", rates)

p = ROOT + r"\balance_sim\gamedata.py"
s = io.open(p, encoding="utf-8").read()
s2 = re.sub(r"GOLDRATE = \[[^\]]*\]", "GOLDRATE = [" + ", ".join("%.2f" % v for v in rates) + "]", s)
assert s2 != s, "gamedata 의 GOLDRATE 를 못 찾음"
io.open(p, "w", encoding="utf-8", newline="").write(s2)

p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\GameConfig.cs"
s = io.open(p, encoding="utf-8").read()
for i in range(8):
    pat = re.compile(r'(new StageConfig\(%d, "s\.%d",[^)]*?,\s*\d+,\s*\d+,\s*)[\d.]+f(,)' % (i, i))
    m = pat.search(s)
    assert m, "StageConfig %d 를 못 찾음" % i
    s = s[:m.start()] + m.group(1) + ("%.2ff" % rates[i]) + m.group(2) + s[m.end():]
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("apply_gold ok")
