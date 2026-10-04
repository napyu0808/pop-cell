# -*- coding: utf-8 -*-
"""
round48 — autotune48 결과(판수 재분배)를 gamedata.py 와 C# 에 반영.

  초반(2~4지역)에서 같은 판을 수십 번 돌던 걸 완만한 곡선으로 재분배한다.
  반영 대상
    gamedata.py      TIER_COST / HP0 / BOSSHP / GOLDRATE
    UpgradeTree.cs   TierCost
    GameConfig.cs    StageConfig 의 hp0 / bossHp / goldRate

  한 번만 돌릴 것 — HP 는 "현재 값 × 배율" 이라 두 번 돌리면 누적된다.
"""
import io, json, re, sys
import gamedata as G
import tune42 as T

ROOT = r"C:\Users\Administrator\Desktop\AutoClickerGame"
res = json.load(open("autotune48_result.json", encoding="utf-8"))

cost = [int(c) for c in res["cost"]]
hpm = res["hp"]
hp0 = [int(round(G.HP0[i] * hpm[i])) for i in range(8)]
boss = [int(round(G.BOSSHP[i] * hpm[i])) for i in range(8)]

G.HP0[:] = hp0
G.BOSSHP[:] = boss
rates = T.gold_rates(res.get("beta", 1.2))

print("TIER_COST", cost)
print("HP0      ", hp0)
print("BOSSHP   ", boss)
print("GOLDRATE ", rates)

# ---- gamedata.py ----
p = ROOT + r"\balance_sim\gamedata.py"
s = io.open(p, encoding="utf-8").read()
s = re.sub(r"TIER_COST    = \[[^\]]*\]", "TIER_COST    = [" + ", ".join(str(v) for v in cost) + "]", s)
s = re.sub(r"HP0      = \[[^\]]*\]", "HP0      = [" + ", ".join(str(v) for v in hp0) + "]", s)
s = re.sub(r"BOSSHP   = \[[^\]]*\]", "BOSSHP   = [" + ", ".join(str(v) for v in boss) + "]", s)
s = re.sub(r"GOLDRATE = \[[^\]]*\]", "GOLDRATE = [" + ", ".join("%.2f" % v for v in rates) + "]", s)
io.open(p, "w", encoding="utf-8", newline="").write(s)

# ---- UpgradeTree.cs ----
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\UpgradeTree.cs"
s = io.open(p, encoding="utf-8").read()
s2 = re.sub(r"static readonly long\[\] TierCost = \{[^}]*\};",
            "static readonly long[] TierCost = { " + ", ".join(str(v) for v in cost) + " };", s)
assert s2 != s, "TierCost 를 못 찾음"
io.open(p, "w", encoding="utf-8", newline="").write(s2)

# ---- GameConfig.cs ----
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\GameConfig.cs"
s = io.open(p, encoding="utf-8").read()
for i in range(8):
    pat = re.compile(r'(new StageConfig\(%d, "s\.%d",\s*\d+,\s*[\d.]+f,)\s*[\d.]+f(,\s*[\d.]+f,)\s*\d+(,\s*\d+,\s*\d+,)\s*[\d.]+f(,)' % (i, i))
    m = pat.search(s)
    assert m, "StageConfig %d 를 못 찾음" % i
    s = s[:m.start()] + "%s %8df%s %11d%s %7.2ff%s" % (
        m.group(1), hp0[i], m.group(2), boss[i], m.group(3), rates[i], m.group(4)) + s[m.end():]
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("적용 완료")
