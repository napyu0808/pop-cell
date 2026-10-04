using System.Collections.Generic;
using UnityEngine;

namespace BlackholeGame
{
    public enum Lang { KO = 0, JA = 1, EN = 2 }

    // ============================================================
    // 다국어. 문자열은 키로 참조. 인덱스 0=한, 1=일, 2=영.
    //   플레인:  Loc.T("key")
    //   서식:    Loc.F("key", args)   // "{0}" 치환
    // ============================================================
    public static class Loc
    {
        const string PrefKey = "popcell.lang";
        public static Lang Cur = Lang.KO;

        public static readonly string[] LangNames = { "한국어", "日本語", "English" };

        public static void LoadPref()
        {
            Cur = (Lang)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 2);
        }

        public static void SetLang(Lang l)
        {
            Cur = l;
            PlayerPrefs.SetInt(PrefKey, (int)l);
            PlayerPrefs.Save();
        }

        public static string T(string key)
        {
            if (Table.TryGetValue(key, out var a))
            {
                int i = (int)Cur;
                return (i < a.Length && !string.IsNullOrEmpty(a[i])) ? a[i] : a[0];
            }
            return key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        // key -> { 한, 일, 영 }
        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // ---- 공통 / 타이틀 ----
            ["title.sub"]      = new[] { "세포 팝 — 인크리멘탈 클리커", "セル・ポップ — インクリメンタルクリッカー", "POP Cell — an incremental clicker" },
            ["title.start"]    = new[] { "게임 시작", "ゲーム開始", "Start" },
            ["title.continue"] = new[] { "이어하기", "つづきから", "Continue" },
            ["title.new"]      = new[] { "새로 시작", "はじめから", "New Game" },
            ["title.newConfirm"] = new[] { "기록 지우고 새로 시작?", "記録を消して始める?", "Erase save & restart?" },
            ["title.settings"] = new[] { "설정", "設定", "Settings" },
            ["title.quit"]     = new[] { "게임 종료", "終了", "Quit" },
            ["title.ascendLevel"] = new[] { "변이 단계", "変異段階", "Mutation Stage" },
            ["title.ascendDesc"] = new[] { "적 체력 ×{0}   보스 체력 ×{1}   골드 ×{2}", "敵体力 ×{0}   ボス体力 ×{1}   ゴールド ×{2}", "Enemy HP ×{0}   Boss HP ×{1}   Gold ×{2}" },
            ["title.ascendNote"] = new[] { "클리어 시 메타 강화 초기화 · 새 노드·상한 해금", "クリア時メタ強化リセット · 新ノード・上限解放", "Clearing resets meta upgrades · unlocks new nodes/caps" },
            ["title.patNone"]  = new[] { "보스 패턴 없음", "ボスパターンなし", "No boss patterns" },
            ["title.patPrefix"] = new[] { "보스 패턴: {0}", "ボスパターン: {0}", "Boss patterns: {0}" },
            ["title.patCrit"]  = new[] { "치명 내성", "クリティカル耐性", "Crit resist" },
            ["title.patTeleport"] = new[] { "가속 순간이동", "高速テレポート", "Faster teleport" },
            ["title.patShield"] = new[] { "무적 페이즈", "無敵フェイズ", "Shield phase" },
            ["common.back"]    = new[] { "뒤로", "もどる", "Back" },
            ["common.toTitle"] = new[] { "타이틀로", "タイトルへ", "To Title" },
            ["common.toMap"]   = new[] { "지도로", "マップへ", "To Map" },

            // ---- 설정 ----
            ["set.bgm"]        = new[] { "배경음악", "BGM", "Music" },
            ["set.sfx"]        = new[] { "효과음", "効果音", "SFX" },
            ["set.lang"]       = new[] { "언어", "言語", "Language" },
            ["set.res"]        = new[] { "해상도      1920 × 1080            (준비 중)", "解像度      1920 × 1080            (準備中)", "Resolution   1920 × 1080          (WIP)" },

            // ---- HUD ----
            ["hud.wave"]       = new[] { "웨이브 {0} / {1}", "ウェーブ {0} / {1}", "Wave {0} / {1}" },
            ["hud.boss"]       = new[] { "── 보스 ──", "── ボス ──", "── BOSS ──" },
            ["hud.clickHint"]  = new[] { "클릭해서 공격", "クリックで攻撃", "Click to attack" },

            // ---- 일시정지 ----
            ["pause.title"]    = new[] { "일시정지", "ポーズ", "Paused" },
            ["pause.menu"]     = new[] { "메뉴", "メニュー", "Menu" },
            ["pause.resume"]   = new[] { "계속하기", "つづける", "Resume" },
            ["pause.return"]   = new[] { "돌아가기", "もどる", "Return" },
            ["pause.endWave"]  = new[] { "웨이브 종료", "ウェーブ終了", "End Wave" },

            // ---- 지역 맵 ----
            ["map.title"]      = new[] { "지역 선택", "エリア選択", "Select Area" },
            ["map.info"]       = new[] { "보유 ${0}   ·   투입 {1}회   ·   클리어 {2}/{3}", "所持 ${0}   ·   出撃 {1}回   ·   クリア {2}/{3}", "Gold ${0}   ·   Sorties {1}   ·   Cleared {2}/{3}" },
            ["map.infoAsc"]    = new[] { "보유 ${0}   ·   투입 {1}회   ·   클리어 {2}/{3}   ·   변이 {4}", "所持 ${0}   ·   出撃 {1}回   ·   クリア {2}/{3}   ·   変異 {4}", "Gold ${0}   ·   Sorties {1}   ·   Cleared {2}/{3}   ·   Mutation {4}" },
            ["map.locked"]     = new[] { "잠김", "ロック", "Locked" },
            ["map.cleared"]    = new[] { "클리어됨 · 재도전 가능", "クリア済 · 再挑戦可", "Cleared · replayable" },
            ["map.wavesBoss"]  = new[] { "{0}웨이브 · 보스", "{0}ウェーブ · ボス", "{0} waves · boss" },
            ["map.upgrade"]    = new[] { "업그레이드", "アップグレード", "Upgrades" },
            ["map.meta"]       = new[] { "메타 강화", "メタ強化", "Meta" },
            ["map.metaN"]      = new[] { "메타 강화 ({0})", "メタ強化 ({0})", "Meta ({0})" },
            ["map.rebirth"]    = new[] { "과거로 돌아가기 (+{0})", "過去へ戻る (+{0})", "Rewind Time (+{0})" },
            ["map.rebirthInfo"]= new[] { "과거로 돌아가면 진행 초기화 · shard +{0} (누적 {1}) · 변이 {2} → {3}", "過去へ戻ると進行リセット · shard +{0} (累計 {1}) · 変異 {2} → {3}", "Rewinding resets progress · +{0} shards (total {1}) · Mutation {2} → {3}" },
            ["map.rebirthLock"]= new[] { "2지역 클리어 후 사용 가능", "エリア2クリア後に使用可", "Unlocks after Area 2" },

            // ---- 메타 화면 ----
            ["meta.title"]     = new[] { "미래의 지식 · 각성", "未来の知識 · 覚醒", "Knowledge of the Future" },
            ["meta.shard"]     = new[] { "shard {0}", "shard {0}", "shards {0}" },
            ["meta.lv"]        = new[] { "{0}   Lv {1}/{2}", "{0}   Lv {1}/{2}", "{0}   Lv {1}/{2}" },
            ["meta.cost"]      = new[] { "{0} shard", "{0} shard", "{0} shards" },
            ["meta.max"]       = new[] { "MAX", "MAX", "MAX" },
            ["meta.lockAsc"]   = new[] { "변이 {0}단계부터 해금", "変異{0}段階から解放", "Unlocks at Mutation Stage {0}" },

            // ---- 결과 / 승리 ----
            ["res.title"]      = new[] { "— {0}지역 · 웨이브 {1} 종료 —", "— エリア{0} · ウェーブ{1} 終了 —", "— Area {0} · Wave {1} ended —" },
            ["res.titleClear"] = new[] { "— {0}지역 · 감염원 제거 —", "— エリア{0} · 感染源除去 —", "— Area {0} · Source eradicated —" },
            ["res.cycle"]      = new[] { "이번 싸이클", "このサイクル", "This cycle" },
            ["res.kills"]      = new[] { "처치  {0}", "撃破  {0}", "Kills  {0}" },
            ["res.gain"]       = new[] { "획득  +${0}", "獲得  +${0}", "Gained  +${0}" },
            ["res.have"]       = new[] { "보유  ${0}", "所持  ${0}", "Gold  ${0}" },
            ["res.retry"]      = new[] { "재도전", "再挑戦", "Retry" },
            ["win.title"]      = new[] { "감염원 제거 완료 — CLEAR", "感染源 除去完了 — CLEAR", "Source eradicated — CLEAR" },
            ["win.line1"]      = new[] { "이번 판  ·  처치 {0}", "今回  ·  撃破 {0}", "This run  ·  kills {0}" },
            ["win.line2"]      = new[] { "획득  +${0}      보유  ${1}", "獲得  +${0}      所持  ${1}", "Gained  +${0}      Gold  ${1}" },
            ["win.ascend"]     = new[] { "변이 {0}단계 돌파 · shard +{1} · 메타 강화 확장", "変異{0}段階突破 · shard +{1} · メタ強化拡張", "Mutation Stage {0} broken through · +{1} shards · meta tree expanded" },
            ["win.next"]       = new[] { "다음 변이로", "次の変異へ", "To the Next Mutation" },
            ["win.unlocked"]   = new[] { "감염원이 변이하기 시작합니다!", "感染源が変異を始めました！", "The source has begun to mutate!" },
            ["win.time"]       = new[] { "소요 시간  {0}:{1}", "所要時間  {0}:{1}", "Time  {0}:{1}" },
            ["win.goldEarned"] = new[] { "획득  ${0}", "獲得  ${0}", "Gained  ${0}" },
            ["win.nextLevel"]  = new[] { "다음 판 변이 단계", "次のラン・変異段階", "Next Run — Mutation Stage" },

            // ---- 스탯 패널 ----
            ["stat.dmg"]       = new[] { "공격력", "攻撃力", "Damage" },
            ["stat.crit"]      = new[] { "치명타", "クリティカル", "Crit" },
            ["stat.aspd"]      = new[] { "공격 속도", "攻撃速度", "Atk Speed" },
            ["stat.range"]     = new[] { "공격 범위", "攻撃範囲", "Range" },
            ["stat.gold"]      = new[] { "골드 획득", "ゴールド獲得", "Gold Gain" },
            ["stat.spawn"]     = new[] { "소환", "湧き", "Spawn" },
            ["stat.time"]      = new[] { "제한 시간", "制限時間", "Time Limit" },
            ["stat.startWave"] = new[] { "시작 웨이브", "開始ウェーブ", "Start Wave" },
            ["stat.aspdVal"]   = new[] { "초당 {0}회", "毎秒 {0}回", "{0}/s" },
            ["stat.spawnVal"]  = new[] { "×{0} · {1}마리", "×{0} · {1}体", "×{0} · {1}" },
            ["stat.timeVal"]   = new[] { "{0}초", "{0}秒", "{0}s" },
            ["stat.bomb"]      = new[] { "백신", "ワクチン", "Vaccine" },
            ["stat.bombVal"]   = new[] { "×{0} · 범위×{1} · {2}초", "×{0} · 範囲×{1} · {2}秒", "×{0} · r×{1} · {2}s" },
            ["stat.bombOff"]   = new[] { "잠김", "未解放", "Locked" },
            ["stat.cur"]       = new[] { "현재 능력치", "現在の能力値", "Current Stats" },
            ["stat.nodes"]     = new[] { "해금 노드", "解放ノード", "Nodes" },

            // ---- 트리 ----
            ["tree.owned"]     = new[] { "보유 중", "取得済", "Owned" },
            ["tree.free"]      = new[] { "무료", "無料", "Free" },
            ["tree.lockTier"]  = new[] { "{0}지역 클리어 시 해금", "エリア{0}クリアで解放", "Unlocks at Area {0}" },
            ["tree.reset"]     = new[] { "화면 리셋", "表示リセット", "Reset View" },
            ["tree.toTop"]     = new[] { "맨 위로", "最上部へ", "Top" },
            ["tree.toBottom"]  = new[] { "맨 아래로", "最下部へ", "Bottom" },
            ["cheat.shards"]    = new[] { "치트: 샤드 +{0} (보유 {1})", "チート: シャード +{0}（所持 {1}）", "Cheat: shards +{0} (have {1})" },
            ["cheat.stage"]     = new[] { "치트: {0}지역까지 해금", "チート: エリア{0}まで解放", "Cheat: unlocked up to Area {0}" },
            ["cheat.allStages"] = new[] { "치트: 모든 지역 해금", "チート: 全エリア解放", "Cheat: all areas unlocked" },
            ["cheat.allNodes"]  = new[] { "치트: 모든 노드 해금", "チート: 全ノード解放", "Cheat: all nodes unlocked" },
            ["tree.repStatus"] = new[] { "Lv {0} · 1회 ${1}", "Lv {0} · 1回 ${1}", "Lv {0} · ${1} each" },
            ["tree.repMax"]    = new[] { "최대 +{0}", "最大 +{0}", "Max +{0}" },
            ["tree.repTitle"]  = new[] { "영구 강화 ∞", "永久強化 ∞", "Endless Upgrades ∞" },
            ["tree.repMaxShort"] = new[] { "최대", "最大", "Max" },
            ["tree.repLocked"] = new[] { "가지 끝까지 뚫어야 열림", "枝を最後まで解放すると開く", "Unlocks at the end of its lane" },
            ["tree.startWaveN"]= new[] { "웨이브 {0} 시작", "ウェーブ {0} 開始", "Start Wave {0}" },
            ["tree.help"]      = new[] { "노드에 마우스를 올리면 효과.  ·  구매하면 그 다음 노드가 드러납니다.\n휠 / 우클릭 드래그: 위아래 스크롤",
                                        "ノードにカーソルで効果表示.  ·  購入すると次のノードが現れます.\nホイール / 右ドラッグ: 上下スクロール",
                                        "Hover a node for its effect.  ·  Buying one reveals what comes next.\nWheel / right-drag: scroll up and down" },

            // ---- 노드 라벨 ----
            ["n.flat"]  = new[] { "공격력", "攻撃力", "Damage" },
            ["n.mult"]  = new[] { "공격 배수", "攻撃倍率", "Multiplier" },
            ["n.speed"] = new[] { "공격 속도", "攻撃速度", "Atk Speed" },
            ["n.critc"] = new[] { "치명 확률", "クリ率", "Crit Rate" },
            ["n.critx"] = new[] { "치명 배수", "クリ倍率", "Crit Dmg" },
            ["n.range"] = new[] { "공격 범위", "攻撃範囲", "Range" },
            ["n.gold"]  = new[] { "골드 획득", "ゴールド", "Gold" },
            ["n.time"]  = new[] { "시작 시간", "開始時間", "Start Time" },
            ["n.spawn"] = new[] { "소환 가속", "湧き加速", "Spawn Rate" },
            ["n.scount"]= new[] { "동시 소환", "同時湧き", "Spawn Count" },
            ["n.skip"]  = new[] { "웨이브 스킵", "ウェーブスキップ", "Wave Skip" },
            ["n.auto"]  = new[] { "자동 공격", "オート攻撃", "Auto Attack" },
            ["n.core"]  = new[] { "코어", "コア", "Core" },
            // ---- 반복 강화 (round44) ----
            ["n.rflat"]  = new[] { "공격력 강화 ∞", "攻撃力強化 ∞", "Damage ∞" },
            ["n.rmult"]  = new[] { "공격 배수 강화 ∞", "攻撃倍率強化 ∞", "Multiplier ∞" },
            ["n.rspeed"] = new[] { "공격 속도 강화 ∞", "攻撃速度強化 ∞", "Atk Speed ∞" },
            ["n.rrange"] = new[] { "공격 범위 강화 ∞", "攻撃範囲強化 ∞", "Range ∞" },
            ["n.rcritx"] = new[] { "치명 배수 강화 ∞", "クリ倍率強化 ∞", "Crit Dmg ∞" },
            ["n.rgold"]  = new[] { "골드 획득 강화 ∞", "ゴールド強化 ∞", "Gold ∞" },
            ["n.bomb"]     = new[] { "백신", "ワクチン", "Vaccine" },
            ["n.bombdmg"]  = new[] { "백신 위력", "ワクチン威力", "Vaccine Power" },
            ["n.bombrad"]  = new[] { "백신 범위", "ワクチン範囲", "Vaccine Radius" },
            ["n.bombfreq"] = new[] { "백신 빈도", "ワクチン頻度", "Vaccine Rate" },
            // ---- 노드 설명 ({0} = 수치) ----
            ["nd.flat"]  = new[] { "고정 공격력 +{0}", "固定攻撃力 +{0}", "Flat damage +{0}" },
            ["nd.mult"]  = new[] { "공격 배수 +{0:0.##}%p", "攻撃倍率 +{0:0.##}%p", "Damage mult +{0:0.##}%p" },
            ["nd.speed"] = new[] { "타격 간격 ×{0:0.###}", "攻撃間隔 ×{0:0.###}", "Attack interval ×{0:0.###}" },
            ["nd.critc"] = new[] { "치명타 확률 +{0:0.##}%p", "クリ率 +{0:0.##}%p", "Crit rate +{0:0.##}%p" },
            ["nd.critx"] = new[] { "치명타 배수 +{0:0.##}", "クリ倍率 +{0:0.##}", "Crit dmg +{0:0.##}" },
            ["nd.range"] = new[] { "커서 타격 범위 +{0:0.##}", "カーソル範囲 +{0:0.##}", "Cursor range +{0:0.##}" },
            ["nd.gold"]  = new[] { "처치 시 골드 +{0:0.##}%", "撃破ゴールド +{0:0.##}%", "Kill gold +{0:0.##}%" },
            ["nd.time"]  = new[] { "시작 제한시간 +{0:0.##}초", "開始制限時間 +{0:0.##}秒", "Start time +{0:0.##}s" },
            ["nd.spawn"] = new[] { "적 소환 간격 ×{0:0.##}", "敵湧き間隔 ×{0:0.##}", "Spawn interval ×{0:0.##}" },
            ["nd.scount"]= new[] { "동시 소환 마릿수 +1", "同時湧き +1体", "Spawn count +1" },
            ["nd.skip"]  = new[] { "시작 웨이브 +{0:0}", "開始ウェーブ +{0:0}", "Start wave +{0:0}" },
            ["nd.auto"]  = new[] { "커서 호버 자동 공격 해금 — 이 노드 전엔 클릭으로 공격", "カーソルホバー自動攻撃を解放 — これ以前はクリック攻撃", "Unlocks hover auto-attack — before this, click to attack" },
            ["nd.core"]  = new[] { "고정 공격력 +8", "固定攻撃力 +8", "Flat damage +8" },
            ["nd.rflat"]  = new[] { "반복 강화 — 1회마다 고정 공격력 +{0:0.##}", "繰り返し強化 — 1回ごとに固定攻撃力 +{0:0.##}", "Repeatable — flat damage +{0:0.##} each" },
            ["nd.rmult"]  = new[] { "반복 강화 — 1회마다 공격 배수 +{0:0.##}%p (상한 없음)", "繰り返し強化 — 1回ごとに攻撃倍率 +{0:0.##}%p（上限なし）", "Repeatable — damage mult +{0:0.##}%p each (no cap)" },
            ["nd.rspeed"] = new[] { "반복 강화 — 1회마다 초당 공격 +{0:0.##}회", "繰り返し強化 — 1回ごとに毎秒攻撃 +{0:0.##}回", "Repeatable — +{0:0.##} attacks/s each" },
            ["nd.rrange"] = new[] { "반복 강화 — 1회마다 커서 범위 +{0:0.##}", "繰り返し強化 — 1回ごとにカーソル範囲 +{0:0.##}", "Repeatable — cursor range +{0:0.##} each" },
            ["nd.rcritx"] = new[] { "반복 강화 — 1회마다 치명타 배수 +{0:0.##}", "繰り返し強化 — 1回ごとにクリ倍率 +{0:0.##}", "Repeatable — crit dmg +{0:0.##} each" },
            ["nd.rgold"]  = new[] { "반복 강화 — 1회마다 처치 골드 +{0:0.##}%", "繰り返し強化 — 1回ごとに撃破ゴールド +{0:0.##}%", "Repeatable — kill gold +{0:0.##}% each" },
            ["nd.bomb"]     = new[] { "백신 캡슐이 나타나기 시작 — 터뜨리면 주변 적에게 광역 피해",
                                      "ワクチンカプセルが出現し始める — 割ると周囲の敵に範囲ダメージ",
                                      "Vaccine capsules start appearing — pop one to blast nearby enemies" },
            ["nd.bombdmg"]  = new[] { "폭발 피해 +{0:0.##}배 (보스 최대체력 비율도 상승)", "爆発ダメージ +{0:0.##}倍（ボス最大HP割合も上昇）", "Blast damage +{0:0.##}x (boss max-HP share too)" },
            ["nd.bombrad"]  = new[] { "폭발 범위 +{0:0.##}배", "爆発範囲 +{0:0.##}倍", "Blast radius +{0:0.##}x" },
            ["nd.bombfreq"] = new[] { "캡슐 출현 간격 -{0:0.##}초", "カプセル出現間隔 -{0:0.##}秒", "Capsule interval -{0:0.##}s" },

            // ---- 메타 노드 ----
            ["m.dmg"]    = new[] { "각성 · 공격", "覚醒 · 攻撃", "Awaken · Damage" },
            ["md.dmg"]   = new[] { "영구 공격력 배수 +8%p / 레벨", "永久 攻撃倍率 +8%p / Lv", "Perm. damage mult +8%p / lv" },
            ["m.spd"]    = new[] { "각성 · 속도", "覚醒 · 速度", "Awaken · Speed" },
            ["md.spd"]   = new[] { "영구 타격 간격 ×0.94 / 레벨", "永久 攻撃間隔 ×0.94 / Lv", "Perm. atk interval ×0.94 / lv" },
            ["m.gold"]   = new[] { "각성 · 재화", "覚醒 · 資源", "Awaken · Gold" },
            ["md.gold"]  = new[] { "영구 골드 획득 +15% / 레벨", "永久 ゴールド獲得 +15% / Lv", "Perm. gold gain +15% / lv" },
            ["m.range"]  = new[] { "각성 · 범위", "覚醒 · 範囲", "Awaken · Range" },
            ["md.range"] = new[] { "영구 커서 범위 +0.08 / 레벨", "永久 カーソル範囲 +0.08 / Lv", "Perm. cursor range +0.08 / lv" },
            ["m.crit"]   = new[] { "각성 · 치명", "覚醒 · クリ", "Awaken · Crit" },
            ["md.crit"]  = new[] { "영구 치명 확률 +3%p / 레벨", "永久 クリ率 +3%p / Lv", "Perm. crit rate +3%p / lv" },
            ["m.start"]  = new[] { "미래 지식 · 자금", "未来の知識 · 資金", "Foresight · Funds" },
            ["md.start"] = new[] { "런 시작 골드 +2,000 / 레벨", "開始ゴールド +2,000 / Lv", "Starting gold +2,000 / lv" },
            ["m.auto"]   = new[] { "미래 지식 · 자동", "未来の知識 · オート", "Foresight · Auto" },
            ["md.auto"]  = new[] { "과거로 돌아간 뒤 자동 공격 즉시 해금", "過去へ戻った後オート攻撃を即解放", "Auto-attack from the start after rewinding" },
            ["m.critx"]  = new[] { "변이 · 치명 배율", "変異 · クリ倍率", "Mutation · Crit Dmg" },
            ["md.critx"] = new[] { "영구 치명 배수 +0.15 / 레벨", "永久 クリ倍率 +0.15 / Lv", "Perm. crit dmg mult +0.15 / lv" },
            ["m.time"]   = new[] { "변이 · 여유", "変異 · 猶予", "Mutation · Time" },
            ["md.time"]  = new[] { "영구 시작 제한시간 +3초 / 레벨", "永久 開始制限時間 +3秒 / Lv", "Perm. start time +3s / lv" },

            // ---- 스테이지 이름 ----
            ["s.0"] = new[] { "감염 구역 · 실험실", "感染区域 · 研究所", "Infected Zone · Lab" },
            ["s.1"] = new[] { "정착지 외곽", "居住地 外縁", "Settlement Outskirts" },
            ["s.2"] = new[] { "붕괴한 도심", "崩壊した都心", "Ruined Downtown" },
            ["s.3"] = new[] { "지하 수로망", "地下 水路網", "Underground Waterways" },
            ["s.4"] = new[] { "격리 항만", "隔離 港湾", "Quarantine Harbor" },
            ["s.5"] = new[] { "오염 산림", "汚染 森林", "Blighted Forest" },
            ["s.6"] = new[] { "설원 연구기지", "雪原 研究基地", "Snowfield Base" },
            ["s.7"] = new[] { "근원 · 최심부", "根源 · 最深部", "The Source · Depths" },
        };
    }
}
