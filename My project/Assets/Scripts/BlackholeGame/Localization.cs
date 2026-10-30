using System.Collections.Generic;
using UnityEngine;

namespace BlackholeGame
{
    public enum Lang { KO = 0, JA = 1 }

    // ============================================================
    // 문자열 표. 여기는 한국어, 일본어는 LocalizationJa.cs.
    //   플레인:  Loc.T("key")
    //   서식:    Loc.F("key", args)   // "{0}" 치환
    // ============================================================
    public static partial class Loc
    {
        const string PrefKey = "popcell.lang";
        public static Lang Cur = Lang.KO;
        public static readonly string[] LangNames = { "한국어", "日本語" };

        public static void LoadPref()
        {
            Cur = (Lang)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, LangNames.Length - 1);
        }

        public static void SetLang(Lang l)
        {
            Cur = l;
            PlayerPrefs.SetInt(PrefKey, (int)l);
            PlayerPrefs.Save();
        }

        public static string T(string key)
        {
            if (Cur == Lang.JA && JaTable.TryGetValue(key, out var ja)) return ja;
            return Table.TryGetValue(key, out var v) ? v : key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        // key -> 문구
        static readonly Dictionary<string, string> Table = new Dictionary<string, string>
        {
            // ---- 공통 / 타이틀 ----
            ["title.sub"]      = "세포 팝 — 인크리멘탈 클리커",
            ["title.start"]    = "게임 시작",
            ["title.continue"] = "이어하기",
            ["title.new"]      = "새로 시작",
            ["title.newConfirm"] = "기록 지우고 새로 시작?",
            ["title.settings"] = "설정",
            ["title.quit"]     = "게임 종료",
            ["title.ascendLevel"] = "변이 단계",
            ["title.ascendDesc"] = "적 체력 ×{0}   보스 체력 ×{1}   골드 ×{2}",
            ["title.ascendNote"] = "클리어 시 메타 강화·배양·정수 초기화 · 새 노드·상한 해금",
            ["title.patNone"]  = "보스 패턴 없음",
            ["title.patPrefix"] = "보스 패턴: {0}",
            ["title.patCrit"]  = "치명 내성",
            ["title.patTeleport"] = "가속 순간이동",
            ["title.patShield"] = "무적 페이즈",
            ["common.back"]    = "뒤로",
            ["common.toTitle"] = "타이틀로",
            ["common.toMap"]   = "지도로",

            // ---- 설정 ----
            ["set.bgm"]        = "배경음악",
            ["set.sfx"]        = "효과음",
            ["set.lang"]       = "언어",
            ["set.res"]        = "해상도      1920 × 1080            (준비 중)",

            // ---- HUD ----
            ["hud.wave"]       = "웨이브 {0} / {1}",
            ["hud.boss"]       = "── 보스 ──",
            ["hud.clickHint"]  = "클릭해서 공격",

            // ---- 일시정지 ----
            ["pause.title"]    = "일시정지",
            ["pause.menu"]     = "메뉴",
            ["pause.resume"]   = "계속하기",
            ["pause.return"]   = "돌아가기",
            ["pause.endWave"]  = "웨이브 종료",

            // ---- 지역 맵 ----
            ["map.title"]      = "지역 선택",
            ["map.info"]       = "보유 ${0}   ·   투입 {1}회   ·   클리어 {2}/{3}",
            ["map.infoAsc"]    = "보유 ${0}   ·   투입 {1}회   ·   클리어 {2}/{3}   ·   변이 {4}",
            ["map.locked"]     = "잠김",
            ["map.cleared"]    = "클리어됨 · 재도전 가능",
            ["map.wavesBoss"]  = "{0}웨이브 · 보스",
            ["map.upgrade"]    = "업그레이드",
            ["map.meta"]       = "메타 강화",
            ["map.metaN"]      = "메타 강화 ({0})",
            ["map.rebirth"]    = "과거로 돌아가기 (+{0})",
            ["map.rebirthInfo"]= "과거로 돌아가면 진행 초기화 · shard +{0} (누적 {1})",
            ["map.rebirthLock"]= "2지역 클리어 후 사용 가능",
            ["map.rebirthAskTitle"] = "과거로 돌아갈까요?",
            ["map.rebirthAskBody"]  = "지금까지의 진행(산 노드·골드·클리어한 지역)이 초기화됩니다.\n대신 shard +{0} 를 얻습니다.\n메타 강화·배양·정수는 그대로 남습니다.",
            ["map.rebirthYes"]      = "돌아가기",
            ["map.rebirthNo"]       = "취소",

            // ---- 메타 화면 ----
            ["meta.title"]     = "미래의 지식 · 각성",
            ["meta.shard"]     = "shard {0}",
            ["meta.lv"]        = "{0}   Lv {1}/{2}",
            ["meta.cost"]      = "{0} shard",
            ["meta.max"]       = "MAX",
            ["meta.lockAsc"]   = "변이 {0}단계부터 해금",

            // ---- 결과 / 승리 ----
            ["res.title"]      = "— {0}지역 · 웨이브 {1} 종료 —",
            ["res.titleClear"] = "— {0}지역 · 감염원 제거 —",
            ["res.cycle"]      = "이번 싸이클",
            ["res.kills"]      = "처치  {0}",
            ["res.gain"]       = "획득  +${0}",
            ["res.have"]       = "보유  ${0}",
            ["res.retry"]      = "재도전",
            ["win.title"]      = "감염원 제거 완료 — CLEAR",
            ["win.line1"]      = "이번 판  ·  처치 {0}",
            ["win.line2"]      = "획득  +${0}      보유  ${1}",
            ["win.ascend"]     = "변이 {0}단계 돌파 · shard +{1} · 메타 강화 확장",
            ["win.next"]       = "다음 변이로",
            ["win.unlocked"]   = "감염원이 변이하기 시작합니다!",
            ["win.time"]       = "소요 시간  {0}:{1}",
            ["win.goldEarned"] = "획득  ${0}",
            ["win.nextLevel"]  = "다음 판 변이 단계",

            // ---- 스탯 패널 ----
            ["stat.dmg"]       = "공격력",
            ["stat.crit"]      = "치명타",
            ["stat.aspd"]      = "공격 속도",
            ["stat.range"]     = "공격 범위",
            ["stat.gold"]      = "골드 획득",
            ["stat.spawn"]     = "소환",
            ["stat.time"]      = "제한 시간",
            ["stat.startWave"] = "시작 웨이브",
            ["stat.aspdVal"]   = "초당 {0}회",
            ["stat.spawnVal"]  = "×{0} · {1}마리",
            ["stat.timeVal"]   = "{0}초",
            ["stat.bomb"]      = "백신",
            ["stat.bombVal"]   = "×{0} · 범위×{1} · {2}초",
            ["stat.bombOff"]   = "잠김",
            ["stat.cur"]       = "현재 능력치",
            ["stat.nodes"]     = "해금 노드",

            // ---- 트리 ----
            ["tree.owned"]     = "보유 중",
            ["tree.free"]      = "무료",
            ["tree.lockTier"]  = "{0}지역 클리어 시 해금",
            ["tree.needPrev"]  = "이전 노드를 먼저 해금해야 합니다",
            ["tree.needAll"]   = "이어진 세 갈래를 모두 해금해야 합니다",
            ["tree.needGold"]  = "골드가 부족합니다 ({0} 더 필요)",
            ["tree.reset"]     = "화면 리셋",
            ["stat.tower"]     = "타워",
            ["stat.towerOff"]  = "없음",
            ["stat.towerVal"]  = "{0}대 ×{1} · {2}초",
            ["tree.tabCursor"] = "커서 강화",
            ["tree.tabTower"]  = "타워 강화",
            ["tree.tabCulture"]  = "배양",
            ["tree.tabEssence"]  = "정수",
            ["tree.essTitle"]    = "보스 정수",
            ["tree.essHint"]     = "지역 보스를 잡으면 그 지역의 정수가 하나 떨어진다.",
            ["ess.0"]            = "배양 정수",
            ["ess.1"]            = "변이 정수",
            ["ess.2"]            = "잔해 정수",
            ["ess.3"]            = "오수 정수",
            ["ess.4"]            = "염수 정수",
            ["ess.5"]            = "포자 정수",
            ["ess.6"]            = "빙결 정수",
            ["essd.0"]           = "고정 공격력 +{0}",
            ["essd.1"]           = "공격 속도 +{0}%",
            ["essd.2"]           = "타워 고정 공격력 +{0}",
            ["essd.3"]           = "골드 획득 +{0}%",
            ["essd.4"]           = "모든 피해 +{0}%",
            ["essd.5"]           = "커서 범위 +{0}%",
            ["essd.6"]           = "보스 피해 +{0}%",
            ["ess.owned"]        = "보유 {0}",
            ["ess.per"]          = "레벨당 {0}",
            ["ess.next"]         = "Lv {0} · 다음 {1}개",
            ["ess.max"]          = "Lv {0} · 최대",
            ["ess.locked"]       = "아직 못 깬 지역",
            ["res.essence"]      = "정수  +{0}",
            ["hud.combo"]        = "{0} 연속",
            ["hud.comboBonus"]   = "골드 +{0}%",
            ["hud.streak"]       = "연속 클리어 {0} · 골드 +{1}%",
            ["tree.cultureTitle"]= "배양 연구소",
            ["tree.cultureRate"] = "초당 +${0}",
            ["n.culture0"]       = "연구원 수",
            ["n.culture1"]       = "배양 산출",
            ["n.culture2"]       = "연구 속도",
            ["cu.workers"]       = "{0} / {1}명이 배양을 돌린다",
            ["cu.load"]          = "1회 배양당 ${0}",
            ["cu.speed"]         = "배양 주기 {0}초",
            ["cu.needWorker"]    = "연구원이 없으면 소용없다",
            ["save.rebalanced"]  = "밸런스가 새로 짜여 이번 회차는 처음부터 — 샤드·메타 강화·정수·승천·배양은 그대로입니다",
            ["cu.buy"]           = "강화",
            ["cheat.gold"]      = "치트: 골드 +{0:N0} (보유 {1:N0})",
            ["cheat.shards"]    = "치트: 샤드 +{0:N0} (보유 {1:N0})",
            ["cheat.wipe"]      = "치트: 화면의 적 {0}마리 전멸",
            ["cheat.stage"]     = "치트: {0}지역까지 해금",
            ["cheat.allStages"] = "치트: 모든 지역 해금",
            ["cheat.allNodes"]  = "치트: 모든 노드 해금",
            ["cheat.allNodes2"] = "치트: 노드 {0}개 해금",
            ["tree.repStatus"] = "Lv {0} · 1회 ${1}",
            ["tree.repMax"]    = "최대 +{0}",
            ["tree.repTitle"]  = "영구 강화 ∞",
            ["tree.repMaxShort"] = "최대",
            ["common.close"]   = "닫기",
            ["tree.repOpen"]   = "영구 강화 열기",
            ["tree.repSummary"]= "총 Lv {0} · 열린 항목 {1}개",
            ["tree.repLocked"] = "가지 끝까지 뚫어야 열림",
            ["tree.startWaveN"]= "웨이브 {0} 시작",
            ["tree.help"]      = "노드에 마우스를 올리면 효과.  ·  구매하면 그 다음 노드가 드러납니다.\nWASD / 우클릭 드래그: 상하좌우 이동",

            // ---- 노드 라벨 ----
            ["n.flat"]  = "공격력",
            ["n.mult"]  = "공격 배수",
            ["n.speed"] = "공격 속도",
            ["n.critc"] = "치명 확률",
            ["n.critx"] = "치명 배수",
            ["n.range"] = "공격 범위",
            ["n.gold"]  = "골드 획득",
            ["n.time"]  = "시작 시간",
            ["n.spawn"] = "소환 가속",
            ["n.scount"]= "동시 소환",
            ["n.skip"]  = "웨이브 스킵",
            ["n.ctime"] = "콤보 유지",
            ["n.cmul"]  = "콤보 배율",
            ["n.auto"]  = "자동 공격",
            ["n.core"]  = "코어",
            // ---- 자동 타워 (round47) ----
            ["n.towerL"] = "왼쪽 타워",
            ["n.towerC"] = "중앙 타워",
            ["n.towerR"] = "오른쪽 타워",
            ["n.towerBoss"] = "보스 특화",
            // ---- 반복 강화 (round44) ----
            ["n.rflat"]  = "공격력 강화 ∞",
            ["n.rmult"]  = "공격 배수 강화 ∞",
            ["n.rspeed"] = "공격 속도 강화 ∞",
            ["n.rrange"] = "공격 범위 강화 ∞",
            ["n.rcritx"] = "치명 배수 강화 ∞",
            ["n.rgold"]  = "골드 획득 강화 ∞",
            ["n.bomb"]     = "백신",
            ["n.bombdmg"]  = "백신 위력",
            ["n.bombrad"]  = "백신 범위",
            ["n.bombfreq"] = "백신 빈도",
            ["n.bombtime"] = "백신 지속",
            // ---- 노드 설명 ({0} = 수치) ----
            ["nd.flat"]  = "고정 공격력 +{0}",
            ["nd.mult"]  = "공격 배수 +{0:0.##}%p",
            ["nd.speed"] = "타격 간격 ×{0:0.###}",
            ["nd.critc"] = "치명타 확률 +{0:0.##}%p",
            ["nd.critx"] = "치명타 배수 +{0:0.##}",
            ["nd.range"] = "커서 타격 범위 +{0:0.##}",
            ["nd.gold"]  = "처치 시 골드 +{0:0.##}%",
            ["nd.time"]  = "시작 제한시간 +{0:0.##}초",
            ["nd.spawn"] = "적 소환 간격 ×{0:0.##}",
            ["nd.scount"]= "동시 소환 마릿수 +1",
            ["nd.skip"]  = "시작 웨이브 +{0:0}",
            ["nd.ctime"] = "콤보가 끊기기까지 +{0:0.##}초",
            ["nd.cmul"]  = "연속 1회당 골드 보너스 +{0:0.##}%p",
            ["nd.auto"]  = "커서 호버 자동 공격 해금 — 이 노드 전엔 클릭으로 공격",
            ["nd.core"]  = "고정 공격력 +8",
            ["nd.towerOn"]   = "이 자리에 자동 타워 설치 — 제자리에서 돌며 탄알을 쏜다",
            ["nd.towerDmg"]  = "타워 공격력 +{0:0.##}배 (커서 타격 기준)",
            ["nd.towerFlat"] = "타워 고정 공격력 +{0:N0}",
            ["nd.towerBoss"] = "타워가 보스에게 주는 피해 +{0:0.##}배 (최대 1.2)",
            ["nd.towerSpd"]  = "타워 발사 간격 ×{0:0.##}",
            ["nd.towerBurst"]= "연발 — 한 번에 3발을 부채꼴로",
            ["nd.towerSpin"] = "타워 회전 속도 +{0:0}도/초",
            ["nd.towerFlame"]= "화염방사 — 총구 앞을 부채꼴로 지진다. 발사 간격 대신 내 공격 속도를 따른다",
            ["nd.towerLaser"]= "레이저 — 총구 방향으로 꿰뚫는 빔. 내 공격 속도를 따른다",
            ["nd.towerBeam"]  = "분사구 +1 — 같은 각도 간격으로 동시에 뻗는다(최대 3)",
            ["nd.towerTarget"]= "저격 표적 +1 — 보스 말고 잡몹도 같이 노린다(최대 3)",
            ["nd.towerSniper"]="저격 — 가장 단단한 적(보스 우선)을 조준해 강하게 한 발",
            ["tree.pattern"] = "공격 패턴",
            ["pat.shot"]     = "단발",
            ["pat.burst"]    = "연발",
            ["pat.flame"]    = "화염",
            ["pat.laser"]    = "레이저",
            ["pat.sniper"]   = "저격",
            // round51: 영구 강화 패널에 들어갈 짧은 효과 문구(한 줄에 들어가야 한다)
            ["rs.rflat"]  = "1회 +{0:0.##} 공격력",
            ["rs.rmult"]  = "1회 +{0:0.##}%p 공격 배수",
            ["rs.rspeed"] = "1회 +{0:0.##}회/초 공격 속도",
            ["rs.rrange"] = "1회 +{0:0.##} 공격 범위",
            ["rs.rcritx"] = "1회 +{0:0.##} 치명 배수",
            ["rs.rgold"]  = "1회 +{0:0.##}% 골드 획득",
            ["nd.rflat"]  = "반복 강화 — 1회마다 고정 공격력 +{0:0.##}",
            ["nd.rmult"]  = "반복 강화 — 1회마다 공격 배수 +{0:0.##}%p (상한 없음)",
            ["nd.rspeed"] = "반복 강화 — 1회마다 초당 공격 +{0:0.##}회",
            ["nd.rrange"] = "반복 강화 — 1회마다 커서 범위 +{0:0.##}",
            ["nd.rcritx"] = "반복 강화 — 1회마다 치명타 배수 +{0:0.##}",
            ["nd.rgold"]  = "반복 강화 — 1회마다 처치 골드 +{0:0.##}%",
            ["nd.bomb"]     = "백신 캡슐이 나타나기 시작 — 터뜨리면 주변 적에게 광역 피해",
            ["nd.bombdmg"]  = "폭발 피해 +{0:0.##}배 (보스 최대체력 비율도 상승)",
            ["nd.bombrad"]  = "폭발 범위 +{0:0.##}배",
            ["nd.bombfreq"] = "캡슐 출현 간격 -{0:0.##}초",
            ["nd.bombtime"] = "백신 장판 지속 +{0:0.##}초",

            // ---- 메타 노드 ----
            ["m.dmg"]    = "각성 · 공격",
            ["md.dmg"]   = "영구 공격력 배수 +8%p / 레벨",
            ["m.spd"]    = "각성 · 속도",
            ["md.spd"]   = "영구 타격 간격 ×0.94 / 레벨",
            ["m.gold"]   = "각성 · 재화",
            ["md.gold"]  = "영구 골드 획득 +15% / 레벨",
            ["m.range"]  = "각성 · 범위",
            ["md.range"] = "영구 커서 범위 +0.08 / 레벨",
            ["m.crit"]   = "각성 · 치명",
            ["md.crit"]  = "영구 치명 확률 +3%p / 레벨",
            ["m.start"]  = "미래 지식 · 자금",
            ["md.start"] = "런 시작 골드 +2,000 / 레벨",
            ["m.auto"]   = "미래 지식 · 자동",
            ["md.auto"]  = "과거로 돌아간 뒤 자동 공격 즉시 해금",
            ["m.critx"]  = "변이 · 치명 배율",
            ["md.critx"] = "영구 치명 배수 +0.15 / 레벨",
            ["m.time"]   = "변이 · 여유",
            ["md.time"]  = "영구 시작 제한시간 +3초 / 레벨",

            // ---- 스테이지 이름 ----
            ["s.0"] = "감염 구역 · 실험실",
            ["s.1"] = "정착지 외곽",
            ["s.2"] = "붕괴한 도심",
            ["s.3"] = "지하 수로망",
            ["s.4"] = "격리 항만",
            ["s.5"] = "오염 산림",
            ["s.6"] = "설원 연구기지",
            ["s.7"] = "근원 · 최심부",
        };
    }
}
