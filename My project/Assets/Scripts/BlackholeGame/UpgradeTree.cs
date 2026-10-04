using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlackholeGame
{
    // 노드 하나. 부모가 뚫려야 뚫린다. 위치는 부모 + dir * spacing.
    // 효과 텍스트는 노드에 안 보이고, 마우스 오버 시 툴팁으로만 표시된다.
    // tier = 이 노드를 열려면 클리어해야 하는 스테이지 수 (0 = 처음부터). Phase 2에서 게이팅에 사용.
    public class UpgradeNode
    {
        public readonly string id, parentId, label, desc, icon;   // label/desc = Loc 키
        public readonly float descArg;                            // 설명 서식 인자 (0 = 없음)
        public readonly Vector2 dir;        // 부모로부터의 방향 (스크린 좌표, 위 = -Y)
        public readonly long cost;   // round44: long — 반복 강화 값이 21억을 넘을 수 있다
        public readonly int tier;
        public readonly Action<Stats> apply;
        public readonly bool repeat;        // round44: 반복 강화 — 살 때마다 같은 값(cost)으로 같은 효과(apply)를 한 번 더
        public readonly int tab;            // round47: 0 = 커서(기존 트리), 1 = 자동 타워
        public readonly string[] alsoNeed;  // round50: 부모 말고 추가로 뚫어야 하는 노드(타워 변신)

        public UpgradeNode(string id, string parentId, Vector2 dir, string icon,
                           string label, long cost, int tier, string desc, float descArg, Action<Stats> apply,
                           bool repeat = false, int tab = 0, string[] alsoNeed = null)
        {
            this.id = id; this.parentId = parentId; this.dir = dir; this.icon = icon;
            this.label = label; this.cost = cost; this.tier = tier; this.desc = desc; this.descArg = descArg; this.apply = apply;
            this.repeat = repeat; this.tab = tab; this.alsoNeed = alsoNeed;
        }
        public bool IsRoot => parentId == null;
    }

    // ============================================================
    // 스킬트리 (round44) — 능력치마다 한 가지, 150노드 + 가지 끝 반복 강화.
    //   코어에서 4갈래(분야) → 분야마다 능력치별 가지로 갈라진다.
    //     공격        : [자동공격] → 배수 · 공격력 · 공속
    //     치명/범위   : [범위 1]  → 치명확률 · 범위 · 치명배수
    //     경제        : [골드 1]  → 시간 · 골드 · 웨이브 스킵
    //     소환/백신   : [소환 1]  → 동시소환 · 소환 · [백신] → 백신 대미지 · 범위 · 빈도
    //   일반 노드는 1~6지역(티어 0~5)에 걸쳐 열리고, 공격력·배수·공속·범위·치명배수·골드 가지 맨 위엔
    //   "반복 강화"가 붙는다(5지역 클리어 후, 같은 값으로 같은 양을 계속). 6~8지역 보스전은 이걸로 민다.
    //   round42(300노드)에서 반으로 줄이면서 노드 1개당 수치를 키워 풀트리 능력치는 비슷하게 유지.
    //   ※ balance_sim/gamedata.py 가 이 표를 그대로 복제한다 — 값을 바꾸면 거기도 같이.
    // ============================================================
    public static class UpgradeTree
    {
        public const string RootId = "root";
        public const int Tiers = 8;                 // 8지역
        // round45: 티어 6 → 8. 예전엔 6·7·8지역에 새 노드가 없어서 그 구간이 "성장 없이 그냥 깨는" 판이
        //   됐다(7지역 2판, 8지역 1판). 지역마다 새 티어가 열려야 살 이유와 성장 곡선이 이어진다.
        public const int NodeTiers = 8;             // 일반 노드가 퍼지는 티어 수(0~7) = 지역 수
        public const int RepeatTier = 7;            // 반복 강화는 마지막 티어 — 8지역 구간의 추가 성장

        // ---- 상한 ----
        public const float MultCap = 300f;          // 배수 버킷(%p) — 가지 노드만. 반복 강화는 상한 없음
        public const float CritCap = 0.80f;         // 치명 확률
        public const float IntervalFloor = 0.06f;   // 타격 간격 하한(가지 노드)
        public const float RepIntervalFloor = 0.035f; // 반복 강화로는 여기까지
        public const float TimeCap = 120f;          // 시작 시간 +초
        public const float SpawnFloor = 0.22f;      // 소환 간격 배율 하한
        public const int   SCountMax = 10;          // 동시 소환 (기본 1 → 최대 10)
        public const float BombDmgMax = 5f, BombRadMax = 2f, BombIntervalMin = 10f;
        public const float RepRangeMax = 5f;        // 반복 강화 범위 상한(월드 단위 반경)

        static float CM(float v) => Mathf.Min(MultCap, v);
        static float CC(float v) => Mathf.Min(CritCap, v);
        static float CI(float v) => Mathf.Max(IntervalFloor, v);
        static float CT(float v) => Mathf.Min(TimeCap, v);
        static float CS(float v) => Mathf.Max(SpawnFloor, v);
        static int   CN(int v)   => Mathf.Clamp(v, 1, SCountMax);

        // ---- 노드 1개당 수치 (round44: 300→150노드라 대략 2배) ----
        public const float MultPP = 21f, SpeedMul = 0.826f, CritCPP = 0.08f, CritXAdd = 0.18f,
                           RangeAdd = 0.21f, GoldPP = 18f, TimeAdd = 15f, SpawnMul = 0.85f,
                           BombDmgAdd = 0.5f, BombRadAdd = 0.10f, BombIntervalSub = 2f;
        public const int SkipAdd = 2;

        // ---- 반복 강화 1회당 수치·가격 (같은 값으로 같은 양) ----
        // 1회 수치는 작게 — 여러 번(수십~수백 레벨) 사게 해서 +1/+10/최대 버튼이 의미 있게
        public const float RepFlat = 10f, RepMult = 1f, RepAps = 0.05f, RepRange = 0.01f, RepCritX = 0.02f, RepGold = 1f;
        public static readonly long[] RepCost = { 7000000, 7000000, 7000000, 7000000, 7000000, 7000000 };   // flat, mult, speed, range, critx, gold

        // tier별 노드 비용 — balance_sim(tune42)으로 맞춘다.
        static readonly long[] TierCost = { 24, 6268, 136418, 809270, 9640207, 93542245, 994746785, 17987095160 };
        public static long Cost(int tier) =>
            tier >= 0 && tier < TierCost.Length ? TierCost[tier]
            : (long)System.Math.Round(TierCost[TierCost.Length - 1] * System.Math.Pow(2.6, tier - TierCost.Length + 1));

        // 공격력 노드값은 티어별 표(가지 안 높이 → 티어 → 값)
        static readonly int[] FlatByTier = { 20, 24, 30, 50, 88, 156, 290, 560 };
        public static int FlatAt(int tier) =>
            tier >= 0 && tier < FlatByTier.Length ? FlatByTier[tier]
            : Mathf.RoundToInt(FlatByTier[FlatByTier.Length - 1] * Mathf.Pow(1.9f, tier - FlatByTier.Length + 1));

        public enum T { Flat, Mult, Speed, CritC, CritX, Range, Gold, Time, Spawn, SCount, Skip, Auto, Bomb, BombDmg, BombRad, BombFreq,
                        RFlat, RMult, RSpeed, RRange, RCritX, RGold }

        // 가지 정의 — (타입, id 접두어, 노드 수, 시작 티어). 노드 수는 머리 노드를 포함한 전체 길이.
        //   합계 147 + 자동공격 + 백신 = 149, 코어까지 150. (반복 강화 6개는 별도)
        public struct LaneDef { public T type; public string key; public int len; public int tier0; }
        static LaneDef L(T t, string key, int len, int tier0 = 0) => new LaneDef { type = t, key = key, len = len, tier0 = tier0 };
        public static readonly LaneDef
            LFlat  = L(T.Flat,  "flat",  17),  LMult  = L(T.Mult,  "mult",  14),  LSpeed = L(T.Speed, "speed", 13),
            LCritC = L(T.CritC, "critc", 10),  LRange = L(T.Range, "range", 13),  LCritX = L(T.CritX, "critx", 15),
            LTime  = L(T.Time,  "time",   8),  LGold  = L(T.Gold,  "gold",  15),  LSkip  = L(T.Skip,  "skip",  3),
            LSCount= L(T.SCount,"scount", 9),  LSpawn = L(T.Spawn, "spawn",  9),
            LBDmg  = L(T.BombDmg, "bombdmg", 6, 1), LBRad = L(T.BombRad, "bombrad", 10, 1), LBFreq = L(T.BombFreq, "bombfreq", 5, 1);
        public const int BombUnlockTier = 1;   // 백신은 1지역을 깬 뒤 — 기본 조작에 익숙해진 다음 새 요소

        // ---- 자동 타워(round50 개편) ---------------------------------------------------
        //   줄기(좌·중앙·우)마다: 해금 → [공격력 / 공속 / 회전속도] 3갈래 → 셋 다 뚫으면 변신 노드.
        //   변신은 화염방사 → 레이저 → 저격 순. 변신해도 스펙(공격력·공속·회전)은 그대로 쌓이고
        //   공격 패턴만 늘어난다 — 타워 탭에서 뚫은 패턴 중 아무거나 골라 쓴다.
        //   3~6지역에서 힘이 되게 티어를 잡았고, 저격은 보스가 단단해지는 7~8지역용이다.
        public const int TowerUnlockTier = 1;

        public const float TowerDmgAdd = 0.10f;                     // 단계마다 5개 = +0.5
        public static readonly float[] TowerDmgMaxAt = { 1.0f, 1.5f, 2.0f, 2.4f };   // 단계별 상한
        public const float TowerSpeedMul = 0.88f, TowerIntervalMin = 0.25f;
        public const float TowerSpinAdd = 14f, TowerSpinMax = 300f;

        const int TwPerBranch = 5;                                   // 갈래 하나당 노드 수
        //  단계: 0 기본(단발) · 1 화염방사 · 2 레이저 · 3 저격
        static readonly int[] TwStageTier  = { 2, 4, 5, 7 };          // 그 단계 갈래 노드의 시작 티어
        static readonly int[] TwMorphTier  = { 0, 4, 5, 6 };          // 변신 노드 티어(0 단계는 변신 없음)
        static readonly string[] TowerKey  = { "twL", "twC", "twR" };
        static readonly string[] TowerName = { "n.towerL", "n.towerC", "n.towerR" };

        // 줄기 하나
        static void TowerLane(List<UpgradeNode> list, int idx, string parent)
        {
            string key = TowerKey[idx];
            string unlock = key + "0";
            list.Add(new UpgradeNode(unlock, parent, Vector2.zero, "tower", TowerName[idx],
                                     Cost(TowerUnlockTier), TowerUnlockTier, "nd.towerOn", 0f,
                                     s => s.towerOn[idx] = true, false, 1));

            string stem = unlock;
            for (int st = 0; st < 4; st++)
            {
                if (st > 0)
                {
                    // 변신 — 바로 아래 세 갈래 끝을 전부 뚫어야 열린다(가운데 갈래를 부모로)
                    int pat = st == 1 ? Stats.PatFlame : st == 2 ? Stats.PatLaser : Stats.PatSniper;
                    string icon = st == 1 ? "flame" : st == 2 ? "laser" : "scope";
                    string desc = st == 1 ? "nd.towerFlame" : st == 2 ? "nd.towerLaser" : "nd.towerSniper";
                    string mid = stem + "s"; // 직전 단계의 공속 갈래 끝
                    string morph = key + "m" + st;
                    list.Add(new UpgradeNode(morph, mid, Vector2.zero, icon, TowerName[idx],
                                             Cost(TwMorphTier[st]), TwMorphTier[st], desc, 0f,
                                             s => s.UnlockPattern(idx, pat), false, 1,
                                             new[] { stem + "d", stem + "p" }));
                    stem = morph;
                    if (st == 3) break;     // 저격은 변신만 하고 갈래를 더 두지 않는다
                }

                int tier0 = TwStageTier[st];
                float dmgMax = TowerDmgMaxAt[st];
                // 세 갈래 — d 공격력 · s 공속 · p 회전속도. 각각 TwPerBranch 칸.
                for (int b = 0; b < 3; b++)
                {
                    string bk = b == 0 ? "d" : b == 1 ? "s" : "p";
                    string bp = stem;
                    for (int k = 0; k < TwPerBranch; k++)
                    {
                        int tier = Mathf.Min(NodeTiers - 1, tier0 + (k >= TwPerBranch - 2 ? 1 : 0));
                        string id = stem + bk + (k == TwPerBranch - 1 ? "" : k.ToString());
                        if (b == 0)
                            list.Add(new UpgradeNode(id, bp, Vector2.zero, "sword", TowerName[idx], Cost(tier), tier,
                                                     "nd.towerDmg", TowerDmgAdd,
                                                     s => s.towerDmgMul[idx] = Mathf.Min(dmgMax, s.towerDmgMul[idx] + TowerDmgAdd), false, 1));
                        else if (b == 1)
                            list.Add(new UpgradeNode(id, bp, Vector2.zero, "bolt", TowerName[idx], Cost(tier), tier,
                                                     "nd.towerSpd", TowerSpeedMul,
                                                     s => s.towerInterval[idx] = Mathf.Max(TowerIntervalMin, s.towerInterval[idx] * TowerSpeedMul), false, 1));
                        else
                            list.Add(new UpgradeNode(id, bp, Vector2.zero, "spin", TowerName[idx], Cost(tier), tier,
                                                     "nd.towerSpin", TowerSpinAdd,
                                                     s => s.towerSpin[idx] = Mathf.Min(TowerSpinMax, s.towerSpin[idx] + TowerSpinAdd), false, 1));
                        bp = id;
                    }
                }
                // 공속 갈래 끝에 연발 — 기본 타워의 변형이라 변신 단계로 두지 않았다
                if (st == 0)
                    list.Add(new UpgradeNode(stem + "burst", stem + "s", Vector2.zero, "chevrons", TowerName[idx],
                                             Cost(TwStageTier[0] + 1), TwStageTier[0] + 1, "nd.towerBurst", 0f,
                                             s => s.UnlockPattern(idx, Stats.PatBurst), false, 1));
            }
        }

        // 가지 안 k번째(0부터) 노드의 티어 — 시작 티어부터 NodeTiers-1(5)까지 높이 비율로 고르게
        public static int TierOf(LaneDef d, int k) => d.tier0 + (k * (NodeTiers - d.tier0)) / d.len;

        static (string icon, string label, string desc, float descArg, Action<Stats> apply) Effect(T t, int tier)
        {
            switch (t)
            {
                case T.Flat:
                    int fa = FlatAt(tier);
                    return ("sword", "n.flat", "nd.flat", fa, s => s.flatBonus += fa);
                case T.Mult:   return ("mult", "n.mult", "nd.mult", MultPP, s => s.multBucketPercent = CM(s.multBucketPercent + MultPP));
                case T.Speed:  return ("bolt", "n.speed", "nd.speed", SpeedMul, s => s.attackInterval = CI(s.attackInterval * SpeedMul));
                case T.CritC:  return ("crosshair", "n.critc", "nd.critc", CritCPP * 100f, s => s.critChance = CC(s.critChance + CritCPP));
                case T.CritX:  return ("star", "n.critx", "nd.critx", CritXAdd, s => s.critMult += CritXAdd);
                case T.Range:  return ("ring", "n.range", "nd.range", RangeAdd, s => s.cursorRadius += RangeAdd);
                case T.Gold:   return ("coin", "n.gold", "nd.gold", GoldPP, s => s.goldMultPercent += GoldPP);
                case T.Time:   return ("clock", "n.time", "nd.time", TimeAdd, s => s.bonusTimeSec = CT(s.bonusTimeSec + TimeAdd));
                case T.Spawn:  return ("chevrons", "n.spawn", "nd.spawn", SpawnMul, s => s.spawnIntervalMult = CS(s.spawnIntervalMult * SpawnMul));
                case T.SCount: return ("cells", "n.scount", "nd.scount", 1f, s => s.spawnCount = CN(s.spawnCount + 1));
                case T.Skip:   return ("skip", "n.skip", "nd.skip", SkipAdd, s => s.startWave += SkipAdd);
                case T.Bomb:   return ("pill", "n.bomb", "nd.bomb", 0f, s => s.bombUnlocked = true);
                case T.BombDmg:  return ("blast", "n.bombdmg", "nd.bombdmg", BombDmgAdd, s => s.bombDmgMul = Mathf.Min(BombDmgMax, s.bombDmgMul + BombDmgAdd));
                case T.BombRad:  return ("blastring", "n.bombrad", "nd.bombrad", BombRadAdd, s => s.bombRadiusMul = Mathf.Min(BombRadMax, s.bombRadiusMul + BombRadAdd));
                case T.BombFreq: return ("pillfast", "n.bombfreq", "nd.bombfreq", BombIntervalSub, s => s.bombInterval = Mathf.Max(BombIntervalMin, s.bombInterval - BombIntervalSub));
                // ---- 반복 강화 — 가지 노드와 같은 아이콘, 효과는 "같은 양을 계속" ----
                case T.RFlat:  return ("sword", "n.rflat", "nd.rflat", RepFlat, s => s.flatBonus += RepFlat);
                case T.RMult:  return ("mult", "n.rmult", "nd.rmult", RepMult, s => s.multBucketPercent += RepMult);   // 상한 없음
                // 공속은 "초당 공격 +0.2회"씩 — 간격 곱셈이면 같은 양이 아니다
                case T.RSpeed: return ("bolt", "n.rspeed", "nd.rspeed", RepAps,
                                       s => s.attackInterval = Mathf.Max(RepIntervalFloor, 1f / (1f / s.attackInterval + RepAps)));
                case T.RRange: return ("ring", "n.rrange", "nd.rrange", RepRange, s => s.cursorRadius = Mathf.Min(RepRangeMax, s.cursorRadius + RepRange));
                case T.RCritX: return ("star", "n.rcritx", "nd.rcritx", RepCritX, s => s.critMult += RepCritX);
                case T.RGold:  return ("coin", "n.rgold", "nd.rgold", RepGold, s => s.goldMultPercent += RepGold);
                case T.Auto:
                default:
                    return ("bolt", "n.auto", "nd.auto", 0f, s => s.autoAttack = true);
            }
        }

        static UpgradeNode Make(List<UpgradeNode> list, string id, string parent, T t, int tier)
        {
            var e = Effect(t, tier);
            var n = new UpgradeNode(id, parent, Vector2.zero, e.icon, e.label, Cost(tier), tier, e.desc, e.descArg, e.apply);
            list.Add(n);
            return n;
        }

        static void Rep(List<UpgradeNode> list, string id, string parent, T t, int costIdx)
        {
            var e = Effect(t, RepeatTier);
            list.Add(new UpgradeNode(id, parent, Vector2.zero, e.icon, e.label, RepCost[costIdx], RepeatTier, e.desc, e.descArg, e.apply, true));
        }

        // 가지 하나를 parent 밑에 쭉 — from 번째 노드부터(머리 노드를 이미 만들었으면 1부터). 마지막 id 를 돌려준다.
        static string Lane(List<UpgradeNode> list, LaneDef d, string parent, int from = 0)
        {
            for (int k = from; k < d.len; k++)
            {
                string id = d.key + k;
                Make(list, id, parent, d.type, TierOf(d, k));
                parent = id;
            }
            return parent;
        }

        // 자식 순서 = 화면 왼쪽→오른쪽. 이어지는 가지를 가운데에 둬서 머리 노드 바로 위로 곧게 뻗게.
        public static List<UpgradeNode> BuildAll()
        {
            var list = new List<UpgradeNode>();
            list.Add(new UpgradeNode(RootId, null, Vector2.zero, "hex", "n.core", 0, 0, "nd.core", 0f, s => s.flatBonus += 8f));

            // 공격 — 머리는 자동공격(첫 구매)
            Make(list, "auto", RootId, T.Auto, 0);
            Rep(list, "rmult",  Lane(list, LMult, "auto"),  T.RMult, 1);
            Rep(list, "rflat",  Lane(list, LFlat, "auto"),  T.RFlat, 0);
            Rep(list, "rspeed", Lane(list, LSpeed, "auto"), T.RSpeed, 2);

            // 치명/범위 — 머리 = 범위 1
            Make(list, LRange.key + 0, RootId, T.Range, TierOf(LRange, 0));
            Lane(list, LCritC, LRange.key + 0);
            Rep(list, "rrange", Lane(list, LRange, LRange.key + 0, 1), T.RRange, 3);
            Rep(list, "rcritx", Lane(list, LCritX, LRange.key + 0), T.RCritX, 4);

            // 경제 — 머리 = 골드 1
            Make(list, LGold.key + 0, RootId, T.Gold, TierOf(LGold, 0));
            Lane(list, LTime, LGold.key + 0);
            Rep(list, "rgold", Lane(list, LGold, LGold.key + 0, 1), T.RGold, 5);
            Lane(list, LSkip, LGold.key + 0);

            // 소환/백신 — 머리 = 소환 1, 그 위에 [백신] 해금 노드가 따로 갈라져 3갈래
            Make(list, LSpawn.key + 0, RootId, T.Spawn, TierOf(LSpawn, 0));
            Lane(list, LSCount, LSpawn.key + 0);
            Lane(list, LSpawn, LSpawn.key + 0, 1);
            Make(list, "bomb", LSpawn.key + 0, T.Bomb, BombUnlockTier);
            Lane(list, LBDmg, "bomb");
            Lane(list, LBRad, "bomb");
            Lane(list, LBFreq, "bomb");

            // 자동 타워 — 별도 탭(tab 1). 코어에서 좌/중앙/우 3줄기.
            for (int i = 0; i < 3; i++) TowerLane(list, i, RootId);

            return list;
        }

        // ============================================================
        // 메타(환생) 강화 — 환생으로도 유지되는 영구 업그레이드. 재화 = shard.
        //   baseStats 에 한 번 적용된다(런/스테이지마다 이득). 트리 아님, 그냥 목록.
        // ============================================================
        public class MetaNode
        {
            public readonly string id, label, desc;
            public readonly int cost, maxLv, unlockAsc;   // unlockAsc = 이 노드가 보이려면 필요한 승천 레벨(0=항상)
            public readonly Action<Stats, int> apply;   // (baseStats, 레벨)
            public MetaNode(string id, string label, string desc, int cost, int maxLv, int unlockAsc, Action<Stats, int> apply)
            { this.id = id; this.label = label; this.desc = desc; this.cost = cost; this.maxLv = maxLv; this.unlockAsc = unlockAsc; this.apply = apply; }
        }

        public static List<MetaNode> BuildMeta()
        {
            return new List<MetaNode>
            {
                new MetaNode("m_dmg",   "m.dmg",   "md.dmg", 3, 10, 0,
                    (s, lv) => s.multBucketPercent += 8f * lv),
                new MetaNode("m_spd",   "m.spd",   "md.spd",   4, 8, 0,
                    (s, lv) => s.attackInterval = Mathf.Max(0.06f, s.attackInterval * Mathf.Pow(0.94f, lv))),
                new MetaNode("m_gold",  "m.gold",   "md.gold",     3, 10, 0,
                    (s, lv) => s.goldMultPercent += 15f * lv),
                new MetaNode("m_range", "m.range",   "md.range",    5, 6, 0,
                    (s, lv) => s.cursorRadius += 0.08f * lv),
                new MetaNode("m_crit",  "m.crit",   "md.crit",     5, 8, 0,
                    (s, lv) => s.critChance = Mathf.Min(CritCap, s.critChance + 0.04f * lv)),
                new MetaNode("m_start", "m.start", "md.start",  4, 10, 0,
                    (s, lv) => { /* 시작 골드는 GameManager 에서 metaLv 로 처리 */ }),
                new MetaNode("m_auto",  "m.auto", "md.auto", 6, 1, 0,
                    (s, lv) => { if (lv > 0) s.autoAttack = true; }),
                // ---- 승천으로만 열리는 노드 — "승천에 따른 강화요소 추가" ----
                new MetaNode("m_critx", "m.critx", "md.critx", 6, 6, 1,
                    (s, lv) => s.critMult += 0.05f * lv),
                new MetaNode("m_time",  "m.time",  "md.time",  5, 8, 2,
                    (s, lv) => s.bonusTimeSec += 3f * lv),
            };
        }
    }
}
