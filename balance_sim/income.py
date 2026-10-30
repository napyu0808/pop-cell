# -*- coding: utf-8 -*-
"""
지역별 한 판 수입 — 전투를 돌리지 않고 데이터만으로 계산한다.

  tune42.py 는 프레임 단위로 전투를 돌려서 메모리를 많이 먹는다(numpy + 프로세스 3개).
  "이 지역을 한 판 깨면 얼마가 들어오나"만 알고 싶을 때는 그럴 필요가 없다 —
  웨이브마다 처치 할당량 x 처치당 골드를 합치면 된다. 순수 파이썬, 메모리 거의 0.

  주의: '전 웨이브를 다 깬다'는 가정이라 실제보다 후하다. 중간에 시간 초과로 끝나는
  판이 섞이므로, 실측 평균은 여기서 나온 값의 60~80% 쯤으로 보면 된다.

  사용법: python income.py
"""
import gamedata as G

GOLD_MULT = G.GOLD_OUTSIDE_SIM   # 연속 클리어·콤보·배양·정수까지 포함한 어림 배수


def gold_at(stage, w):
    n = len(G.GOLD_CURVE)
    g = G.GOLD_CURVE[w - 1] if w <= n else G.GOLD_CURVE[-1] * (1.10 ** (w - n))
    return max(1, round(g * G.GOLDRATE[stage]))


def run_income(stage):
    """한 판(1웨이브 ~ 보스)을 다 깼을 때의 골드와, 제한시간 합."""
    waves = G.WAVES[stage]
    quota0 = G.QUOTA0[stage]
    total = 0
    for w in range(1, waves + 1):
        kills = max(5, int(round((quota0 + (w - 1) * 2) / 5.0)) * 5)   # GameConfig.QuotaAt 미러
        total += kills * gold_at(stage, w)
    secs = G.TLIM[stage] * waves      # 웨이브마다 제한시간이 다시 찬다
    return total * GOLD_MULT, secs


def main():
    print("지역별 한 판 수입 (전 웨이브 클리어 가정, 외부 배수 x%.2f 포함)" % GOLD_MULT)
    print()
    print("%-6s %12s %7s %14s %14s %16s" % ("지역", "한 판 골드", "최대 초", "골드/분", "골드/초", "티어 노드 가격"))
    for i in range(8):
        g, secs = run_income(i)
        tier_cost = G.TIER_COST[i] if i < len(G.TIER_COST) else 0
        print("%-6s %12.3g %7d %14.3g %14.3g %16.3g"
              % ("S%d" % (i + 1), g, secs, g / (secs / 60.0), g / secs, tier_cost))
    print()
    print("※ 티어 노드 가격 = 그 티어 노드 1개 값(UpgradeTree.TierCost).")
    print("   한 판에 그 티어 노드를 몇 개 살 수 있는지가 곧 체감 속도다.")
    print()
    print("%-6s %18s" % ("지역", "한 판 = 티어노드 n개"))
    for i in range(8):
        g, _ = run_income(i)
        tc = G.TIER_COST[i] if i < len(G.TIER_COST) else 0
        print("%-6s %18.2f" % ("S%d" % (i + 1), g / tc if tc else 0))


if __name__ == "__main__":
    main()
