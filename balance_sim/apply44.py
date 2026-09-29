# -*- coding: utf-8 -*-
"""
round44 튜닝 결과(autotune44b_result.json 의 cfg)를 C#(UpgradeTree.cs, GameConfig.cs)과 gamedata.py 에 반영.
  cfg = {"cost":[6], "rep":[6], "hp":[8 배율], "beta":1.2}
  체력은 현재 gamedata HP0/BOSSHP 에 배율을 곱하고, 골드 배율은 바뀐 체력으로 beta 공식 재계산.
"""
import io, re, json, sys
import gamedata as G
import tune42 as T

ROOT = r"C:\Users\Administrator\Desktop\AutoClickerGame"
cfg = json.load(open(sys.argv[1] if len(sys.argv) > 1 else "autotune44b_result.json"))["cfg"]

hp0 = [round(h * m) for h, m in zip(G.HP0, cfg["hp"])]
boss = [round(b * m) for b, m in zip(G.BOSSHP, cfg["hp"])]
G.HP0[:] = hp0; G.BOSSHP[:] = boss
rates = T.gold_rates(cfg["beta"])
cost = [int(round(c)) for c in cfg["cost"]]
rep = [int(round(r)) for r in cfg["rep"]]
assert max(cost) < 2_000_000_000, "int 한도(티어 가격은 int)"   # 반복 강화 가격은 long
print("HP0", hp0); print("BOSS", boss); print("GOLDRATE", rates); print("COST", cost); print("REP", rep)

# ---- gamedata.py ----
p = ROOT + r"\balance_sim\gamedata.py"
s = io.open(p, encoding="utf-8").read()
s = re.sub(r"TIER_COST    = \[[^\]]*\]", "TIER_COST    = " + str(cost), s)
s = re.sub(r"REP_COST = \[[^\]]*\]", "REP_COST = " + str(rep), s)
s = re.sub(r"HP0      = \[[^\]]*\]", "HP0      = [" + ", ".join(str(v) for v in hp0) + "]", s)
s = re.sub(r"BOSSHP   = \[[^\]]*\]", "BOSSHP   = [" + ", ".join(str(v) for v in boss) + "]", s)
s = re.sub(r"GOLDRATE = \[[^\]]*\]", "GOLDRATE = [" + ", ".join("%.2f" % v for v in rates) + "]", s)
if "# round44: 지역 골드" not in s:
    s = s.replace("# ---- GameConfig.cs : StageConfig.Stages",
                  "# round44: 지역 골드 = '골드/체력' 효율이 지역마다 ×1.2 (tune42.gold_rates), 체력·가격은 autotune44(b)\n"
                  "# ---- GameConfig.cs : StageConfig.Stages", 1)
io.open(p, "w", encoding="utf-8", newline="").write(s)

# ---- UpgradeTree.cs ----
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\UpgradeTree.cs"
s = io.open(p, encoding="utf-8").read()
s = re.sub(r"static readonly int\[\] TierCost = \{[^}]*\};",
           "static readonly int[] TierCost = { " + ", ".join(str(c) for c in cost) + " };", s)
s = re.sub(r"public static readonly long\[\] RepCost = \{[^}]*\};",
           "public static readonly long[] RepCost = { " + ", ".join(str(c) for c in rep) + " };", s)
io.open(p, "w", encoding="utf-8", newline="").write(s)

# ---- GameConfig.cs StageConfig ----
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\GameConfig.cs"
s = io.open(p, encoding="utf-8").read()
for i in range(8):
    pat = re.compile(r'(new StageConfig\(%d, "s\.%d",\s*\d+,\s*[\d.]+f,)\s*[\d.]+f(,\s*[\d.]+f,)\s*\d+(,\s*\d+,\s*\d+,)\s*[\d.]+f(,)' % (i, i))
    m = pat.search(s)
    assert m, i
    s = s[:m.start()] + "%s %7df%s %10d%s %6.2ff%s" % (m.group(1), hp0[i], m.group(2), boss[i], m.group(3), rates[i], m.group(4)) + s[m.end():]
if "round44: 지역 골드" not in s:
    s = s.replace("        public static readonly StageConfig[] Stages =",
                  "        // round44: 지역 골드 배율 = '골드/적 체력' 효율이 지역마다 ×1.2 가 되게(예전엔 2지역이 1지역의 0.2배,\n"
                  "        //   8지역은 0.012배라 올라갈수록 벌이가 줄었다). 체력은 150노드 트리에 맞춰 balance_sim/autotune44(b)로.\n"
                  "        public static readonly StageConfig[] Stages =", 1)
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("적용 완료")
