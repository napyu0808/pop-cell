# -*- coding: utf-8 -*-
"""
round45 — 적 체력을 "일반 노드(반복 강화 제외)를 다 뚫었을 때 공격력" 기준으로 재조정.

  문제: round44 자동 튜닝이 "반복 강화를 40~50레벨 산다"는 전제로 체력을 올렸는데,
        반복 강화가 7.2억이라 현실적으로 못 사고 치트키로도 안 사진다.
        결과: 일반 노드 풀트리(DPS 225,825)로 8지역 보스(50억)가 209배 초과 = 클리어 불가.

  기준: 지역 k 는 "그때까지 열린 티어(0..min(k,5))를 다 산 상태" 로 깰 수 있어야 한다.
        보스   = 그 DPS × 목표초 (제한시간의 15%→55% 로 지역마다 빡빡해짐)
        잡몹   = 마지막 웨이브 한 마리가 목표초 안에 죽게
        반복 강화는 "더 빨리/여유있게" 용도 — 필수가 아니다.
  골드 배율은 바뀐 체력으로 다시 계산한다(효율 = 골드/체력 이 지역마다 ×1.2).
"""
import io, re, sys
import gamedata as G
import tune42 as T

ROOT = r"C:\Users\Administrator\Desktop\AutoClickerGame"

# 에디터에서 실측한 "지역 k 에서 살 수 있는 노드 전부" DPS (단일 대상, 치명 평균 포함)
DPS = [256, 894, 2286, 6642, 23705, 65041, 220748, 578355]   # round45: 티어 8개로 늘린 뒤 실측
# 보스 목표 시간 = (제한시간 + 그 지역까지 살 수 있는 시간 노드 보너스) 의 50%.
#   시간 노드가 지역당 +15초씩(최대 +120) 붙으므로 실질 제한시간이 2배가 된다 — 그걸 감안해야
#   "풀트리(시간 노드 포함)로 잡히되 빡빡한" 체력이 나온다.
BOSS_SEC = [21.5, 32.0, 44.5, 57.0, 69.5, 82.0, 96.5, 114.0]
MOB_SEC = [0.15, 0.30, 0.50, 0.35, 0.30, 0.35, 0.50, 0.70]   # 마지막 웨이브 잡몹 1마리
REP_COST = 7_000_000                                # 사용자 요청: 500만~1000만

boss = [int(round(d * s)) for d, s in zip(DPS, BOSS_SEC)]
hp0 = []
for i in range(8):
    grow = G.HPG[i] ** (G.WAVES[i] - 2)             # 마지막 잡몹 웨이브
    hp0.append(int(round(DPS[i] * MOB_SEC[i] / grow)))

G.HP0[:] = hp0
G.BOSSHP[:] = boss
rates = T.gold_rates(1.2)

print("HP0 ", hp0)
print("BOSS", boss)
print("GOLDRATE", rates)
print("REP_COST", REP_COST)

# ---- gamedata.py ----
p = ROOT + r"\balance_sim\gamedata.py"
s = io.open(p, encoding="utf-8").read()
s = re.sub(r"HP0      = \[[^\]]*\]", "HP0      = [" + ", ".join(str(v) for v in hp0) + "]", s)
s = re.sub(r"BOSSHP   = \[[^\]]*\]", "BOSSHP   = [" + ", ".join(str(v) for v in boss) + "]", s)
s = re.sub(r"GOLDRATE = \[[^\]]*\]", "GOLDRATE = [" + ", ".join("%.2f" % v for v in rates) + "]", s)
s = re.sub(r"REP_COST = \[[^\]]*\]", "REP_COST = " + str([REP_COST] * 6), s)
io.open(p, "w", encoding="utf-8", newline="").write(s)

# ---- UpgradeTree.cs (반복 강화 가격) ----
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\UpgradeTree.cs"
s = io.open(p, encoding="utf-8").read()
s2 = re.sub(r"public static readonly long\[\] RepCost = \{[^}]*\};",
            "public static readonly long[] RepCost = { " + ", ".join([str(REP_COST)] * 6) + " };   // flat, mult, speed, range, critx, gold", s)
assert s2 != s
io.open(p, "w", encoding="utf-8", newline="").write(s2)

# ---- GameConfig.cs (지역 체력·골드) ----
p = ROOT + r"\My project\Assets\Scripts\BlackholeGame\GameConfig.cs"
s = io.open(p, encoding="utf-8").read()
for i in range(8):
    pat = re.compile(r'(new StageConfig\(%d, "s\.%d",\s*\d+,\s*[\d.]+f,)\s*[\d.]+f(,\s*[\d.]+f,)\s*\d+(,\s*\d+,\s*\d+,)\s*[\d.]+f(,)' % (i, i))
    m = pat.search(s)
    assert m, i
    s = s[:m.start()] + "%s %7df%s %10d%s %6.2ff%s" % (m.group(1), hp0[i], m.group(2), boss[i], m.group(3), rates[i], m.group(4)) + s[m.end():]
if "round45: 체력은" not in s:
    s = s.replace("        public static readonly StageConfig[] Stages =",
                  "        // round45: 체력은 '그 지역까지 열린 일반 노드를 다 산 DPS' 기준 — 반복 강화 없이도 깰 수 있게.\n"
                  "        //   (round44 튜닝은 반복 강화 40~50레벨을 전제해서 8지역 보스가 209배 초과였다)\n"
                  "        public static readonly StageConfig[] Stages =", 1)
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("적용 완료")
