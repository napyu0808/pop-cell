# -*- coding: utf-8 -*-
"""
round51 — 타워 리메이크(공속 상향·분사구·표적) 로 후반이 너무 쉬워진 걸 체력으로 되돌린다.

  사용법: python apply51.py "1,1,1,1,1.7,2.8,6.5,17.0"
  HP0 / BOSSHP 에 지역별 배율을 곱하고, 골드 배율은 beta=1.2 공식으로 다시 계산해
  gamedata.py 와 GameConfig.cs 에 같이 반영한다. 한 번만 돌릴 것(배율이 누적된다).
"""
import io, re, sys
import gamedata as G
import tune42 as T

ROOT = r"C:\Users\Administrator\Desktop\AutoClickerGame"

mul = [float(v) for v in sys.argv[1].split(",")]
assert len(mul) == 8, "지역 8개의 배율이 필요하다"

hp0 = [int(round(G.HP0[i] * mul[i])) for i in range(8)]
boss = [int(round(G.BOSSHP[i] * mul[i])) for i in range(8)]
G.HP0[:] = hp0
G.BOSSHP[:] = boss
rates = T.gold_rates(1.2)

print("HP0     ", hp0)
print("BOSSHP  ", boss)
print("GOLDRATE", rates)

p = ROOT + r"\balance_sim\gamedata.py"
s = io.open(p, encoding="utf-8").read()
s = re.sub(r"HP0      = \[[^\]]*\]", "HP0      = [" + ", ".join(str(v) for v in hp0) + "]", s)
s = re.sub(r"BOSSHP   = \[[^\]]*\]", "BOSSHP   = [" + ", ".join(str(v) for v in boss) + "]", s)
s = re.sub(r"GOLDRATE = \[[^\]]*\]", "GOLDRATE = [" + ", ".join("%.2f" % v for v in rates) + "]", s)
io.open(p, "w", encoding="utf-8", newline="").write(s)

p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\GameConfig.cs"
s = io.open(p, encoding="utf-8").read()
for i in range(8):
    pat = re.compile(r'(new StageConfig\(%d, "s\.%d",\s*\d+,\s*[\d.]+f,)\s*[\d.]+f(,\s*[\d.]+f,)\s*\d+(,\s*\d+,\s*\d+,)\s*[\d.]+f(,)' % (i, i))
    m = pat.search(s)
    assert m, "StageConfig %d 를 못 찾음" % i
    s = s[:m.start()] + "%s %9df%s %12d%s %8.2ff%s" % (
        m.group(1), hp0[i], m.group(2), boss[i], m.group(3), rates[i], m.group(4)) + s[m.end():]
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("적용 완료")
