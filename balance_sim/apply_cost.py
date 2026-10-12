# -*- coding: utf-8 -*-
"""
티어 노드 가격 · 반복 강화 가격을 C# 과 시뮬에 함께 반영한다.

  사용법: python apply_cost.py "71,23035,568181,4758508,61408119,389603450,3655694435,28203765211" 20000000

  round57: 플레이타임을 늘리는 레버로 체력 대신 가격을 쓴다.
    체력을 올리면 "필요 공격력 > 가진 공격력"이 되는 순간 그 지역에서 벽이 생긴다
    (x1.35 로 7지역 2/3 seed 실패). 가격은 배양이 깔아주는 초당 수입 덕에 기다리면 반드시
    모이므로, 길이만 늘고 막히지는 않는다.

  배율이 아니라 '절대값'을 받는다 — 여러 번 돌려도 누적되지 않게.
"""
import io, re, sys

ROOT = r"C:\Users\Administrator\Desktop\AutoClickerGame"

costs = [int(v) for v in sys.argv[1].split(",")]
assert len(costs) == 8, "티어 8개의 가격이 필요하다"
rep = int(sys.argv[2]) if len(sys.argv) > 2 else None

# ---- UpgradeTree.cs : TierCost ----
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\UpgradeTree.cs"
s = io.open(p, encoding="utf-8").read()
pat = re.compile(r"(static readonly long\[\] TierCost = \{)[^}]*(\};)")
m = pat.search(s)
assert m, "TierCost 를 못 찾음"
s = s[:m.start()] + m.group(1) + " " + ", ".join(str(v) for v in costs) + " " + m.group(2) + s[m.end():]

if rep is not None:
    pat2 = re.compile(r"(public static readonly long\[\] RepCost = \{)[^}]*(\};)")
    m2 = pat2.search(s)
    assert m2, "RepCost 를 못 찾음"
    s = s[:m2.start()] + m2.group(1) + " " + ", ".join([str(rep)] * 6) + " " + m2.group(2) + s[m2.end():]
io.open(p, "w", encoding="utf-8", newline="").write(s)

# ---- gamedata.py ----
p = ROOT + r"\balance_sim\gamedata.py"
s = io.open(p, encoding="utf-8").read()
s2 = re.sub(r"TIER_COST(\s*)= \[[^\]]*\]", "TIER_COST    = [" + ", ".join(str(v) for v in costs) + "]", s)
assert s2 != s, "gamedata 의 TIER_COST 를 못 찾음"
if rep is not None:
    s2 = re.sub(r"REP_COST = \[[^\]]*\]", "REP_COST = [" + ", ".join([str(rep)] * 6) + "]", s2)
io.open(p, "w", encoding="utf-8", newline="").write(s2)

print("TIER_COST =", costs)
if rep is not None:
    print("REP_COST  =", rep)
print("apply_cost ok")
