using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlackholeGame
{
    // ============================================================
    // POP Cell — 코어 루프 + 눈결정 스킬트리 + 세포/바이러스 비주얼.
    //   커서 호버 자동타격 → 처치 시 $ → 쿼터 달성 시 다음 웨이브
    //   / 시간초과 or 일시정지에서 "웨이브 종료" → 엔드 화면 → 업그레이드 / 계속.
    //   타이머는 "연속" 구조. 스탯은 버킷 구조. 구매 노드는 한 게임 안에서 유지.
    // ============================================================
    public class GameManager : MonoBehaviour
    {
        // 밸런스는 GameConfig.cs / UpgradeTree.cs 에서 조절. (Awake에서 항상 코드 기본값으로 리셋 —
        // 씬 컴포넌트에 옛 값이 직렬화돼 남는 문제를 원천 차단)
        [HideInInspector] public Stats stats = new Stats();
        [HideInInspector] public WaveConfig wave = new WaveConfig();

        [Tooltip("플레이 필드 크기(월드 단위), 원점 중심")]
        public Vector2 fieldSize = new Vector2(26f, 16f);

        [Header("UI")]
        [Tooltip("HUD·텍스트 전체 배율 — Awake에서 항상 코드 기본값으로 리셋(씬 직렬화 무시)")]
        [HideInInspector] public float uiScale = 1.5f;

        enum State { Title, Map, Playing, Paused, Result, Tree, Settings, Meta, Win }
        State state = State.Title;
        State settingsReturn = State.Title;
        State pauseReturn = State.Playing;   // 일시정지에서 "계속/돌아가기" 시 복귀할 화면

        long gold;   // round44: 지역별 골드가 체력만큼 커져서(8지역 킬당 ~1만) int(21억)로는 넘칠 수 있다
        int waveNum = 1, kills, quota;
        int cycleKills; long cycleGold;
        int currentStage = 0;     // 지금 플레이 중인 지역 (0~7)
        int stagesCleared = 0;    // 클리어한 지역 수 = 해금된 tier
        int retryCount = 0;       // 부대 카운터(리트라이 횟수) — 표시용
        int maxScore = 0;         // 프레스티지 보상 기준 (stagesCleared*100 + 최고 웨이브)
        int bossSeenMask = 0;     // 보스 웨이브까지 가본 지역 비트마스크 → 지도에서 그림자→실제 보스
        bool hasSave = false;     // 타이틀 '이어하기' 표시용
        string cheatMsg; float cheatMsgUntil;   // 치트 적용 안내(화면 위 잠깐)
        float newConfirmUntil = -1f;   // 세이브가 있을 때 '새로 시작'은 두 번 눌러야 — 첫 클릭 후 이 시각까지 확인 대기
        int mapIndex = 0;         // 지역 캐러셀에서 고른 칸
        float mapScroll = -1f;    // 캐러셀 부드러운 이동 (음수 = 초기화 전)
        int metaCurrency = 0;     // 환생 재화 (shard)
        int bestScore = 0;        // 역대 최고 score
        bool everRebirth = false; // 환생이 한 번이라도 해금된 적 있으면 true(환생해도 안 꺼짐) — 메타/환생 버튼 노출용
        bool resultCleared = false; // Result 화면이 "지역 클리어"로 온 건지("감염원 제거") "시간초과"로 온 건지
        // 트리 화면에 "재도전"을 띄울지 — 전투가 끝나고(Result) 들어왔을 때만. 지도에서 그냥 구경하러
        // 들어온 경우엔 재도전할 "직전 전투"가 없으므로 숨긴다.
        bool treeFromResult = false;
        bool langOpen = false;      // 타이틀 우측 상단 언어 드롭다운이 펼쳐져 있는지
        int ascensionLevel = 0;    // 다음 판에 적용될 승천 난이도 — Win 화면 화살표로 플레이어가 직접 고름
        int maxAscensionUnlocked = 0;  // 지금까지 클리어로 열어본 최고 승천치 — 화살표 선택 상한(영구, 새로시작에만 리셋)
        int lastAscendShardGain = 0;   // Win 화면에 표시할, 방금 승천으로 받은 shard 양
        int lastAscendLevelPlayed = 0; // Win 화면에 표시할, 방금 클리어한 난이도(ascensionLevel 은 다음 판 기본값으로 이미 바뀜)
        bool lastAscendWasFirst = false;   // 방금이 생애 첫 변이인지 — "감염원이 변이하기 시작합니다" 배너용
        int campaignKills = 0; long campaignGold = 0;   // 이번 승천 사이클(마지막 리셋 이후) 누적 — Win 화면 표시용
        float campaignElapsedSec = 0f;
        int winKills = 0; long winGold = 0; float winTimeSec = 0f;   // Win 화면에 고정 표시할 스냅샷(리셋 전에 떠둠)
        readonly Dictionary<string, int> metaLv = new Dictionary<string, int>();
        List<UpgradeTree.MetaNode> metaNodes;
        Stats pristineStats;      // 메타 적용 전의 순정 기본값
        float timeLeft, attackTimer, spawnTimer, spawnInterval;

        Stats baseStats;
        HashSet<string> bought = new HashSet<string>();
        // round44: 반복 강화 레벨. 한 번이라도 산 반복 노드는 bought 에도 들어간다(연결선·공개 판정용).
        readonly Dictionary<string, int> repLv = new Dictionary<string, int>();
        float treeMaxPan;           // 트리 세로 스크롤 최대치(맨 위) — 하단 '맨 위로' 버튼이 쓴다
        bool treeBarDrag;           // 스크롤바 핸들을 잡고 있는 중
        float treeBarGrabOff;       // 핸들 안에서 잡은 지점(위에서부터) — 드래그가 튀지 않게
        List<UpgradeNode> nodes;
        readonly Dictionary<string, Vector2> npos = new Dictionary<string, Vector2>();

        float treeZoom = 0f;
        Vector2 treePan;

        class Enemy
        {
            public GameObject go;
            public Transform tr;
            public SpriteRenderer sr;
            public float hp, hpMax, r, flash;
            public Vector2 vel;
            public Color baseColor;
            public float wobSpeed, wobPhase;
            public float rotSpeed;     // 독립 회전(도/초) — 이동 방향과 무관하게 계속 돈다. 불규칙해 보이게
            public float visScale = 1f; // 종(species)마다 그림이 텍스처를 채우는 정도가 달라 타격판정(r)과
                                        // 눈에 보이는 크기를 맞추기 위한 배율 — BuildCellSprites 에서 실측
            public int spawnWave;   // 이 적이 소환된 웨이브 (골드·체력 기준)
            // ---- 보스 전용 패턴(승천 레벨별로 하나씩 켜짐, round35) ----
            public bool elite;         // 변이개체 — 체력 x3, 골드 x5, 빨간 테두리
            public bool bomb;          // 백신 캡슐 — 터뜨리면 광역 피해
            public bool dead;          // 이번 타격 처리 중 죽음 — 펄스 끝에 목록에서 한꺼번에 뺀다(폭발 연쇄 대비)
            public bool critResist;    // 승천1+: 치명타 "추가" 피해를 절반만 받음
            public bool shielded;      // 지금 무적 페이즈인지
            public float shieldTimer;  // 다음 무적 페이즈까지 남은 시간(또는 무적 페이즈 남은 시간)
        }
        readonly List<Enemy> enemies = new List<Enemy>();
        Enemy boss;          // 30웨이브 보스 (없으면 null)
        float bossTeleportTimer;                  // 이 값이 0 이하가 되면 보스가 맵 어딘가로 순간이동
        const float BossTeleportInterval = 4.5f;   // 후반에 커서가 너무 커져서 그냥 쫓아가기만 하면 잡히는 문제 완화
        const float ShieldPhaseInterval = 7f;      // 승천3+: 이 간격마다 잠깐 무적
        const float ShieldPhaseDuration = 1.2f;
        const float BossWaveSpawnSlow = 2.0f;      // 보스 웨이브 잡몹 소환 간격 배율(느리게) — 보스에 집중할 여지

        // ---- 변이개체 / 백신 캡슐 (round39) ----
        const float EliteChance   = 0.07f;   // 잡몹이 변이개체로 나올 확률
        const float EliteHpMul    = 3f;      // 체력 배율
        const int   EliteGoldMul  = 5;       // 골드 배율
        // 폭탄은 "잡몹 소환의 일정 비율"이 아니라 자체 타이머로 뜬다. 비율로 하면 소환 노드를 찍을수록
        // 빈도가 같이 폭증해서(소환수 2→6, 간격 하한 0.22) 8지역에선 폭탄만으로 보스 체력 71% 가 날아간다.
        // round42: 간격·대미지·반경·보스 비율은 전부 Stats(bomb*)로 — 백신 노드가 올린다.
        //   반경은 커서와 무관한 고정값 × bombRadiusMul — 커서 비례로 두면 후반엔 강화 없이도 화면을
        //   다 덮어서 반경 노드가 의미 없어진다. 2.6 → 최대 5.2 (전장 16×10 의 약 절반).
        const float BombBaseRadius = 2.6f;
        const int   BombMaxAlive   = 2;       // 안 터뜨리고 두면 쌓이는 것 방지
        float bombTimer;
        int bossesKilled;    // 무한 모드 보스 체력 스케일

        class Floater { public Vector3 world; public float life; public string text; public bool crit; }
        readonly List<Floater> floaters = new List<Floater>();

        class GoldFloat { public Vector3 startWorld; public float age; public float life; public int amount; }
        readonly List<GoldFloat> goldFloats = new List<GoldFloat>();

        class Burst { public Transform tr; public SpriteRenderer sr; public float life, maxLife, r0; public Color col; }
        readonly List<Burst> bursts = new List<Burst>();

        // 터진 세포가 배양접시에 남기는 자국 — 커서가 훑고 간 자리가 그대로 보인다
        class Splat { public Transform tr; public SpriteRenderer sr; public float life, maxLife; public Color col; }
        readonly List<Splat> splats = new List<Splat>();
        const int MaxSplats = 260;   // 후반엔 배양접시 바닥이 세포 잔여물로 뒤덮인다

        readonly List<Transform> specks = new List<Transform>();
        readonly List<Vector2> speckVel = new List<Vector2>();
        readonly List<SpriteRenderer> speckSr = new List<SpriteRenderer>();  // 감염 단계별 재틴트용
        readonly List<float> speckAlpha = new List<float>();                 // 포자 개체별 농도 편차

        // 감염지 배경 렌더러 — 변이 단계가 바뀌면 ApplyFieldLook() 이 색만 갈아끼운다
        SpriteRenderer groundSr, gridSr, infectSr, vigSr;

        GameAudio sound;     // 절차 생성 효과음 + BGM (Audio.cs)
        bool audioDirty;             // 설정에서 볼륨을 건드렸으면 나갈 때 PlayerPrefs 저장
        float manualClickCd;        // 수동 공격 쿨다운 — 오토마우스/연타로 이득 못 보게
        float lastSfxPreview;        // 효과음 슬라이더 미리듣기 쿨다운

        Camera cam;
        Sprite discSprite, splatSprite;
        Texture2D nodeRingTex;   // 트리 노드용 네온 글로우 링(round40)
        Sprite[] cellSprites;   // 지역(0~7)별로 다른 잡몹 실루엣 — round39
        float[] cellVisualBoost; // 종별 시각 배율(1.0 이상) — 타격판정보다 그림이 작아 보이는 문제 보정
        float[] bossVisualBoost; // 보스도 같은 문제(종마다 실제로 차지하는 면적이 다름) — 지역별 배율
        Sprite capsuleSprite;   // 백신 캡슐(알약) — 터뜨리면 광역 피해
        Sprite[] bossSprites;   // 지역별 보스 실루엣 (스테이지 오를수록 촉수↑, 마지막 = 살점 덩어리)
        Texture2D gridTile;
        Dictionary<string, Texture2D> iconTex;
        Material spriteMat;
        Transform cursorRing;
        SpriteRenderer cursorSr;
        SpriteRenderer cursorBorderSr;
        Sprite ringSprite;
        float lastRingRadius = -1f;   // 이 값이 바뀌면 테두리 두께를 다시 계산해서 링 스프라이트를 새로 만든다
        Vector3 cursorWorld;

        GUIStyle sLabel, sGold, sCenter, sBig, sSmall, sBtn;
        float lastUiScale = -1f;
        Font uiFont;   // 번들된 한글 폰트 (Assets/Resources/PopCellKR.ttf) — 웹 빌드엔 OS 폰트 폴백이 없어서 필수

        // 현미경 유리 팔레트 — 타이틀 화면과 게임 배경이 공유한다
        static readonly Color Glass     = new Color(0.74f, 0.86f, 0.91f); // 뿌연 하늘색(유리)
        static readonly Color GlassGrid = new Color(0.60f, 0.74f, 0.82f); // 그 위 격자(계수판)
        static readonly Color InkDark   = new Color(0.09f, 0.11f, 0.13f); // 밝은 배경용 진한 텍스트
        // (HudGold 제거 — 전장이 어두운 감염지로 바뀌면서 HUD/골드 텍스트가 전부 밝은 Gold 로 통일됨, round35)

        static readonly Color Ink  = new Color(0.94f, 0.94f, 0.95f); // 기본 텍스트(흰색) — 회색 바탕용
        static readonly Color Gold = new Color(1f, 0.82f, 0.30f);    // 달러 관련 텍스트(노랑)

        // ---- 감염지(round35) — 전장 배경은 어두운 감염 조직. 변이 단계가 올라갈수록 감염이 번진다 ----
        //   메뉴(타이틀/지도/트리)는 지금까지의 현미경 유리 톤 그대로 두고, 실제로 싸우는 필드만 바꾼다.
        //   한 단계 = 한 눈금씩 초록빛 초기 감염 -> 붉게 곪은 말기로 이동.
        struct FieldLook
        {
            public Color ground;    // 조직 바닥 틴트
            public Color grid;      // 관측 격자
            public Color infect;    // 감염막(핏줄·반점) 틴트 — 알파 포함
            public Color vignette;  // 경통 어둠 — 알파 포함
            public Color speck;     // 떠다니는 포자 — 알파 포함
        }

        const int InfectFullLv = 4;   // 이 단계에서 감염 연출이 최대치

        static FieldLook LookFor(int lvl)
        {
            float k = Mathf.Clamp01(lvl / (float)InfectFullLv);
            var l = new FieldLook();
            // 바닥은 "감염 전 조직" — 차갑고 어두운 무채색에 가깝게 둔다.
            //   세포(특히 1지역의 초록)가 바닥과 같은 색이면 묻혀버리므로, 색기운은 감염막이 담당한다.
            l.ground   = Color.Lerp(new Color(0.115f, 0.140f, 0.145f), new Color(0.165f, 0.085f, 0.095f), k);
            l.grid     = Color.Lerp(new Color(0.40f, 0.62f, 0.56f, 0.22f), new Color(0.62f, 0.30f, 0.30f, 0.20f), k);
            // 감염막은 실핏줄/반점에만 얹히는 마스크 — 단계가 오를수록 초기감염에서 검붉은 말기로.
            //   초반 색을 세포(1지역이 초록)와 같은 초록으로 두면 세포가 핏줄에 묻힌다 — 청록 쪽으로 비켜둔다.
            l.infect   = Color.Lerp(new Color(0.16f, 0.74f, 0.68f, 0.24f), new Color(0.92f, 0.13f, 0.26f, 0.55f), k);
            l.vignette = Color.Lerp(new Color(0.01f, 0.04f, 0.04f, 0.70f), new Color(0.09f, 0.00f, 0.02f, 0.86f), k);
            l.speck    = Color.Lerp(new Color(0.62f, 0.96f, 0.76f, 0.30f), new Color(1.00f, 0.46f, 0.38f, 0.46f), k);
            return l;
        }

        Rect FieldRect => new Rect(-fieldSize.x * 0.5f, -fieldSize.y * 0.5f, fieldSize.x, fieldSize.y);

        // 밝은 회색 배경에서도 확실히 보이도록 깊고 채도 높은 색으로
        static readonly Color[] Palette =
        {
            new Color(0.18f,0.60f,0.30f), new Color(0.10f,0.54f,0.48f), new Color(0.42f,0.60f,0.12f),
            new Color(0.62f,0.52f,0.10f), new Color(0.14f,0.44f,0.66f), new Color(0.46f,0.28f,0.70f),
            new Color(0.72f,0.20f,0.42f), new Color(0.16f,0.58f,0.42f), new Color(0.68f,0.34f,0.14f),
            new Color(0.22f,0.42f,0.74f), new Color(0.38f,0.58f,0.18f), new Color(0.70f,0.16f,0.30f),
            new Color(0.12f,0.56f,0.36f), new Color(0.52f,0.54f,0.14f), new Color(0.28f,0.38f,0.74f)
        };
        Color WaveColor(int w) => Palette[Mathf.Max(0, w - 1) % Palette.Length];

        void Awake()
        {
            Loc.LoadPref();
            stats = new Stats();       // 직렬화된 옛 값 무시, 항상 코드 기본값
            wave = new WaveConfig();
            fieldSize = new Vector2(26f, 16f);   // 보스 텔레포트가 의미있게 넓혀둠(기존 16×10) — 커서 상대 크기 체감도 줄어듦
            uiScale = 1.5f;            // 씬에 2가 직렬화돼 UI가 화면 밖으로 넘치던 문제 차단

            cam = Camera.main;
            if (cam == null) { Debug.LogError("[POP Cell] Main Camera 없음"); enabled = false; return; }

            cam.orthographic = true;
            cam.orthographicSize = fieldSize.y * 0.5f + 0.5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Glass;   // 현미경 유리 너머의 뿌연 하늘색

            spriteMat = new Material(FindSpriteShader());
            cellSprites = BuildCellSprites();
            discSprite = BuildDiscSprite();
            splatSprite = BuildSplatSprite();
            gridTile = BuildGridTile();
            iconTex = NodeIcons.Build();
            bossSprites = BuildBossSprites();

            BuildBackground();
            BuildCursor();

            nodes = UpgradeTree.BuildAll();
            metaNodes = UpgradeTree.BuildMeta();
            pristineStats = stats.Clone();
            baseStats = stats.Clone();
            SaveSystem.DropLegacy();   // round42: 옛 트리(v1) 세이브는 새 트리와 안 맞아 버린다
            LoadProgress();
            RebuildBaseStats();

            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            sound = new GameAudio(transform);

            state = State.Title; // 타이틀에서 시작
        }

        // ---- 절차적 스프라이트 ----

        Sprite[] BuildCellSprites()
        {
            var arr = new Sprite[StageConfig.Stages.Length];
            cellVisualBoost = new float[arr.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = BuildCellSprite(i, arr.Length);
                cellVisualBoost[i] = MeasureVisualBoost(arr[i].texture);
            }
            return arr;
        }

        // 텍스처 중심에서 "알파 0.5 이상"인 가장 먼 픽셀까지의 거리를 재서, 예전 원형 세포가 꽉 채우던
        // 기준(~44.6px, half=48 텍스처에서)과 비교한 배율을 낸다. 종마다 실루엣이 차지하는 면적이
        // 다르므로(박테리아는 좁게, 바이러스는 넓게) — 타격판정(e.r)은 그대로 두고 "보이는 크기"만
        // 이 배율만큼 키워서 눈에 보이는 것과 실제 맞는 범위를 맞춘다.
        static float MeasureVisualBoost(Texture2D tex) => MeasureVisualBoost(tex, 44.6f);  // 잡몹 기본값(96px 텍스처 기준)

        // 텍스처 중심에서 "알파 0.5 이상"인 가장 먼 픽셀까지의 거리를 재서, 목표 반경(refRadius)과
        // 비교한 배율을 낸다 — 종마다 실루엣이 차지하는 면적이 달라 그림만 보정하고 타격판정은 그대로 둘 때 쓴다.
        static float MeasureVisualBoost(Texture2D tex, float refRadius)
        {
            int size = tex.width;
            float half = size * 0.5f;
            var px = tex.GetPixels32();
            float maxD = 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (px[y * size + x].a < 128) continue;
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > maxD) maxD = d;
                }
            return Mathf.Clamp(refRadius / maxD, 1.0f, 2.4f);
        }

        // 잡몹 실루엣 — 지역(tier)이 오를수록 둥근 원에서 울퉁불퉁한 다엽형으로 "돌연변이"해간다.
        // BuildBossSprite 와 같은 극좌표 가장자리 요동 방식이지만, 작은 크기에서도 읽히도록
        // 촉수 대신 "혹(lobe) 개수·진폭"만 키운다 — 후반에도 실루엣이 뭉개지지 않는다.
        // 곡선을 따라 StampBlob 을 여러 번 찍어 두꺼운 선(촉수/꼬리/고리)을 만든다.
        //   path(u), thickness(u) : u=0..1 구간의 위치/두께 함수. BuildBossSprite 의 촉수 루프를 일반화한 것.
        static void StampCurve(float[] a, int size, System.Func<float, Vector2> path, System.Func<float, float> thickness, int steps = 36)
        {
            for (int k = 0; k < steps; k++)
            {
                float u = k / (float)(steps - 1);
                Vector2 p = path(u);
                StampBlob(a, size, p.x, p.y, thickness(u));
            }
        }

        // StampBlob 의 반대 — 원 안쪽 알파를 파낸다(눈구멍 등에 사용).
        static void StampErase(float[] a, int size, float cx, float cy, float r)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(cy + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > r) continue;
                    float v = Mathf.SmoothStep(1f, 0f, d / r);
                    int idx = y * size + x;
                    a[idx] *= (1f - v);
                }
        }

        // 지역(0~7)마다 "완전히 다른 병원체 종" 실루엣 — 전염병주식회사 스타일 참고(round39).
        //   예전엔 원 하나가 점점 삐죽해지는 단일 변형이라 후반이 오히려 꽃처럼 예뻐 보였다.
        //   이제 종 자체가 바뀐다: 박테리아(순한 덩어리) -> 바이러스(둥근 돌기) -> 곰팡이(비대칭 군집) ->
        //   기생충(꿈틀대는 몸) -> 프리온(엉킨 리본) -> 나노바이러스(각진 가시) -> 뇌기생충(머리+꼬리) ->
        //   네크로아(해골+가시관). 알파 버퍼에 합성 스탬프를 찍은 뒤 평평한 흰색 실루엣으로 굽는다.
        Sprite BuildCellSprite(int tier, int tierCount)
        {
            const int size = 96;
            float half = size * 0.5f;
            var c = new Vector2(half, half);
            var a = new float[size * size];
            var rnd = new System.Random(tier * 7841 + 13);

            switch (tier)
            {
                case 0: // 박테리아 — 둥근 덩어리 3개가 겹친 순한 군집
                    StampBlob(a, size, half - 11f, half - 6f, 17f);
                    StampBlob(a, size, half + 12f, half - 3f, 15f);
                    StampBlob(a, size, half + 1f, half + 14f, 14f);
                    break;

                case 1: // 바이러스 — 둥근 몸통 + 끝이 동그란 돌기(코로나 실루엣)
                {
                    StampBlob(a, size, c.x, c.y, 21f);
                    int spikes = 10;
                    for (int i = 0; i < spikes; i++)
                    {
                        float ang = i / (float)spikes * Mathf.PI * 2f;
                        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        var baseP = c + dir * 20f;
                        var tip = c + dir * 33f;
                        StampCurve(a, size, u => Vector2.Lerp(baseP, tip, u), u => Mathf.Lerp(4f, 2.2f, u), 16);
                        StampBlob(a, size, tip.x, tip.y, 5.4f);
                    }
                    break;
                }

                case 2: // 곰팡이균 — 중심 덩어리 + 비대칭으로 흩어진 포자 뭉치
                {
                    StampBlob(a, size, c.x, c.y, 19f);
                    int bumps = 7;
                    for (int i = 0; i < bumps; i++)
                    {
                        float ang = (i / (float)bumps) * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.7f;
                        float dist = 13f + (float)rnd.NextDouble() * 9f;
                        float r = 8f + (float)rnd.NextDouble() * 6f;
                        StampBlob(a, size, c.x + Mathf.Cos(ang) * dist, c.y + Mathf.Sin(ang) * dist, r);
                    }
                    break;
                }

                case 3: // 기생충 — 두껍게 꿈틀대는 S자 몸
                {
                    var startP = c + new Vector2(-30f, -13f);
                    var endP = c + new Vector2(30f, 15f);
                    StampCurve(a, size,
                        u => Vector2.Lerp(startP, endP, u) + new Vector2(0f, Mathf.Sin(u * Mathf.PI * 2.1f) * 13f),
                        u => 3.6f + 7f * Mathf.Sin(u * Mathf.PI), 72);
                    break;
                }

                case 4: // 프리온 — 중심 덩어리를 휘감은 엉킨 리본 두 가닥
                {
                    StampBlob(a, size, c.x, c.y, 13f);
                    for (int li = 0; li < 2; li++)
                    {
                        float loopR = 19f + li * 9f;
                        float rot = li * 2.0f;
                        StampCurve(a, size,
                            u => c + new Vector2(Mathf.Cos(u * Mathf.PI * 1.4f + rot), Mathf.Sin(u * Mathf.PI * 1.4f + rot) * 0.72f) * loopR,
                            u => 3f + 2f * Mathf.Sin(u * 9f + li), 150);
                    }
                    break;
                }

                case 5: // 나노바이러스 — 작은 코어 + 각진 침(가늘고 뾰족, 기계적인 느낌)
                {
                    StampBlob(a, size, c.x, c.y, 13f);
                    int spikes = 8;
                    for (int i = 0; i < spikes; i++)
                    {
                        float ang = i / (float)spikes * Mathf.PI * 2f + 0.28f;
                        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        StampCurve(a, size, u => c + dir * Mathf.Lerp(11f, 39f, u), u => Mathf.Lerp(5f, 0.6f, u), 24);
                    }
                    break;
                }

                case 6: // 뇌신경 기생충 — 둥근 머리 + 길게 휘어지는 꼬리
                {
                    var head = c + new Vector2(-7f, -11f);
                    StampBlob(a, size, head.x, head.y, 16f);
                    StampCurve(a, size,
                        u => head + new Vector2(u * 38f, u * u * 28f + Mathf.Sin(u * 6f) * 7f),
                        u => Mathf.Lerp(8.5f, 1.8f, u), 40);
                    break;
                }

                default: // 네크로아 바이러스(최종) — 해골(눈구멍) + 위쪽 절반에 가시관
                {
                    var skullC = new Vector2(c.x, c.y - 5f);
                    StampBlob(a, size, skullC.x, skullC.y, 21f);          // 두개골
                    StampBlob(a, size, c.x, c.y + 15f, 12f);              // 턱
                    StampErase(a, size, skullC.x - 7.5f, skullC.y - 6f, 5.5f);  // 눈구멍(좌)
                    StampErase(a, size, skullC.x + 7.5f, skullC.y - 6f, 5.5f);  // 눈구멍(우)
                    StampErase(a, size, skullC.x, skullC.y + 3f, 2.6f);        // 콧구멍
                    int spikes = 6;
                    for (int i = 0; i < spikes; i++)
                    {
                        float ang = Mathf.Lerp(Mathf.PI * 0.12f, Mathf.PI * 0.88f, i / (float)(spikes - 1));
                        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        var baseP = skullC + dir * 19f;
                        var tip = skullC + dir * 32f;
                        StampCurve(a, size, u => Vector2.Lerp(baseP, tip, u), u => Mathf.Lerp(3.6f, 0.8f, u), 14);
                    }
                    break;
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int i = 0; i < a.Length; i++)
                px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i]) * 255f));
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        // 백신 캡슐 — 세로로 선 알약 모양. 위/아래 반쪽 밝기를 달리해 "캡슐"로 읽히게 하고,
        // 가운데 이음새와 바깥 테두리를 진하게. cellSprite 와 같은 규약(흰 알파형 → 런타임에 색 틴트).
        Sprite BuildCapsuleSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float cx = size * 0.5f, cy = size * 0.5f;
            float halfW = size * 0.26f;          // 알약 반폭
            float halfH = size * 0.44f;          // 반높이 (세로로 길쭉)
            float rCap = halfW;                  // 위아래 둥근 끝 반지름
            float straight = halfH - rCap;       // 직선 구간 절반 길이
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    // 캡슐(스타디움) 거리장 — 직선 구간은 |dx|, 끝은 원 거리
                    float ay = Mathf.Abs(dy);
                    float d = (ay <= straight) ? Mathf.Abs(dx)
                                               : Mathf.Sqrt(dx * dx + (ay - straight) * (ay - straight));
                    float t = d / rCap;          // 0 = 중심축, 1 = 바깥선
                    float v, a;
                    if (t >= 1.02f) { v = 0f; a = 0f; }
                    else
                    {
                        bool seam = ay < size * 0.035f;                   // 가운데 이음새
                        if (t > 0.88f)      v = 0.10f;                    // 진한 테두리
                        else if (seam)      v = 0.22f;                    // 이음새 선
                        else if (dy > 0f)   v = 0.98f;                    // 위쪽 반 — 밝게
                        else                v = 0.62f;                    // 아래쪽 반 — 조금 어둡게
                        // 위쪽 반에 사선 하이라이트 한 줄
                        if (dy > 0f && t < 0.55f && Mathf.Abs(dx + (dy - straight) * 0.35f) < size * 0.035f) v = 1.0f;
                        a = t < 0.92f ? 1.0f : Mathf.Lerp(1.0f, 0f, (t - 0.92f) / 0.10f);
                    }
                    px[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(v) * 255f), (byte)(Mathf.Clamp01(v) * 255f),
                        (byte)(Mathf.Clamp01(v) * 255f), (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }


        // 세포가 터진 자국. 원이 아니라 각도별로 반지름을 흔들어 찌그러진 얼룩 + 튄 방울 몇 개.
        Sprite BuildSplatSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            var c = new Vector2(size * 0.5f, size * 0.5f);
            float half = size * 0.5f;

            // 튄 방울 (각도, 거리, 반지름)
            var rnd = new System.Random(4242);
            int SAT = 5;
            var satA = new float[SAT]; var satD = new float[SAT]; var satR = new float[SAT];
            for (int i = 0; i < SAT; i++)
            {
                satA[i] = (float)rnd.NextDouble() * 6.2832f;
                satD[i] = 0.60f + (float)rnd.NextDouble() * 0.26f;
                satR[i] = 0.045f + (float)rnd.NextDouble() * 0.065f;
            }

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - c.x) / half, dy = (y + 0.5f - c.y) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float th = Mathf.Atan2(dy, dx);

                    // 울퉁불퉁한 본체 경계
                    float edge = 0.55f + 0.13f * Mathf.Sin(3f * th + 1.1f)
                                       + 0.08f * Mathf.Sin(5f * th + 2.7f)
                                       + 0.05f * Mathf.Sin(7f * th + 0.4f);
                    float a = Mathf.Clamp01((edge - d) / 0.07f);

                    // 튄 방울들
                    for (int i = 0; i < SAT; i++)
                    {
                        float sx = Mathf.Cos(satA[i]) * satD[i], sy = Mathf.Sin(satA[i]) * satD[i];
                        float sd = Mathf.Sqrt((dx - sx) * (dx - sx) + (dy - sy) * (dy - sy));
                        a = Mathf.Max(a, Mathf.Clamp01((satR[i] - sd) / 0.035f));
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        // ---- 절차 생성 값 노이즈 (감염지 배경용) ----
        //   격자 난수 + 부드러운 보간, 옥타브를 겹쳐 유기적인 얼룩을 만든다. 격자를 wrap 해서 이음매 없음.
        static float[] NoiseLattice(int n, System.Random rnd)
        {
            var a = new float[n * n];
            for (int i = 0; i < a.Length; i++) a[i] = (float)rnd.NextDouble();
            return a;
        }

        static float NoiseAt(float[] lat, int n, float x, float y)
        {
            float fx = x * n, fy = y * n;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            int x1 = ((x0 + 1) % n + n) % n, y1 = ((y0 + 1) % n + n) % n;
            x0 = (x0 % n + n) % n; y0 = (y0 % n + n) % n;
            float sx = tx * tx * (3f - 2f * tx), sy = ty * ty * (3f - 2f * ty);   // smoothstep 보간
            float a = Mathf.Lerp(lat[y0 * n + x0], lat[y0 * n + x1], sx);
            float b = Mathf.Lerp(lat[y1 * n + x0], lat[y1 * n + x1], sx);
            return Mathf.Lerp(a, b, sy);
        }

        static float Fbm(float[][] lats, int[] ns, float x, float y)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < lats.Length; o++)
            {
                sum += NoiseAt(lats[o], ns[o], x, y) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        // 감염지 바닥 — 축축한 조직 덩어리. RGB 는 명암만 담고, 실제 색은 sr.color 틴트로 입힌다.
        Sprite BuildTissueSprite()
        {
            const int size = 512;
            var rnd = new System.Random(20260911);
            int[] ns = { 4, 8, 16, 32, 64 };   // 고주파 옥타브까지 — 매끈한 그라데이션이 아니라 조직 결이 보이게
            var lats = new float[ns.Length][];
            for (int i = 0; i < ns.Length; i++) lats[i] = NoiseLattice(ns[i], rnd);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float n = Fbm(lats, ns, u, v);
                    // 대비를 세게 — 중간값 주변을 밀어내서 덩어리진 조직처럼 보이게
                    float c2 = Mathf.Clamp01((n - 0.5f) * 1.9f + 0.5f);
                    // 0.30~1.0 — 완전히 검게 눌러버리면 세포가 떠 보이지 않으니 바닥을 남긴다
                    float lum = 0.30f + 0.70f * Mathf.SmoothStep(0f, 1f, c2);
                    byte b = (byte)(Mathf.Clamp01(lum) * 255f);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        // 감염막 — 조직 위로 뻗은 실핏줄과 번진 반점. 알파만 쓰고 색/농도는 변이 단계가 정한다.
        Sprite BuildInfectionSprite()
        {
            const int size = 512;
            var rnd = new System.Random(770411);
            int[] nv = { 4, 9, 18, 36 };   // 촘촘한 망 — 굵은 강줄기 하나가 아니라 실핏줄 그물처럼
            int[] nb = { 2, 5, 10 };
            var lv = new float[nv.Length][];
            for (int i = 0; i < nv.Length; i++) lv[i] = NoiseLattice(nv[i], rnd);
            var lb = new float[nb.Length][];
            for (int i = 0; i < nb.Length; i++) lb[i] = NoiseLattice(nb[i], rnd);

            // fBm 은 값이 0.5 근처에 강하게 몰린다 — 절대 임계값으로 자르면 화면 전체가 덮이거나
            //   아무것도 안 남는다(온통 초록 막이 됐던 원인). 그래서 두 노이즈장을 먼저 다 구해
            //   평균·표준편차로 표준화한 뒤, 그 위에서 "얼마나 덮을지"를 고른다. 노이즈 파라미터를
            //   바꿔도 덮는 비율이 흔들리지 않는다.
            int N = size * size;
            var nv1 = new float[N];
            var nb1 = new float[N];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    nv1[y * size + x] = Fbm(lv, nv, u, v);
                    nb1[y * size + x] = Fbm(lb, nb, u, v);
                }
            float mv = 0f, mb = 0f;
            for (int i = 0; i < N; i++) { mv += nv1[i]; mb += nb1[i]; }
            mv /= N; mb /= N;
            float sv = 0f, sb = 0f;
            for (int i = 0; i < N; i++)
            {
                float dv = nv1[i] - mv, db = nb1[i] - mb;
                sv += dv * dv; sb += db * db;
            }
            sv = Mathf.Sqrt(sv / N) + 1e-6f;
            sb = Mathf.Sqrt(sb / N) + 1e-6f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
            {
                // 실핏줄 = 표준화한 노이즈의 등고선(level set) 둘레 아주 좁은 띠
                float z = (nv1[i] - mv) / sv;
                float vein = Mathf.Exp(-z * z * 150f);   // 클수록 가늘어짐
                // 반점은 상위 꼬리에서만 — 바닥이 드러나 있어야 감염이 "번진" 것처럼 읽힌다.
                //   주의: Unity 의 Mathf.SmoothStep(from,to,t) 은 GLSL smoothstep 이 아니라 from~to
                //   "사이 값"을 돌려준다. 임계값으로 쓰려면 InverseLerp 로 t 를 만들어 넣어야 한다.
                float zb = (nb1[i] - mb) / sb;
                float blot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.4f, 2.4f, zb));
                float a = Mathf.Clamp01(vein * 0.95f + blot * 0.55f);
                px[i] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        // 현미경으로 들여다보는 느낌 — 가장자리로 갈수록 어두워진다
        Sprite BuildVignetteSprite()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 1.05f, d));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        Sprite BuildDiscSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float rad = size * 0.5f - 1f;
            var c = new Vector2(size * 0.5f, size * 0.5f);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a = Mathf.Clamp01(rad - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        // 트리 노드 뱃지 테두리 — 링 라인에서 안팎으로 부드럽게 번지는 네온 글로우.
        //   GUI.DrawTexture 로 그대로 색을 입혀 쓴다(Texture2D, Sprite 아님).
        Texture2D BuildNodeRingTex()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float half = size * 0.5f;
            float ringR = half - 7f;
            float glowW = 16f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Abs(d - ringR) / glowW;
                    float a = Mathf.Clamp01(1f - t);
                    a *= a;   // 부드러운 감쇠 — 중심선에 밝음이 몰리게
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        // 커서 범위 테두리 — 속은 비고 가장자리만 굵게. 안쪽 옅은 채움은 discSprite가 담당.
        Sprite BuildRingSprite(float borderFrac)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float half = size * 0.5f;
            float outer = half - 1f;
            float inner = outer * (1f - borderFrac);
            var c = new Vector2(half, half);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        // ---- 지역별 보스 실루엣 ----
        //   흰색(알파)으로 그려서 지도에서 검은 그림자로도, 색을 입혀 살아있는 보스로도 쓴다.
        //   스테이지가 오를수록 세포에 촉수가 많아지고, 마지막 지역은 여러 로브가 뭉친 살점 덩어리.
        Sprite[] BuildBossSprites()
        {
            int n = StageConfig.Stages.Length;
            var arr = new Sprite[n];
            bossVisualBoost = new float[n];
            for (int i = 0; i < n; i++)
            {
                arr[i] = BuildBossSprite(i, n);
                // 그 스테이지의 "의도된 최대 반경"(옛 촉수형 보스가 뻗던 len)에 맞춰 배율을 낸다 —
                // 종(species)마다 실제 잉크가 차지하는 면적이 달라서(박테리아처럼 중심에 몰린 종은
                // 작아 보이고, 바이러스처럼 끝까지 뻗는 종은 커 보임) 그림만 보정, 타격판정(bossR)은 그대로.
                bool finalBoss = i >= n - 1;
                float grow = n > 1 ? i / (float)(n - 1) : 0f;
                float half = arr[i].texture.width * 0.5f;
                float bodyR = half * BossBodyFrac(i, n);
                float lenTarget = bodyR + half * (finalBoss ? 0.24f : Mathf.Lerp(0.14f, 0.34f, grow));
                bossVisualBoost[i] = MeasureVisualBoost(arr[i].texture, lenTarget);
            }
            return arr;
        }

        static void StampBlob(float[] a, int size, float cx, float cy, float r)
        {
            if (r < 0.5f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(cy + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > r) continue;
                    float v = Mathf.SmoothStep(1f, 0f, d / r);
                    int idx = y * size + x;
                    if (v > a[idx]) a[idx] = v;
                }
        }

        // 그 지역 보스 스프라이트의 "몸통이 텍스처 절반(half) 대비 차지하는 비율"(0~1).
        //   BuildBossSprite 의 bodyR 계산과 반드시 같은 값 — SpawnBoss 가 이 비율로 타격판정 반경(Enemy.r)을
        //   잡아서, 스프라이트가 커 보이는 지역일수록(후반) 실제 맞는 범위도 같이 커지게 한다.
        static float BossBodyFrac(int stage, int stageCount)
        {
            bool finalBoss = stage >= stageCount - 1;
            float grow = stageCount > 1 ? stage / (float)(stageCount - 1) : 0f;
            return finalBoss ? 0.50f : Mathf.Lerp(0.36f, 0.46f, grow);
        }

        // 보스 = 그 지역 잡몹과 같은 "종"의 거대판. 잡몹 실루엣(BuildCellSprite)과 같은 8종 언어를
        // 그대로 쓰되, 개체수·촉수 길이를 키운다(round39). 코어 반경(bodyR)·촉수 최대 길이(len)는
        // 예전 촉수형 보스와 동일한 공식을 그대로 물려받아 — 타격판정(SpawnBoss 의 bossR)이 쓰는
        // BossBodyFrac 기준과 계속 맞는다(별도 보정 없이도 기존 밸런스가 유지됨).
        Sprite BuildBossSprite(int stage, int stageCount)
        {
            const int size = 192;
            float half = size * 0.5f;
            var a = new float[size * size];
            var rnd = new System.Random(stage * 9173 + 41);
            bool finalBoss = stage >= stageCount - 1;
            float grow = stageCount > 1 ? stage / (float)(stageCount - 1) : 0f;   // 0..1

            float bodyR = half * BossBodyFrac(stage, stageCount);
            int tent = finalBoss ? 12 : Mathf.RoundToInt(Mathf.Lerp(4f, 10f, grow));
            float len = bodyR + half * (finalBoss ? 0.24f
                                                  : Mathf.Lerp(0.14f, 0.34f, grow));
            var c = new Vector2(half, half);

            switch (Mathf.Clamp(stage, 0, 7))
            {
                case 0: // 박테리아 — 큰 덩어리 여럿이 겹친 군집 (len 근처까지 퍼지도록 넓게)
                {
                    int blobs = 7;
                    for (int i = 0; i < blobs; i++)
                    {
                        float ang = i / (float)blobs * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.4f;
                        float dist = Mathf.Lerp(bodyR * 0.35f, len * 0.78f, (float)rnd.NextDouble());
                        float r = bodyR * Mathf.Lerp(0.55f, 0.80f, (float)rnd.NextDouble());
                        StampBlob(a, size, c.x + Mathf.Cos(ang) * dist, c.y + Mathf.Sin(ang) * dist, r);
                    }
                    break;
                }

                case 1: // 바이러스 — 큰 몸통 + 둥근 돌기 다수
                {
                    StampBlob(a, size, c.x, c.y, bodyR);
                    for (int i = 0; i < tent; i++)
                    {
                        float ang = i / (float)tent * Mathf.PI * 2f;
                        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        var baseP = c + dir * bodyR * 0.92f;
                        var tip = c + dir * len;
                        StampCurve(a, size, u => Vector2.Lerp(baseP, tip, u), u => Mathf.Lerp(size * 0.052f, size * 0.024f, u),
                                   Mathf.Max(20, Mathf.RoundToInt((len - bodyR) / 2.2f)));
                        StampBlob(a, size, tip.x, tip.y, size * 0.045f);
                    }
                    break;
                }

                case 2: // 곰팡이균 — 중심 덩어리 + 여러 겹의 비대칭 포자 뭉치
                {
                    StampBlob(a, size, c.x, c.y, bodyR * 0.86f);
                    int bumps = tent + 6;
                    for (int i = 0; i < bumps; i++)
                    {
                        float ang = (i / (float)bumps) * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.6f;
                        float dist = bodyR * Mathf.Lerp(0.55f, 1.02f, (float)rnd.NextDouble());
                        float r = bodyR * Mathf.Lerp(0.24f, 0.40f, (float)rnd.NextDouble());
                        StampBlob(a, size, c.x + Mathf.Cos(ang) * dist, c.y + Mathf.Sin(ang) * dist, r);
                    }
                    break;
                }

                case 3: // 기생충 — 화면을 가로지르는 굵고 긴 꿈틀 몸
                {
                    var startP = c + new Vector2(-len * 0.94f, -bodyR * 0.5f);
                    var endP = c + new Vector2(len * 0.94f, bodyR * 0.6f);
                    StampCurve(a, size,
                        u => Vector2.Lerp(startP, endP, u) + new Vector2(0f, Mathf.Sin(u * Mathf.PI * 2.6f) * bodyR * 0.55f),
                        u => bodyR * (0.20f + 0.34f * Mathf.Sin(u * Mathf.PI)), 130);
                    break;
                }

                case 4: // 프리온 — 중심 덩어리를 휘감은 굵은 리본 여러 가닥
                {
                    StampBlob(a, size, c.x, c.y, bodyR * 0.56f);
                    int loops = 3;
                    for (int li = 0; li < loops; li++)
                    {
                        float loopR = Mathf.Lerp(bodyR * 0.62f, len, li / (float)(loops - 1));
                        float rot = li * 1.6f;
                        StampCurve(a, size,
                            u => c + new Vector2(Mathf.Cos(u * Mathf.PI * 1.4f + rot), Mathf.Sin(u * Mathf.PI * 1.4f + rot) * 0.72f) * loopR,
                            u => bodyR * (0.13f + 0.05f * Mathf.Sin(u * 9f + li)), 260);
                    }
                    break;
                }

                case 5: // 나노바이러스 — 코어 + 각진 침(길고 짧은 두 겹, 기계적)
                {
                    StampBlob(a, size, c.x, c.y, bodyR * 0.62f);
                    for (int i = 0; i < tent; i++)
                    {
                        float ang = i / (float)tent * Mathf.PI * 2f + 0.2f;
                        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        float thisLen = (i % 2 == 0) ? len : Mathf.Lerp(bodyR, len, 0.62f);
                        StampCurve(a, size, u => c + dir * Mathf.Lerp(bodyR * 0.5f, thisLen, u),
                                   u => Mathf.Lerp(size * 0.030f, size * 0.004f, u),
                                   Mathf.Max(18, Mathf.RoundToInt(thisLen / 2f)));
                    }
                    break;
                }

                case 6: // 뇌신경 기생충 — 크고 또렷한 머리 + 사방으로 넓게 펼쳐지는 꼬리 3갈래(히드라형)
                {
                    StampBlob(a, size, c.x, c.y, bodyR * 0.95f);
                    int tails = 3;
                    for (int ti = 0; ti < tails; ti++)
                    {
                        float baseAng = ti / (float)tails * Mathf.PI * 2f + 0.5f;   // 120°씩 고르게 분산
                        float curl = (float)(rnd.NextDouble() - 0.5) * 1.1f;
                        var dir0 = new Vector2(Mathf.Cos(baseAng), Mathf.Sin(baseAng));
                        StampCurve(a, size,
                            u => c + dir0 * (bodyR * 0.6f + (len - bodyR * 0.6f) * u)
                                    + new Vector2(-dir0.y, dir0.x) * Mathf.Sin(u * 3.2f) * bodyR * 0.22f * u
                                    + dir0 * curl * u * u * bodyR * 0.5f,
                            u => Mathf.Lerp(bodyR * 0.30f, size * 0.010f, u), 90);
                    }
                    break;
                }

                default: // 네크로아 바이러스(최종) — 큰 해골 + 사방을 두른 가시관
                {
                    StampBlob(a, size, c.x, c.y - bodyR * 0.14f, bodyR * 0.92f);   // 두개골
                    StampBlob(a, size, c.x, c.y + bodyR * 0.62f, bodyR * 0.56f);  // 턱
                    StampErase(a, size, c.x - bodyR * 0.34f, c.y - bodyR * 0.26f, bodyR * 0.22f);  // 눈구멍(좌)
                    StampErase(a, size, c.x + bodyR * 0.34f, c.y - bodyR * 0.26f, bodyR * 0.22f);  // 눈구멍(우)
                    StampErase(a, size, c.x, c.y + bodyR * 0.06f, bodyR * 0.10f);                   // 콧구멍
                    for (int i = 0; i < tent; i++)
                    {
                        float ang = i / (float)tent * Mathf.PI * 2f;
                        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        var baseP = c + dir * bodyR * 0.88f;
                        var tip = c + dir * len;
                        StampCurve(a, size, u => Vector2.Lerp(baseP, tip, u), u => Mathf.Lerp(size * 0.034f, size * 0.006f, u),
                                   Mathf.Max(16, Mathf.RoundToInt((len - bodyR) / 2f)));
                    }
                    break;
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int i = 0; i < a.Length; i++)
                px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i]) * 255f));
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        // 16×16 점선 그리드 타일 (IMGUI 타일링 + 월드 보드 공용)
        Texture2D BuildGridTile()
        {
            const int n = 40;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var clear = new Color32(0, 0, 0, 0);
            var dot = new Color32(255, 255, 255, 255);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool onEdge = (y == 0 && x % 4 == 0) || (x == 0 && y % 4 == 0);
                    px[y * n + x] = onEdge ? dot : clear;
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        void BuildBackground()
        {
            // 고정 크기 16×16 점선 그리드 텍스처 (화면 비율에 의존하지 않음 — 크래시 방지)
            const int G = 512, cell = 32;
            var gtex = new Texture2D(G, G, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var gpx = new Color32[G * G];
            var white = new Color32(255, 255, 255, 255);
            var none = new Color32(0, 0, 0, 0);
            for (int y = 0; y < G; y++)
                for (int x = 0; x < G; x++)
                {
                    bool line = ((x % cell == 0) && (y % 4 == 0)) || ((y % cell == 0) && (x % 4 == 0));
                    gpx[y * G + x] = line ? white : none;
                }
            gtex.SetPixels32(gpx); gtex.Apply();
            var gsprite = Sprite.Create(gtex, new Rect(0, 0, G, G), new Vector2(0.5f, 0.5f), G, 0, SpriteMeshType.FullRect);

            float aspect = cam.aspect;
            if (float.IsNaN(aspect) || aspect < 0.2f || aspect > 6f) aspect = 16f / 9f;
            float vh = cam.orthographicSize * 2f;
            float vw = vh * aspect;

            // 조직 바닥 — 가장 아래. 격자·감염막·세포가 전부 이 위에 얹힌다.
            var groundGo = new GameObject("InfectedGround");
            groundSr = groundGo.AddComponent<SpriteRenderer>();
            groundSr.sprite = BuildTissueSprite();
            groundSr.sharedMaterial = spriteMat;
            groundSr.sortingOrder = -70;
            groundGo.transform.position = new Vector3(0f, 0f, 1.2f);
            groundGo.transform.localScale = new Vector3(vw * 1.3f, vh * 1.3f, 1f);

            var board = new GameObject("GridBoard");
            gridSr = board.AddComponent<SpriteRenderer>();
            gridSr.sprite = gsprite;
            gridSr.sharedMaterial = spriteMat;
            gridSr.sortingOrder = -60;
            board.transform.position = new Vector3(0f, 0f, 1f);
            board.transform.localScale = new Vector3(vw * 1.25f, vh * 1.25f, 1f);

            // 감염막 — 조직 위, 잔해(-20)·세포(10) 아래
            var infGo = new GameObject("InfectionFilm");
            infectSr = infGo.AddComponent<SpriteRenderer>();
            infectSr.sprite = BuildInfectionSprite();
            infectSr.sharedMaterial = spriteMat;
            infectSr.sortingOrder = -50;
            infGo.transform.position = new Vector3(0f, 0f, 0.9f);
            infGo.transform.localScale = new Vector3(vw * 1.3f, vh * 1.3f, 1f);

            var fr = FieldRect;
            for (int i = 0; i < 24; i++)
            {
                var sp = new GameObject("Speck");
                var ssr = sp.AddComponent<SpriteRenderer>();
                ssr.sprite = discSprite;
                ssr.sharedMaterial = spriteMat;
                ssr.sortingOrder = -40;
                sp.transform.localScale = Vector3.one * Random.Range(0.04f, 0.10f);
                sp.transform.position = new Vector3(Random.Range(fr.xMin, fr.xMax), Random.Range(fr.yMin, fr.yMax), 0f);
                specks.Add(sp.transform);
                speckSr.Add(ssr);                      // 변이 단계마다 포자 색을 다시 입히려고 보관
                speckAlpha.Add(Random.Range(0.55f, 1f));  // 개체별 농도 편차 유지
                speckVel.Add(new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f)));
            }

            // 비네트는 세포(정렬 10)보다 아래에 둔다 — 가장자리 세포까지 어두워지면 안 보이므로
            var vig = new GameObject("Vignette");
            vigSr = vig.AddComponent<SpriteRenderer>();
            vigSr.sprite = BuildVignetteSprite();
            vigSr.sharedMaterial = spriteMat;
            vigSr.sortingOrder = -15;
            vig.transform.position = new Vector3(0f, 0f, 0.5f);
            vig.transform.localScale = new Vector3(vw * 1.2f, vh * 1.2f, 1f);

            ApplyFieldLook();
        }

        // 변이 단계에 맞춰 전장 색을 다시 입힌다 — 배경 오브젝트는 그대로 두고 틴트만 바꾼다.
        //   StartRun/Prestige 등 "판이 시작되는 순간"마다 호출하면 단계 변경이 바로 반영된다.
        void ApplyFieldLook()
        {
            var look = LookFor(ascensionLevel);
            if (cam != null) cam.backgroundColor = look.ground * 0.55f;   // 필드 밖 여백은 더 어둡게
            if (groundSr != null) groundSr.color = look.ground;
            if (gridSr != null) gridSr.color = look.grid;
            if (infectSr != null) infectSr.color = look.infect;
            if (vigSr != null) vigSr.color = look.vignette;
            for (int i = 0; i < speckSr.Count; i++)
            {
                if (speckSr[i] == null) continue;
                var c = look.speck;
                c.a *= (i < speckAlpha.Count ? speckAlpha[i] : 1f);
                speckSr[i].color = c;
            }
        }

        void SpawnSplat(Vector3 pos, Color col, float r)
        {
            if (splatSprite == null || spriteMat == null) return;
            if (splats.Count >= MaxSplats)   // 가장 오래된 것부터 재활용
            {
                var old = splats[0];
                if (old.tr) Destroy(old.tr.gameObject);
                splats.RemoveAt(0);
            }
            var go = new GameObject("Splat");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = splatSprite;
            sr.sharedMaterial = spriteMat;
            sr.sortingOrder = -20;           // 그리드(-60)/티끌(-40) 위, 비네트(-15)·세포(10) 아래
            go.transform.position = new Vector3(pos.x, pos.y, 0.2f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * (r * 2f * Random.Range(1.7f, 2.7f));
            // 물감처럼 덧칠되도록 — 알파는 조금씩 다르게(겹칠수록 불균일하게 짙어짐).
            //   round35: 바닥이 어두운 감염지로 바뀌어 예전처럼 어둡게 깔면 묻힌다 — 세포색보다 살짝 밝게.
            float a = Random.Range(0.44f, 0.62f);
            var c = new Color(col.r * 1.12f, col.g * 1.12f, col.b * 1.12f, a);
            sr.color = c;
            // 한 판 최대 길이(~85초)보다 길게 — 판 도중엔 안 사라지고 쌓이기만, StartRun에서 한꺼번에 지워짐
            splats.Add(new Splat { tr = go.transform, sr = sr, life = 200f, maxLife = 200f, col = c });
        }

        void UpdateSplats(float dt)
        {
            for (int i = splats.Count - 1; i >= 0; i--)
            {
                var s = splats[i];
                s.life -= dt;
                if (s.life <= 0f) { if (s.tr) Destroy(s.tr.gameObject); splats.RemoveAt(i); continue; }
                // 수명의 85%는 그대로 두고 마지막 15%에서만 페이드 (상점에 오래 머물 때의 폴백)
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s.life / (s.maxLife * 0.15f)));
                var c = s.col; c.a = s.col.a * k;
                s.sr.color = c;
            }
        }

        void BuildCursor()
        {
            if (ringSprite == null) ringSprite = BuildRingSprite(0.12f);   // UpdateCursor 가 첫 프레임에 실제 반경에 맞게 다시 만듦

            var go = new GameObject("CursorField");
            cursorRing = go.transform;
            cursorSr = go.AddComponent<SpriteRenderer>();
            cursorSr.sprite = discSprite;
            cursorSr.sharedMaterial = spriteMat;
            // 안쪽은 아주 옅게만 채운다 — 범위는 테두리로 읽는다
            cursorSr.color = new Color(1f, 1f, 1f, 0.10f);
            cursorSr.sortingOrder = 40;

            // 밝은 테두리 — 어두운 감염지(round35) 위에서 범위가 또렷하게. 검은 링은 바닥에 묻힌다.
            var bd = new GameObject("CursorBorder");
            bd.transform.SetParent(go.transform, false);
            cursorBorderSr = bd.AddComponent<SpriteRenderer>();
            cursorBorderSr.sprite = ringSprite;
            cursorBorderSr.sharedMaterial = spriteMat;
            cursorBorderSr.color = new Color(0.86f, 1f, 0.93f, 0.90f);
            cursorBorderSr.sortingOrder = 41;
        }

        // ---- 게임 시작/라이프사이클 ----

        // URP 2D에선 Sprites/Default가 2D 라이트와 얽혀 어둡게/투명하게 나오는 경우가 있어
        // URP 전용 언릿 스프라이트 셰이더를 우선 사용한다.
        static Shader FindSpriteShader()
        {
            var s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (s == null) s = Shader.Find("Sprites/Default");
            return s;
        }

        int MetaLv(string id) => metaLv.TryGetValue(id, out var v) ? v : 0;

        // baseStats = 순정 + 구매한 메타 효과. 메타가 바뀌거나 로드 직후 호출.
        void RebuildBaseStats()
        {
            baseStats = (pristineStats ?? new Stats()).Clone();
            if (metaNodes != null)
                foreach (var m in metaNodes)
                {
                    int lv = MetaLv(m.id);
                    if (lv > 0) m.apply(baseStats, lv);
                }
        }

        // 환생 보상 — "이번 회차에 몇 지역까지 깼나"로 직접 차등한다.
        //   예전엔 maxScore(=지역수×100+웨이브) 의 제곱 곡선이라 (a) 환생이 열리는 2지역에서 0을 줬고,
        //   (b) 3~5지역 구간이 뭉개져서 더 깊이 가도 보상이 늘어난 게 체감되지 않았다.
        //   지역을 하나 더 깰 때마다 증가폭 자체가 커지도록(+2,+3,+5,+8,+12,+16,+24) — 깊이 갈수록 이득.
        //   round44: 전부 ×4 — 메타 강화를 다 사는 데 ~1,360 샤드인데 8지역 풀클리어 환생이 70개뿐이라
        //   한 번 환생해도 한두 레벨밖에 못 올렸다. 이제 풀클리어 280(메타 전체의 ~20%), 5지역 72.
        static readonly int[] ShardByRegion = { 0, 0, 8, 20, 40, 72, 120, 184, 280 };
        int ShardReward(int regions)
        {
            int r = Mathf.Clamp(regions, 0, ShardByRegion.Length - 1);
            return ShardByRegion[r];
        }

        // 메타 노드 구매 비용 — 레벨이 오를수록 ×1.4 씩 뛴다. 한 번의 환생으로 같은 노드를
        // 여러 레벨 한꺼번에 사는 게 너무 쉬웠던 문제(고정 비용) 해결.
        int MetaCostAt(UpgradeTree.MetaNode m, int lv) => Mathf.RoundToInt(m.cost * Mathf.Pow(1.4f, lv));

        // 변이 단계가 오를수록 메타 노드 상한이 늘어난다("변이에 따른 강화 횟수 증가") — 단계당 +2.
        // 지금 고른 난이도(ascensionLevel)가 아니라 "지금까지 도달한 최고 승천"(maxAscensionUnlocked) 기준
        // — 쉬운 난이도로 파밍한다고 이미 딴 상한이 줄어들면 안 됨.
        int MetaMaxLvAt(UpgradeTree.MetaNode m) => m.maxLv + maxAscensionUnlocked * 2;

        // 환생 — 시간여행. 2지역 클리어 후 자율적으로 몇 번이든 반복 가능. 진행(골드·노드·지역)만 리셋,
        // 메타 강화·승천 레벨은 그대로 유지 — 승천과는 별개의, 반복 파밍용 루프.
        void Prestige()
        {
            metaCurrency += ShardGainNow();   // 현재 승천 레벨 기준 보상
            bestScore = Mathf.Max(bestScore, maxScore);
            gold = 2000 * MetaLv("m_start");
            bought.Clear();
            repLv.Clear();
            stagesCleared = 0;
            currentStage = 0;
            retryCount = Mathf.Max(0, retryCount / 2);
            maxScore = 0;
            bossSeenMask = 0;
            campaignKills = 0; campaignGold = 0; campaignElapsedSec = 0f;   // 이번 승천 사이클을 다시 시작
            mapScroll = -1f;
            RebuildBaseStats();
            stats = baseStats.Clone();
            GrabRoot();
            wave.LoadStage(0);
            wave.ApplyAscension(ascensionLevel);
            SaveProgress();
            state = State.Map;
        }

        // 승천 보상 미리보기/실제 지급에 같이 쓰는 계산 — 레벨이 오를수록 shard 도 더 준다(15%/레벨).
        int ShardGainNow() => Mathf.RoundToInt(ShardReward(stagesCleared) * (1f + 0.15f * ascensionLevel));

        void BuyMeta(UpgradeTree.MetaNode m)
        {
            if (maxAscensionUnlocked < m.unlockAsc) return;   // 아직 승천으로 안 열림
            int lv = MetaLv(m.id);
            int cost = MetaCostAt(m, lv);
            if (lv >= MetaMaxLvAt(m) || metaCurrency < cost) return;
            metaCurrency -= cost;
            metaLv[m.id] = lv + 1;
            RebuildBaseStats();
            if (sound != null) sound.Play(Sfx.Buy, 0.8f);
            SaveProgress();
        }

        // 코어(root) 는 항상 자동 보유 — 이게 있어야 depth-0 노드가 구매 가능해진다
        void GrabRoot()
        {
            if (nodes == null) return;
            var r = nodes.Find(n => n.IsRoot);
            if (r != null && bought.Add(r.id)) r.apply(stats);
        }

        // ---- 저장/불러오기 ----
        void SaveProgress()
        {
            var d = new SaveData
            {
                stagesCleared = stagesCleared,
                currentStage = currentStage,
                gold = gold,
                retryCount = retryCount,
                maxScore = maxScore,
                bossSeenMask = bossSeenMask,
                everRebirth = everRebirth,
                ascensionLevel = ascensionLevel,
                maxAscensionUnlocked = maxAscensionUnlocked,
                metaCurrency = metaCurrency,
            };
            foreach (var id in bought) d.bought.Add(id);
            foreach (var kv in metaLv) if (kv.Value > 0) d.metaBought.Add(kv.Key + ":" + kv.Value);
            foreach (var kv in repLv) if (kv.Value > 0) d.repLv.Add(kv.Key + ":" + kv.Value);
            SaveSystem.Save(d);
        }

        void LoadProgress()
        {
            var d = SaveSystem.Load();
            hasSave = d != null && d.HasProgress;
            metaLv.Clear();
            metaCurrency = 0;
            bossSeenMask = 0;
            everRebirth = false;
            ascensionLevel = 0;
            maxAscensionUnlocked = 0;
            if (d != null)
            {
                metaCurrency = d.metaCurrency;
                bossSeenMask = d.bossSeenMask;
                everRebirth = d.everRebirth;
                ascensionLevel = d.ascensionLevel;
                maxAscensionUnlocked = d.maxAscensionUnlocked;
                foreach (var e in d.metaBought)
                {
                    var pp = e.Split(':');
                    if (pp.Length == 2 && int.TryParse(pp[1], out var lv)) metaLv[pp[0]] = lv;
                }
            }
        }

        void Cheat(string msg)
        {
            cheatMsg = msg;
            cheatMsgUntil = Time.unscaledTime + 2.5f;
            if (state != State.Title) SaveProgress();   // 타이틀(아직 판 없음)에선 저장하지 않는다
            if (sound != null) sound.Play(Sfx.Buy, 0.6f);
        }

        void DrawCheatMsg()
        {
            if (string.IsNullOrEmpty(cheatMsg) || Time.unscaledTime > cheatMsgUntil) return;
            var st = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(16f * uiScale), fontStyle = FontStyle.Bold };
            st.normal.textColor = new Color(1f, 0.86f, 0.35f); FlatText(st);
            float w = Mathf.Min(Screen.width - 32f, st.CalcSize(new GUIContent(cheatMsg)).x + 40f * uiScale);
            float h = st.CalcHeight(new GUIContent(cheatMsg), w) + 16f * uiScale;
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, 14f * uiScale, w, h);
            var gc = GUI.color;
            GUI.color = new Color(0.08f, 0.09f, 0.11f, 0.88f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = gc;
            GUI.Label(r, cheatMsg, st);
        }

        // round44 버그: 게임 중 '타이틀로'는 상태만 바꿔서 저장도 안 되고 hasSave(=이어하기 버튼)도
        //   게임을 켤 때 읽은 값 그대로였다. 새로 시작한 판이면 이어하기가 안 떠서, 새로고침하면 옛 세이브는
        //   뜨지만 그걸 모르고 '새로 시작'을 누르면(즉시 Wipe) 기록이 날아갔다. 먼저 저장하고 버튼을 갱신한다.
        void GoToTitle()
        {
            if (state != State.Title)
            {
                SaveProgress();
                var d = SaveSystem.Load();
                hasSave = d != null && d.HasProgress;
            }
            newConfirmUntil = -1f;
            state = State.Title;
        }

        // '이어하기' — 세이브를 stats/진행에 적용
        void ContinueGame()
        {
            PurgeSpawnedObjects();
            var d = SaveSystem.Load();
            if (d == null) { NewGame(); return; }
            LoadProgress();
            RebuildBaseStats();
            stats = baseStats.Clone();
            bought.Clear();
            repLv.Clear();
            GrabRoot();
            gold = d.gold;
            stagesCleared = d.stagesCleared;
            currentStage = Mathf.Clamp(d.currentStage, 0, StageConfig.Stages.Length - 1);
            retryCount = d.retryCount;
            maxScore = d.maxScore;
            foreach (var id in d.bought)
            {
                var n = NodeById(id);
                if (n != null && !n.repeat && bought.Add(id)) n.apply(stats);
            }
            // 반복 강화는 일반 노드를 다 적용한 뒤에 — 가지 노드의 공속 하한(0.06)이 반복 강화로 내린 값을
            // 다시 끌어올리지 않게 순서를 고정한다.
            foreach (var e in d.repLv)
            {
                var pp = e.Split(':');
                if (pp.Length != 2 || !int.TryParse(pp[1], out var lv) || lv <= 0) continue;
                var n = NodeById(pp[0]);
                if (n == null || !n.repeat) continue;
                repLv[n.id] = lv;
                bought.Add(n.id);
                for (int k = 0; k < lv; k++) n.apply(stats);
            }
            wave.LoadStage(currentStage);
            wave.ApplyAscension(ascensionLevel);
            state = State.Map;
        }

        void NewGame()
        {
            // 무식하게: 이전 세션·디버그·스테일 어셈블리가 남긴 적/연출 오브젝트를 씬에서 싹 제거
            PurgeSpawnedObjects();
            // 스프라이트/머티리얼 안전망 (Awake가 어중간하게 끝났을 경우 대비)
            if (spriteMat == null)  spriteMat = new Material(Shader.Find("Sprites/Default"));
            if (cellSprites == null) cellSprites = BuildCellSprites();
            if (discSprite == null) discSprite = BuildDiscSprite();

            gold = 0;
            bought.Clear();
            repLv.Clear();
            // round45: 메타(샤드) 강화도 같이 지운다 — "새로 시작"은 완전 초기화인데 예전엔 metaLv/
            //   metaCurrency 를 안 건드려서, 세이브를 지워도 영구 강화가 그대로 남아 있었다.
            //   지운 뒤 RebuildBaseStats() 로 순정 baseStats 를 다시 만들어야 능력치에서도 빠진다.
            metaLv.Clear();
            metaCurrency = 0;
            RebuildBaseStats();
            stats = baseStats != null ? baseStats.Clone() : stats;
            GrabRoot();
            gold = 2000 * MetaLv("m_start");   // 메타를 지웠으니 0
            currentStage = 0;
            stagesCleared = 0;
            retryCount = 0;
            bossSeenMask = 0;
            everRebirth = false;   // "새로 시작"은 완전 초기화 — 이전 판에서 해금했던 메타/환생 버튼도 다시 숨김
            ascensionLevel = 0;
            maxAscensionUnlocked = 0;
            campaignKills = 0; campaignGold = 0; campaignElapsedSec = 0f;
            mapScroll = -1f;
            wave.LoadStage(currentStage);
            wave.ApplyAscension(ascensionLevel);
            state = State.Map;
        }

        void PurgeSpawnedObjects()
        {
            enemies.Clear();
            bursts.Clear();
            splats.Clear();
            boss = null;
            foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                string nm = sr.gameObject.name;
                if (nm == "Enemy" || nm == "Burst" || nm == "Boss" || nm == "PROBE" || nm == "Splat")
                    Destroy(sr.gameObject);
            }
        }

        void ApplyWaveParams()
        {
            kills = 0;
            quota = wave.QuotaAt(waveNum);
            spawnInterval = wave.SpawnIntervalAt(waveNum) * stats.spawnIntervalMult;
            if (timeLeft <= 0f) timeLeft = wave.TimeAt(waveNum) + stats.bonusTimeSec;
        }

        void StartRun()
        {
            wave.LoadStage(currentStage);   // 지역 파라미터 적용
            wave.ApplyAscension(ascensionLevel);   // 승천 배율(적/보스 체력↑, 골드↓)
            ApplyFieldLook();               // 변이 단계만큼 감염된 전장 색
            foreach (var e in enemies) if (e.go) Destroy(e.go);
            enemies.Clear();
            boss = null;
            foreach (var b in bursts) if (b.tr) Destroy(b.tr.gameObject);
            bursts.Clear();
            foreach (var sp in splats) if (sp.tr) Destroy(sp.tr.gameObject);   // 새 판 = 깨끗한 배양접시
            splats.Clear();
            floaters.Clear();
            goldFloats.Clear();
            attackTimer = 0f;
            spawnTimer = 0f;
            bombTimer = 0f;
            cycleKills = 0;
            cycleGold = 0;
            bossesKilled = 0;
            waveNum = Mathf.Clamp(stats.startWave, 1, Mathf.Max(1, wave.totalWaves - 1));  // 보스 웨이브로는 시작 안 함
            timeLeft = wave.TimeAt(waveNum) + stats.bonusTimeSec;
            ApplyWaveParams();
            if (waveNum >= wave.totalWaves) SpawnBoss();
            else for (int i = 0; i < wave.startEnemies; i++) SpawnEnemy();
            state = State.Playing;
        }

        void AdvanceWave()
        {
            waveNum++;
            ApplyWaveParams();
            if (waveNum >= wave.totalWaves && boss == null) SpawnBoss();
        }

        void OnWaveClear()
        {
            if (waveNum >= wave.totalWaves) return;  // 보스 웨이브는 쿼터로 안 넘어감 (보스 처치 = 클리어)
            if (sound != null) sound.Play(Sfx.Wave, 0.55f);
            AdvanceWave();
        }

        void OnTimeout()
        {
            if (sound != null) sound.Play(Sfx.Timeout, 0.8f);
            retryCount++;
            maxScore = Mathf.Max(maxScore, stagesCleared * 100 + waveNum);
            SaveProgress();
            mapIndex = currentStage; mapScroll = currentStage;   // 지도로 나가면 방금 시도한 지역이 선택돼 있게
            resultCleared = false;
            state = State.Result;
        }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        Enemy SpawnEnemy(bool asBomb = false)
        {
            // 캡슐은 적이 꽉 차 있을 때 더 필요하다 — 상한을 조금 넘겨서라도 나오게
            if (enemies.Count >= wave.maxEnemies + (asBomb ? BombMaxAlive : 0)) return null;

            if (cellSprites == null) cellSprites = BuildCellSprites();
            if (capsuleSprite == null) capsuleSprite = BuildCapsuleSprite();
            if (spriteMat == null) spriteMat = new Material(FindSpriteShader());

            bool isElite = !asBomb && Random.value < EliteChance;
            int cellTier = Mathf.Clamp(currentStage, 0, cellSprites.Length - 1);
            Sprite cellSprite = cellSprites[cellTier];

            var go = new GameObject(asBomb ? "Vaccine" : isElite ? "Elite" : "Enemy");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = asBomb ? capsuleSprite : cellSprite;
            sr.sharedMaterial = spriteMat;
            sr.sortingOrder = asBomb ? 11 : 10;   // 캡슐은 잡몹보다 살짝 위 — 묻히지 않게
            sr.enabled = true;

            float sizeMul = asBomb ? 1.9f : Random.Range(wave.enemySizeRange.x, wave.enemySizeRange.y);   // 캡슐은 잡몹 떼 속에서도 눈에 띄게

            var e = new Enemy
            {
                go = go,
                tr = go.transform,
                sr = sr,
                r = wave.enemyRadius * sizeMul,
                visScale = asBomb ? 1f : (cellVisualBoost != null ? cellVisualBoost[cellTier] : 1f),
                baseColor = asBomb ? new Color(0.94f, 0.96f, 0.99f) : WaveColor(waveNum),
                wobSpeed = asBomb ? 0.8f : Random.Range(1.4f, 3.4f),
                rotSpeed = asBomb ? 0f : Random.Range(-150f, 150f) + Mathf.Sign(Random.value - 0.5f) * 40f,  // 0 근처를 피해 확실히 돌게
                wobPhase = Random.value * 6.2832f,
                spawnWave = waveNum,
                elite = isElite,
                bomb = asBomb,
            };
            e.hpMax = e.hp = Mathf.CeilToInt(wave.EnemyHpAt(waveNum) * sizeMul
                                             * (isElite ? EliteHpMul : asBomb ? 0.6f : 1f));

            float sp = Random.Range(0.25f, 0.65f);
            float ang = Random.value * Mathf.PI * 2f;
            e.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * sp;

            var fr = FieldRect;
            e.tr.position = new Vector3(
                Random.Range(fr.xMin + e.r, fr.xMax - e.r),
                Random.Range(fr.yMin + e.r, fr.yMax - e.r), 0f);
            e.tr.localScale = Vector3.one * (e.r * 2f);
            var col = e.baseColor; col.a = 1f;   // 알파 확실히 1
            sr.color = col;

            // 변이개체 — 같은 실루엣을 살짝 크게 빨갛게 뒤에 깔아 테두리처럼. 자식이라 회전·흔들림을
            // 그대로 따라가고, 본체 flash 는 e.sr 만 바꾸므로 테두리는 항상 빨갛게 남는다.
            if (isElite)
            {
                var og = new GameObject("EliteRim");
                og.transform.SetParent(go.transform, false);
                og.transform.localScale = Vector3.one * 1.24f;
                var osr = og.AddComponent<SpriteRenderer>();
                osr.sprite = cellSprite;
                osr.sharedMaterial = spriteMat;
                osr.sortingOrder = 9;
                osr.color = new Color(0.95f, 0.10f, 0.12f, 1f);
            }

            enemies.Add(e);
            return e;
        }

        void SpawnBoss()
        {
            if (boss != null) return;
            if (cellSprites == null) cellSprites = BuildCellSprites();
            if (bossSprites == null) bossSprites = BuildBossSprites();
            if (spriteMat == null) spriteMat = new Material(FindSpriteShader());

            var go = new GameObject("Boss");
            var sr = go.AddComponent<SpriteRenderer>();
            int bi = Mathf.Clamp(currentStage, 0, bossSprites.Length - 1);
            sr.sprite = (bossSprites[bi] != null) ? bossSprites[bi] : cellSprites[Mathf.Clamp(bi, 0, cellSprites.Length - 1)];   // 지도 카드와 같은 지역별 실루엣
            sr.sharedMaterial = spriteMat;
            sr.sortingOrder = 12;
            sr.enabled = true;

            // 보스 크기 — 지도 카드와 같은 스프라이트의 "몸통 비율"(BossBodyFrac)에 맞춰 타격판정 반경도
            // 같이 커지게(0.36~0.50 → 기준 최종보스에서 3.6). 예전 고정 2.2보다 전 지역에서 더 크다.
            const float bossBaseR = 3.6f;
            float bossR = bossBaseR * (BossBodyFrac(bi, StageConfig.Stages.Length) / 0.5f);

            boss = new Enemy
            {
                go = go,
                tr = go.transform,
                sr = sr,
                r = bossR,
                baseColor = new Color(0.80f, 0.12f, 0.14f), // 위협적인 진한 빨강
                wobSpeed = 0.9f,
                rotSpeed = 25f,   // 보스는 크니까 천천히, 위압감 있게
                wobPhase = 0f,
                spawnWave = wave.totalWaves,
            };
            // 무한 모드마다 +100%. round43: float 로 곱한다 — 8지역 보스가 3억대라 int 로 곱하면 7번째에 넘친다
            boss.hpMax = boss.hp = (float)wave.bossHp * (bossesKilled + 1);
            float ang = Random.value * Mathf.PI * 2f;
            boss.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.35f;
            boss.visScale = (bossVisualBoost != null && bi < bossVisualBoost.Length) ? bossVisualBoost[bi] : 1f;
            boss.tr.position = Vector3.zero;
            boss.tr.localScale = Vector3.one * (boss.r * 2f * boss.visScale);
            sr.color = boss.baseColor;
            bossTeleportTimer = BossTeleportInterval / BossTeleportSpeedMul(ascensionLevel);

            // ---- 보스 패턴(변이 레벨별로 하나씩 켜짐, round35) ----
            boss.critResist = ascensionLevel >= 1;   // 변이1+: 치명타 "추가" 피해 절반만
            boss.shielded = false;
            boss.shieldTimer = ascensionLevel >= 3 ? ShieldPhaseInterval : -1f;  // 변이3+: 무적 페이즈 활성

            enemies.Add(boss); // 이동·피격·타격 로직을 그대로 태움
            if (sound != null) sound.Play(Sfx.Boss, 0.9f);

            // 이 지역 보스 웨이브에 도달 — 지도에서 그림자 대신 실제 보스가 꿈틀거린다
            if (currentStage >= 0 && currentStage < 32)
            {
                int bit = 1 << currentStage;
                if ((bossSeenMask & bit) == 0) { bossSeenMask |= bit; SaveProgress(); }
            }
        }

        // 변이2+: 순간이동이 더 잦아진다 — 쫓아가는 재미 대신 예측/포지셔닝을 요구.
        static float BossTeleportSpeedMul(int ascLv) => ascLv >= 2 ? 1f + 0.25f * (ascLv - 1) : 1f;

        // 보스가 맵 임의 좌표로 순간이동 — 커서 범위가 커진 후반에도 "그냥 눌러앉아 있기"를 막는다.
        // 필드 밖으로 튀어나가지 않게 반지름만큼 안쪽으로 클램프, 커서와 너무 가까우면 다시 뽑는다.
        void TeleportBoss()
        {
            if (boss == null) return;
            bossTeleportTimer = BossTeleportInterval / BossTeleportSpeedMul(ascensionLevel);
            var fr = FieldRect;
            Vector3 from = boss.tr.position;
            Vector3 to = from;
            for (int tries = 0; tries < 12; tries++)
            {
                float x = Random.Range(fr.xMin + boss.r, fr.xMax - boss.r);
                float y = Random.Range(fr.yMin + boss.r, fr.yMax - boss.r);
                to = new Vector3(x, y, 0f);
                float dx = to.x - cursorWorld.x, dy = to.y - cursorWorld.y;
                if (dx * dx + dy * dy >= 36f) break;   // 커서에서 최소 6유닛 — 바로 옆으로 오는 허탈함 방지
            }
            SpawnBurst(from, boss.baseColor, boss.r * 1.3f);
            boss.tr.position = to;
            float ang = Random.value * Mathf.PI * 2f;
            boss.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.35f;
            boss.flash = 0.15f;
            SpawnBurst(to, Color.white, boss.r * 1.3f);
            if (sound != null) sound.Play(Sfx.Boss, 0.45f, 1.5f);
        }

        void SpawnBurst(Vector3 pos, Color col, float r)
        {
            if (bursts.Count >= 34) return;
            var go = new GameObject("Burst");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = discSprite;
            sr.sharedMaterial = spriteMat;
            sr.sortingOrder = 20;
            go.transform.position = pos;
            sr.color = col;
            bursts.Add(new Burst { tr = go.transform, sr = sr, life = 0.32f, maxLife = 0.32f, r0 = r, col = col });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0.1f) dt = 0.1f;

            UpdateCursor();
            UpdateSpecks(dt);
            UpdateBursts(dt);
            UpdateSplats(dt);
            if (sound != null) sound.Tick(dt);
            UpdateTransition(dt);

            var kb = Keyboard.current;

            // ESC — 상태별 처리
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (state == State.Playing)      { pauseReturn = State.Playing; state = State.Paused; }
                else if (state == State.Paused)  { state = pauseReturn; }
                else if (state == State.Settings)
                {
                    if (audioDirty && sound != null) { sound.Save(); audioDirty = false; }
                    state = settingsReturn;
                }
                else if (state == State.Win)     { GoToTitle(); }
                else if (state == State.Map)     { pauseReturn = State.Map; state = State.Paused; }   // 톱니바퀴와 같은 메뉴
                else if (state == State.Meta)    { state = State.Map; }
                else if (state == State.Result)  { state = State.Map; }
                else if (state == State.Tree)    { state = State.Map; }
            }

            // ---- 치트키 (round45) — Shift + 숫자. 누르면 화면 위에 잠깐 표시되고 바로 저장된다 ----
            //   F 키는 브라우저가 다 쓴다(F5 새로고침, F11 전체화면, F12 개발자도구) — 웹 빌드에서 개발자도구가
            //   열려버려서 Shift+숫자로 옮겼다(브라우저 기본 동작 없음). 에디터에선 Game 창을 클릭해 포커스를 줄 것.
            bool shift = kb != null && kb.shiftKey.isPressed;
            // Shift+1 — 환생 샤드 +500
            if (shift && kb.digit1Key.wasPressedThisFrame)
            {
                metaCurrency += 500;
                Cheat(Loc.F("cheat.shards", 500, metaCurrency));
            }

            // Shift+2 — 다음 지역 하나 해금
            if (shift && kb.digit2Key.wasPressedThisFrame)
            {
                stagesCleared = Mathf.Min(StageConfig.Stages.Length - 1, stagesCleared + 1);
                if (stagesCleared >= 2) everRebirth = true;
                mapScroll = -1f;
                Cheat(Loc.F("cheat.stage", stagesCleared + 1));
            }

            // Shift+3 — 모든 지역(지도) 해금 — tier 게이팅도 같이 풀린다 (IsBuyable 이 stagesCleared 기준)
            if (shift && kb.digit3Key.wasPressedThisFrame)
            {
                stagesCleared = Mathf.Max(stagesCleared, StageConfig.Stages.Length - 1);
                everRebirth = true;
                mapScroll = -1f;   // 캐러셀 재초기화
                Cheat(Loc.T("cheat.allStages"));
            }

            // Shift+4 — 모든 노드 즉시 해금(tier 게이팅 무시, 반복 강화 제외)
            if (shift && kb.digit4Key.wasPressedThisFrame && nodes != null)
            {
                foreach (var n in nodes)
                    if (!n.repeat && !bought.Contains(n.id)) { bought.Add(n.id); n.apply(stats); }
                Cheat(Loc.T("cheat.allNodes"));
            }

            if (state != State.Playing) return;

            timeLeft -= dt;
            if (timeLeft <= 0f) { timeLeft = 0f; OnTimeout(); return; }

            campaignElapsedSec += dt;   // 승천 사이클 전체 소요 시간(Win 화면 표시용)

            if (boss != null)
            {
                bossTeleportTimer -= dt;
                if (bossTeleportTimer <= 0f) TeleportBoss();

                // 변이3+: 일정 간격마다 잠깐 무적 페이즈 — 그동안은 때려도 피해가 안 들어간다.
                if (boss.shieldTimer > -0.5f)
                {
                    boss.shieldTimer -= dt;
                    if (boss.shieldTimer <= 0f)
                    {
                        boss.shielded = !boss.shielded;
                        boss.shieldTimer = boss.shielded ? ShieldPhaseDuration : ShieldPhaseInterval;
                        if (sound != null) sound.Play(Sfx.Boss, 0.4f, boss.shielded ? 1.7f : 0.8f);
                    }
                }
            }

            // 보스 웨이브에도 잡몹이 계속 나온다(round36). 예전엔 보스만 덩그러니 남아서 제한시간의 70~110%를
            // 혼자 먹는 "조용한 장시간 딜링" 구간이 됐다 — 잡몹 구간은 짧은데 보스전만 길다는 문제의 핵심.
            // 이제 보스전은 "잡몹을 계속 터뜨리면서 그 사이를 순간이동하는 보스를 쫓는" 구간이 된다.
            // 다만 보스에게 화력을 집중할 여지는 남겨야 하므로 소환 속도는 절반으로.
            int guard;
            {
                bool bossWave = waveNum >= wave.totalWaves;
                float si = bossWave ? spawnInterval * BossWaveSpawnSlow : spawnInterval;
                spawnTimer += dt;
                guard = 0;
                while (spawnTimer >= si && guard++ < 24)
                {
                    spawnTimer -= si;
                    int n = Mathf.Max(1, stats.spawnCount);   // round42: 기본 1마리, 노드로 최대 10
                    for (int k = 0; k < n; k++) SpawnEnemy();
                }
                if (enemies.Count >= wave.maxEnemies)
                    spawnTimer = Mathf.Min(spawnTimer, si);
            }

            // 백신 캡슐 — 노드로 해금된 뒤부터, 자체 타이머로(보스 웨이브에도). 소환 노드와 무관.
            if (stats.bombUnlocked)
            {
                bombTimer += dt;
                if (bombTimer >= stats.bombInterval)
                {
                    int alive = 0;
                    foreach (var e in enemies) if (e.bomb) alive++;
                    if (alive < BombMaxAlive) { SpawnEnemy(true); bombTimer = 0f; }
                    else bombTimer = stats.bombInterval;   // 꽉 찼으면 자리 나는 즉시 다음 것
                }
            }

            var fr = FieldRect;
            float t = Time.time;
            foreach (var e in enemies)
            {
                Vector3 p = e.tr.position;
                p.x += e.vel.x * dt;
                p.y += e.vel.y * dt;
                if (p.x < fr.xMin + e.r) { p.x = fr.xMin + e.r; e.vel.x = -e.vel.x; }
                if (p.x > fr.xMax - e.r) { p.x = fr.xMax - e.r; e.vel.x = -e.vel.x; }
                if (p.y < fr.yMin + e.r) { p.y = fr.yMin + e.r; e.vel.y = -e.vel.y; }
                if (p.y > fr.yMax - e.r) { p.y = fr.yMax - e.r; e.vel.y = -e.vel.y; }
                e.tr.position = p;
                e.tr.Rotate(0f, 0f, e.rotSpeed * dt);   // 이동 방향과 별개로 계속 회전 — 불규칙하게 뒤척이는 느낌

                float dd = e.r * 2f * e.visScale;
                float sx = 1f + 0.055f * Mathf.Sin(t * e.wobSpeed + e.wobPhase);
                float sy = 1f + 0.055f * Mathf.Sin(t * e.wobSpeed * 1.13f + e.wobPhase + 1.7f);
                e.tr.localScale = new Vector3(dd * sx, dd * sy, 1f);

                if (e.flash > 0f)
                {
                    e.flash -= dt;
                    e.sr.color = Color.Lerp(e.baseColor, Color.white, Mathf.Clamp01(e.flash / 0.08f));
                    if (e.flash <= 0f) e.sr.color = e.baseColor;
                }
            }

            attackTimer += dt;
            guard = 0;
            if (stats.autoAttack)
            {
                while (attackTimer >= stats.attackInterval && state == State.Playing && guard++ < 10)
                {
                    attackTimer -= stats.attackInterval;
                    AttackPulse();
                }
            }
            else
            {
                // 자동 해금 전 — 좌클릭마다 커서 위치에 1회 타격(약물 투하). 쿨다운으로 연타 방지.
                manualClickCd -= dt;
                var ms = Mouse.current;
                if (ms != null && ms.leftButton.wasPressedThisFrame && manualClickCd <= 0f)
                {
                    manualClickCd = Mathf.Max(0.28f, stats.attackInterval * 1.08f);   // 자동보다 살짝만 느리게
                    AttackPulse();
                }
            }

            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                floaters[i].life -= dt;
                floaters[i].world += Vector3.up * (1.2f * dt);
                if (floaters[i].life <= 0f) floaters.RemoveAt(i);
            }
            for (int i = goldFloats.Count - 1; i >= 0; i--)
            {
                goldFloats[i].age += dt;
                if (goldFloats[i].age >= goldFloats[i].life) goldFloats.RemoveAt(i);
            }
        }

        void UpdateSpecks(float dt)
        {
            var fr = FieldRect;
            for (int i = 0; i < specks.Count; i++)
            {
                var s = specks[i];
                Vector3 p = s.position + (Vector3)(speckVel[i] * dt);
                if (p.x < fr.xMin) p.x = fr.xMax; else if (p.x > fr.xMax) p.x = fr.xMin;
                if (p.y < fr.yMin) p.y = fr.yMax; else if (p.y > fr.yMax) p.y = fr.yMin;
                s.position = p;
            }
        }

        void UpdateBursts(float dt)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var b = bursts[i];
                b.life -= dt;
                if (b.life <= 0f) { if (b.tr) Destroy(b.tr.gameObject); bursts.RemoveAt(i); continue; }
                float k = 1f - b.life / b.maxLife;
                b.tr.localScale = Vector3.one * (b.r0 * 2f * (1f + k * 2.0f));
                var col = b.col; col.a = (1f - k) * 0.55f;
                b.sr.color = col;
            }
        }

        void AttackPulse()
        {
            float R = stats.cursorRadius;
            bool hitAny = false;
            bool stop = false;
            for (int i = enemies.Count - 1; i >= 0 && !stop; i--)
            {
                var e = enemies[i];
                if (e.dead) continue;   // 이번 펄스에서 폭발 연쇄로 이미 죽음
                float dx = e.tr.position.x - cursorWorld.x;
                float dy = e.tr.position.y - cursorWorld.y;
                float rr = R + e.r + 0.05f;   // 테두리에 닿아 보이면 반드시 맞도록 살짝 여유
                if (dx * dx + dy * dy > rr * rr) continue;

                // 펄스당 1번만 — 공격 간격이 곧 리듬이 된다 (적 수와 무관하게 일정)
                if (!hitAny) { hitAny = true; if (sound != null) sound.Play(Sfx.Pulse, 0.5f); }

                var (amount, crit) = stats.RollDamage();

                // ---- 보스 패턴(round35): 변이1+ 치명 내성, 변이3+ 무적 페이즈 ----
                bool isBoss = e == boss;
                if (isBoss && crit && e.critResist)
                {
                    float baseDmg = stats.GetHitDamage();
                    amount = baseDmg + (amount - baseDmg) * 0.5f;   // 치명타 "추가" 피해만 절반
                }
                bool blocked = isBoss && e.shielded;
                if (blocked) amount = 0f;

                if (!blocked) e.hp -= amount;
                e.flash = 0.08f;

                if (floaters.Count < 40)
                    floaters.Add(new Floater
                    {
                        world = e.tr.position + Vector3.up * (e.r + 0.1f),
                        life = 0.55f,
                        text = blocked ? "0" : (Mathf.RoundToInt(amount).ToString() + (crit ? "!" : "")),
                        crit = crit && !blocked,
                    });

                if (blocked) continue;   // 무적 중엔 이 적(보스)이 죽을 수 없음 — 사망 처리 스킵

                if (e.hp <= 0f) stop = KillEnemy(e, crit);
            }
            enemies.RemoveAll(x => x.dead);
        }

        // 적 하나 사망 처리. true = 이번 펄스를 여기서 멈춰야 함(보스 처치로 화면 전환 / 웨이브 클리어).
        //   round42: 목록에서 바로 빼지 않고 dead 표시만 — 백신 폭발이 다른 적을 연쇄로 죽여도
        //   AttackPulse 의 인덱스 순회가 꼬이지 않게, 펄스 끝에 RemoveAll 로 한꺼번에 뺀다.
        bool KillEnemy(Enemy e, bool crit)
        {
            if (e.dead) return false;
            e.dead = true;

            if (e.bomb)
            {
                // 백신 캡슐 — 골드·처치 수 없음. 대신 터지면서 주변을 쓸어버린다.
                Vector3 bpos = e.tr.position;
                if (e.go) Destroy(e.go);
                return Detonate(bpos);
            }

            bool wasBoss = e == boss;
            int g = Mathf.RoundToInt(wave.GoldAt(e.spawnWave) * (1f + stats.goldMultPercent / 100f));
            if (e.elite) g *= EliteGoldMul;   // 변이개체 — 반가운 보너스
            SpawnBurst(e.tr.position, e.baseColor, e.r);
            SpawnSplat(e.tr.position, e.baseColor, e.r);
            if (e.elite) SpawnBurst(e.tr.position, new Color(0.95f, 0.12f, 0.14f), e.r * 1.3f);
            if (wasBoss) { SpawnBurst(e.tr.position, e.baseColor, e.r * 1.6f); SpawnBurst(e.tr.position, Color.white, e.r); }
            if (goldFloats.Count < 30)
                goldFloats.Add(new GoldFloat { startWorld = e.tr.position, age = 0f, life = 1.0f, amount = g });

            if (e.go) Destroy(e.go);
            kills++;
            cycleKills++;
            campaignKills++;
            gold += g;
            cycleGold += g;
            campaignGold += g;

            if (wasBoss)
            {
                boss = null; bossesKilled++;
                if (sound != null) sound.Play(Sfx.Win, 1f);
                stagesCleared = Mathf.Max(stagesCleared, currentStage + 1);
                maxScore = Mathf.Max(maxScore, stagesCleared * 100);
                if (stagesCleared >= 2) everRebirth = true;   // 한 번 열리면 환생해도 메타/환생 버튼은 계속 보임
                mapIndex = currentStage; mapScroll = currentStage;   // 지도로 돌아가면 방금 (재)클리어한 지역이 선택돼 있게

                if (currentStage >= StageConfig.Stages.Length - 1)
                {
                    // 8지역 전부 클리어 = 승천. 환생과는 별개 — 여기서만 승천 사다리가 오른다.
                    winKills = campaignKills; winGold = campaignGold; winTimeSec = campaignElapsedSec;   // Win 화면 스냅샷

                    lastAscendShardGain = ShardGainNow();
                    metaCurrency += lastAscendShardGain;
                    bestScore = Mathf.Max(bestScore, maxScore);

                    lastAscendWasFirst = maxAscensionUnlocked == 0;
                    lastAscendLevelPlayed = ascensionLevel;   // Win 화면에 "이번에 깬 난이도"로 표시
                    if (ascensionLevel >= maxAscensionUnlocked) maxAscensionUnlocked = ascensionLevel + 1;
                    ascensionLevel = maxAscensionUnlocked;   // 다음 판 기본값 = 새로 연 상한(화살표로 낮출 수 있음)

                    metaLv.Clear();   // 메타 강화 레벨 초기화 — 대신 승천 레벨만큼 새 노드/더 높은 상한이 열림
                    RebuildBaseStats();
                    bought.Clear();
                    repLv.Clear();
                    gold = 2000 * MetaLv("m_start");   // 방금 초기화했으니 사실상 0
                    stats = baseStats.Clone();
                    GrabRoot();
                    stagesCleared = 0;
                    currentStage = 0;
                    maxScore = 0;
                    bossSeenMask = 0;
                    retryCount = 0;
                    campaignKills = 0; campaignGold = 0; campaignElapsedSec = 0f;   // 다음 승천 사이클 시작
                    wave.LoadStage(0);
                    wave.ApplyAscension(ascensionLevel);
                    SaveProgress();
                    state = State.Win;
                }
                else
                {
                    SaveProgress();
                    resultCleared = true;
                    state = State.Result;   // 바로 지도로 안 넘기고 선택지 3개(업그레이드/재도전/지도로)
                }
                return true;
            }
            if (sound != null) sound.PlayKill(crit);
            // 쿼터를 채워 "실제로 다음 웨이브로 넘어갈 때만" 펄스를 멈춘다.
            //   round43 버그: 보스 웨이브는 쿼터를 채워도 안 넘어가는데(OnWaveClear 가 그냥 return)
            //   여기서 매번 true 를 돌려줘서, 보스 옆 잡몹이 하나 죽을 때마다 그 펄스가 끊겼다.
            //   펄스는 최근에 나온 적부터 도는데 보스는 목록 앞쪽이라 차례가 안 와서, 잡몹이 정리될
            //   때까지(0.5초쯤) 범위 안의 보스가 안 맞았다.
            if (kills >= quota && waveNum < wave.totalWaves) { OnWaveClear(); return true; }
            return false;
        }

        // 백신 폭발 — 반경 안의 잡몹은 타격 × bombDmgMul, 보스는 최대체력 × BombBossFrac(1%→5%).
        //   폭발로 죽은 적도 KillEnemy 로 똑같이 처리(골드·처치 수) — 다른 캡슐이 휘말리면 연쇄 폭발.
        bool Detonate(Vector3 pos)
        {
            float rad = BombBaseRadius * stats.bombRadiusMul;
            var vc = new Color(0.62f, 0.93f, 1f);   // 백신 색(연한 청록)
            SpawnBurst(pos, Color.white, rad / 3f);   // 링이 정확히 폭발 반경까지 퍼진다(최종 지름 = r0×6)
            SpawnBurst(pos, vc, rad / 4.2f);
            SpawnSplat(pos, vc, rad * 0.85f);          // 큼직한 흔적 — 폭발 반경을 거의 덮고 판 끝날 때까지 남는다
                                                       // (자국 스프라이트가 텍스처를 절반쯤만 채워서 0.42 로는 반경의 40%뿐이었다)
            if (sound != null) sound.Play(Sfx.Boss, 0.7f, 1.35f);

            float hit = stats.GetHitDamage() * stats.bombDmgMul;
            for (int j = enemies.Count - 1; j >= 0; j--)
            {
                var o = enemies[j];
                if (o.dead) continue;
                float dx = o.tr.position.x - pos.x, dy = o.tr.position.y - pos.y;
                float rr = rad + o.r;
                if (dx * dx + dy * dy > rr * rr) continue;

                bool isBoss = o == boss;
                if (isBoss && o.shielded) continue;   // 무적 페이즈는 폭발도 막는다
                float dmg = isBoss ? o.hpMax * stats.BombBossFrac : hit;
                o.hp -= dmg;
                o.flash = 0.12f;
                if (floaters.Count < 40)
                    floaters.Add(new Floater
                    {
                        world = o.tr.position + Vector3.up * (o.r + 0.1f),
                        life = 0.6f,
                        text = Mathf.RoundToInt(dmg).ToString(),
                        crit = true,
                    });
                if (o.hp <= 0f && KillEnemy(o, false)) return true;
            }
            return false;
        }

        void UpdateCursor()
        {
            if (Mouse.current == null) return;
            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, -cam.transform.position.z));
            w.z = 0f;
            cursorWorld = w;
            cursorRing.position = w;
            // 보이는 반경 = 실제 타격 반경. (예전엔 맥동으로 살짝 커 보여 "범위 안인데 안 맞음"이 났다)
            float cr = stats.cursorRadius;
            cursorRing.localScale = Vector3.one * (cr * 2f);
            // 테두리 두께 — 반경에 고정 비율(옛날 16%)로 그리면 후반에 범위가 커질수록 테두리만 두꺼워져
            // 보기 흉해진다. 절대 두께가 거의 일정하게 유지되도록 반경이 커질수록 비율을 줄인다.
            if (Mathf.Abs(cr - lastRingRadius) > 0.01f)
            {
                lastRingRadius = cr;
                float borderFrac = Mathf.Clamp(0.05f / Mathf.Max(0.05f, cr), 0.03f, 0.16f);
                var old = ringSprite;
                ringSprite = BuildRingSprite(borderFrac);
                if (cursorBorderSr != null) cursorBorderSr.sprite = ringSprite;
                if (old != null && old.texture != null) Destroy(old.texture);
            }
            cursorSr.enabled = state == State.Playing;
            if (cursorBorderSr != null) cursorBorderSr.enabled = state == State.Playing;
        }

        // ---- 스킬트리 로직 ----
        bool IsBought(string id) => bought.Contains(id);

        bool IsBuyable(UpgradeNode n)
        {
            if (IsBought(n.id) && !n.repeat) return false;
            if (n.tier > stagesCleared) return false;   // 지역 N 클리어 전엔 tier N 잠김
            if (n.IsRoot) return true;
            return IsBought(n.parentId);
        }

        UpgradeNode NodeById(string id) => nodes.Find(m => m.id == id);

        // 트리는 "이미 다 뻗어있는 지도"가 아니라 "가운데서 자라나는 것"으로 보여준다(round38).
        //   보이는 것 = 산 노드 + 그 바로 다음 노드(=지금 살 수 있는 경계)뿐. 그 너머는 아예 안 그린다.
        //   그래서 먼 노드를 눌러 앞쪽까지 한 번에 사는 "일괄구매"도 필요 없어져 같이 걷어냈다.
        bool IsRevealed(UpgradeNode n) => n.IsRoot || IsBought(n.id) || IsBought(n.parentId);

        // 노드 하나만 구매 — 경계(부모가 뚫린 노드)만 살 수 있으므로 경로 구매가 불필요하다
        int RepLevel(string id) => repLv.TryGetValue(id, out var v) ? v : 0;

        // 반복 강화 count 회 — 가진 골드로 살 수 있는 만큼까지만(0이면 아무 것도 안 함)
        int BuyRepeat(UpgradeNode n, int count)
        {
            if (n == null || !n.repeat || !IsBuyable(n) || n.cost <= 0) return 0;
            int can = (int)System.Math.Min((long)count, gold / (long)n.cost);
            if (can <= 0) return 0;
            gold -= can * n.cost;
            for (int k = 0; k < can; k++) n.apply(stats);
            repLv[n.id] = RepLevel(n.id) + can;
            bought.Add(n.id);
            if (sound != null) sound.Play(Sfx.Buy, 0.75f);
            SaveProgress();
            return can;
        }

        void BuyNode(UpgradeNode n)
        {
            if (n != null && n.repeat) return;   // 반복 강화는 BuyRepeat(+1/+10/+MAX 창)으로
            if (n == null || !IsBuyable(n) || gold < n.cost) return;
            gold -= n.cost;
            bought.Add(n.id);
            n.apply(stats);
            if (sound != null) sound.Play(Sfx.Buy, 0.75f);
            SaveProgress();
        }

        // ============================================================
        // 렌더
        // ============================================================
        void EnsureStyles()
        {
            // 화면 높이에 비례한 UI 배율. 예전엔 1.5 고정이라 브라우저 창이 작으면 HUD·패널이
            // 화면을 뒤덮었다. 760p 에서 1.0, 1080p 에서 ~1.42. 0.01 단위로 양자화해서
            // 리사이즈 중 스타일이 매 프레임 재생성되지 않게 한다.
            uiScale = Mathf.Round(Mathf.Clamp(Screen.height / 760f, 0.72f, 1.6f) * 100f) * 0.01f;

            if (sLabel != null && Mathf.Approximately(lastUiScale, uiScale)) return;
            lastUiScale = uiScale;

            // 한글 폰트 — 에디터는 OS 폰트로 폴백되지만 WebGL 빌드엔 폴백이 없어 한글이 안 보인다.
            if (uiFont == null) uiFont = Resources.Load<Font>("PopCellKR");
            if (uiFont != null) GUI.skin.font = uiFont;

            int fBase  = Mathf.RoundToInt(16f * uiScale);
            int fBig   = Mathf.RoundToInt(26f * uiScale);
            int fSmall = Mathf.RoundToInt(12f * uiScale);
            sLabel  = new GUIStyle(GUI.skin.label) { fontSize = fBase };
            if (uiFont != null) sLabel.font = uiFont;
            sLabel.normal.textColor = Ink;
            FlatText(sLabel);                                  // hover/active/focused 글자색도 노말과 동일
            sGold   = new GUIStyle(sLabel) { normal = { textColor = Gold } };
            FlatText(sGold);
            sCenter = new GUIStyle(sLabel) { alignment = TextAnchor.UpperCenter };
            sBig    = new GUIStyle(sLabel) { fontSize = fBig, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            sSmall  = new GUIStyle(sLabel) { fontSize = fSmall };

            sBtn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(15f) };
            if (uiFont != null) sBtn.font = uiFont;
            sBtn.normal.textColor = Ink;
            FlatText(sBtn);
        }

        // 모든 상태의 글자색을 normal과 같게 → 마우스 오버해도 텍스트 색이 안 바뀜
        static void FlatText(GUIStyle s)
        {
            var col = s.normal.textColor;
            s.hover.textColor = s.active.textColor = s.focused.textColor =
                s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = col;
        }

        // 이 노드를 사면 스탯이 어떻게 되는지 — 트리 화면 미리보기용.
        // 경계 노드만 살 수 있게 바뀌었으므로(round38) 그 노드 하나만 반영하면 된다.
        Stats PreviewStats(UpgradeNode target)
        {
            var s = stats.Clone();
            if (target != null && !IsBought(target.id)) target.apply(s);
            return s;
        }

        // 현재 능력치 한 줄. preview가 현재와 다르면 "지금 → 바뀔 값"으로 보여준다.
        static readonly Color Up = new Color(0.49f, 0.92f, 0.62f);   // 상승 표시(초록)

        string[] StatLabels => new[]
        { Loc.T("stat.dmg"), Loc.T("stat.crit"), Loc.T("stat.aspd"), Loc.T("stat.range"),
          Loc.T("stat.gold"), Loc.T("stat.spawn"), Loc.T("stat.time"), Loc.T("stat.startWave"), Loc.T("stat.bomb") };

        string[] StatValues(Stats s)
        {
            float aps = 1f / Mathf.Max(0.0001f, s.attackInterval);
            return new[]
            {
                s.GetHitDamage().ToString("N0"),
                $"{s.critChance * 100f:0}% × {s.critMult:0.0}",
                Loc.F("stat.aspdVal", aps.ToString("0.0")),
                $"{s.cursorRadius:0.00}",
                $"+{s.goldMultPercent:0}%",
                Loc.F("stat.spawnVal", s.spawnIntervalMult.ToString("0.00"), s.spawnCount),
                Loc.F("stat.timeVal", (wave.baseTimeLimit + s.bonusTimeSec).ToString("0")),
                $"{Mathf.Max(1, s.startWave)}",
                s.bombUnlocked
                    ? Loc.F("stat.bombVal", s.bombDmgMul.ToString("0.0#"), s.bombRadiusMul.ToString("0.0"), s.bombInterval.ToString("0"))
                    : Loc.T("stat.bombOff"),
            };
        }

        // 트리 화면 좌측 능력치 패널. 반환값 = 패널이 차지한 폭(트리를 그 오른쪽에 배치하기 위해)
        // 패널 크기·위치를 한 곳에서 계산 — DrawTree 의 트리 배치와 DrawStatPanel 이 같은 값을 쓴다.
        // 우측 상단 고정, 폭은 화면의 30% 이내, 높이는 하단 바 위 공간 안으로 클램프.
        Rect StatPanelRect(float barH)
        {
            int fs0 = Mathf.RoundToInt(14f * uiScale);
            // 좁은 창에서 0.30 은 한글 "공격 속도 / 초당 1.4회" 가 들어가기엔 너무 좁다 — 비율을 키운다
            float w = Mathf.Min(300f * uiScale, Screen.width * (Screen.width < 760 ? 0.44f : 0.30f));
            float rowH0 = Mathf.Max(fs0 * 1.7f, 26f * uiScale);
            float pad0 = 14f * uiScale;
            float h0 = pad0 + rowH0 * (StatLabels.Length + 2.2f) + pad0;
            h0 = Mathf.Min(h0, Mathf.Max(80f, Screen.height - barH - 32f));
            return new Rect(Screen.width - w - 20f, 16f, w, h0);
        }

        void DrawStatPanel(Rect area, UpgradeNode hovered)
        {
            float panelW = area.width;
            var labels = StatLabels;
            var cur = StatValues(stats);
            string[] nxt = null;
            if (hovered != null && !IsBought(hovered.id)) nxt = StatValues(PreviewStats(hovered));

            int fs = Mathf.RoundToInt(14f * uiScale);
            var lab = new GUIStyle(sLabel)
            { alignment = TextAnchor.MiddleLeft, fontSize = fs, normal = { textColor = new Color(0.72f, 0.74f, 0.78f) } };
            FlatText(lab);
            var valS = new GUIStyle(lab)
            { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, normal = { textColor = Ink } };
            FlatText(valS);
            var upS = new GUIStyle(valS) { normal = { textColor = Up } };
            FlatText(upS);
            var head = new GUIStyle(lab)
            { fontSize = Mathf.RoundToInt(16f * uiScale), fontStyle = FontStyle.Bold, normal = { textColor = Ink } };
            FlatText(head);

            float rowH = Mathf.Max(fs * 1.7f, 26f * uiScale);
            float pad = 14f * uiScale;
            float h = pad + rowH * (labels.Length + 2.2f) + pad;

            // StatPanelRect 가 높이를 잘라냈으면(작은 창) 행 높이·패딩도 같은 비율로 줄여
            // 내용이 패널 밖으로 흘러넘치지 않게 한다. 폰트도 같이 축소.
            if (h > area.height && h > 1f)
            {
                float k = area.height / h;
                rowH *= k; pad *= k;
                int fs2 = Mathf.Max(9, Mathf.RoundToInt(fs * k));
                lab.fontSize = valS.fontSize = upS.fontSize = fs2;
                head.fontSize = Mathf.Max(10, Mathf.RoundToInt(head.fontSize * k));
            }
            h = area.height;

            // 폭도 맞춘다 — 예전엔 높이만 줄여서, 창이 좁으면(패널 폭은 Screen.width*0.30) 라벨이
            // 두 줄로 접히며 아랫 행과 글자가 겹쳐 보였다. 라벨+값이 한 줄에 들어가게 폰트를 더 줄이고,
            // 줄바꿈 자체를 꺼서 어떤 경우에도 행이 서로 침범하지 않게 한다.
            lab.wordWrap = valS.wordWrap = upS.wordWrap = head.wordWrap = false;
            float labW = 0f, needW = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                float lw = lab.CalcSize(new GUIContent(labels[i])).x;
                float vw = valS.CalcSize(new GUIContent(cur[i])).x;
                labW = Mathf.Max(labW, lw);
                needW = Mathf.Max(needW, lw + vw);
            }
            float gapMinW = 10f * uiScale;
            float availW = Mathf.Max(1f, area.width - pad * 2f - gapMinW);
            if (needW > availW)
            {
                // 선형 추정으로 한 번에 줄인 뒤, 글리프 폭이 정수라 덜 줄어든 만큼만 1px 씩 더 줄인다
                int fs0 = lab.fontSize;
                int fs3 = Mathf.Clamp(Mathf.RoundToInt(fs0 * (availW / needW)), 8, fs0);
                for (int guard = 0; guard < 12; guard++)
                {
                    lab.fontSize = valS.fontSize = upS.fontSize = fs3;
                    float w2 = 0f;
                    for (int i = 0; i < labels.Length; i++)
                        w2 = Mathf.Max(w2, lab.CalcSize(new GUIContent(labels[i])).x
                                         + valS.CalcSize(new GUIContent(cur[i])).x);
                    if (w2 <= availW || fs3 <= 8) break;
                    fs3--;
                }
                head.fontSize = Mathf.Max(9, Mathf.RoundToInt(head.fontSize * (float)fs3 / fs0));
                labW = 0f;
                for (int i = 0; i < labels.Length; i++)
                    labW = Mathf.Max(labW, lab.CalcSize(new GUIContent(labels[i])).x);
            }
            // 라벨 칸은 실제로 필요한 만큼만(최대 60%) — 나머지는 값이 오른쪽 정렬로 쓴다
            float labFrac = Mathf.Clamp(labW / Mathf.Max(1f, area.width - pad * 2f), 0.30f, 0.60f);

            float x = area.x, y = area.y;

            if (!DrawSkinPanel(new Rect(x, y, panelW, h), false))
            {
                GUI.color = new Color(0.13f, 0.14f, 0.17f, 0.92f);
                GUI.DrawTexture(new Rect(x, y, panelW, h), Texture2D.whiteTexture);
                GUI.color = new Color(0.56f, 0.62f, 0.58f, 0.55f);
                GUI.DrawTexture(new Rect(x, y, panelW, 2f), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            float ix = x + pad, iw = panelW - pad * 2f, iy = y + pad;
            GUI.Label(new Rect(ix, iy, iw, rowH), Loc.T("stat.cur"), head);
            iy += rowH * 1.2f;

            for (int i = 0; i < labels.Length; i++)
            {
                GUI.Label(new Rect(ix, iy, iw * labFrac, rowH), labels[i], lab);
                bool changed = nxt != null && nxt[i] != cur[i];
                if (changed)
                {
                    // "지금 → 바뀔 값" — 바뀔 값만 초록으로
                    var dim = new GUIStyle(valS) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
                    dim.normal.textColor = new Color(0.62f, 0.64f, 0.68f);
                    FlatText(dim);
                    string a = cur[i] + "  →";
                    float aw = dim.CalcSize(new GUIContent(a)).x;
                    float bw = upS.CalcSize(new GUIContent(nxt[i])).x;
                    float right = ix + iw;
                    GUI.Label(new Rect(right - bw, iy, bw, rowH), nxt[i], upS);
                    GUI.Label(new Rect(right - bw - 6f - aw, iy, aw, rowH), a, dim);
                }
                else
                {
                    GUI.Label(new Rect(ix + iw * labFrac, iy, iw * (1f - labFrac), rowH), cur[i], valS);
                }
                iy += rowH;
            }

            var nodeS = new GUIStyle(lab) { alignment = TextAnchor.MiddleRight };
            nodeS.normal.textColor = new Color(0.62f, 0.64f, 0.68f);
            FlatText(nodeS);
            GUI.Label(new Rect(ix, iy, iw * labFrac, rowH), Loc.T("stat.nodes"), lab);
            GUI.Label(new Rect(ix + iw * labFrac, iy, iw * (1f - labFrac), rowH),
                      $"{RegularBoughtCount()} / {RegularNodeCount()}", nodeS);
        }

        int RegularNodeCount() { int c = 0; if (nodes != null) foreach (var n in nodes) if (!n.repeat) c++; return c; }
        int RegularBoughtCount() { int c = 0; if (nodes != null) foreach (var n in nodes) if (!n.repeat && bought.Contains(n.id)) c++; return c; }

        // 라벨 버튼 전부 이걸 통해 그린다 — 클릭음이 한 곳에서 붙도록
        bool UiBtn(Rect r, string label, GUIStyle st)
        {
            LoadUiSkin();
            if (uiBtnN != null)
            {
                bool en = GUI.enabled && transDir == 0;
                bool hover = en && r.Contains(Event.current.mousePosition);
                bool down = hover && Mouse.current != null && Mouse.current.leftButton.isPressed;
                var tex = !GUI.enabled ? (uiBtnD != null ? uiBtnD : uiBtnN)
                        : down ? (uiBtnP != null ? uiBtnP : uiBtnN)
                        : hover ? (uiBtnH != null ? uiBtnH : uiBtnN) : uiBtnN;
                float s = r.height / 72f;   // 텍스처 몸통 높이 72px 가 버튼 높이에 맞게
                if (Event.current.type == EventType.Repaint)
                {
                    var gc = GUI.color; GUI.color = Color.white;
                    // 텍스처는 256x96 에 몸통(240x72)이 (8,6) — 여백만큼 바깥으로 넓혀서 몸통이 r 에 딱 맞게.
                    // 오른쪽 테두리를 80 으로 둬서 세포 점(소기관)이 늘어나지 않고 끝부분에 붙어 있게.
                    DrawNineSlice(new Rect(r.x - 8f * s, r.y - 6f * s, r.width + 16f * s, r.height + 24f * s), tex, 44f, 80f, 34f, 40f, s);
                    GUI.color = gc;
                }
                st = SkinTextStyle(st, down, s);
            }
            if (transDir != 0) { GUI.Button(r, label, st); return false; }   // 전환 중 입력 차단
            if (!GUI.Button(r, label, st)) return false;
            if (sound != null) sound.Play(Sfx.Click, 0.7f);
            return true;
        }

        GUIStyle Btn(int fs)
        {
            var b = new GUIStyle(sBtn) { fontSize = fs };
            b.normal.textColor = Ink;
            FlatText(b);
            return b;
        }

        void OnGUI()
        {
            DrawScreens();
            DrawCheatMsg();
            DrawTransition();   // 눈꺼풀은 항상 맨 위
        }

        void DrawScreens()
        {
            EnsureStyles();

            // 에디터에서 플레이 중 스크립트 재컴파일 → 도메인 리로드로 Awake 산출물이 날아갈 수 있음. 복구.
            if (cam == null) cam = Camera.main;
            if (nodes == null) nodes = UpgradeTree.BuildAll();
            if (iconTex == null) iconTex = NodeIcons.Build();
            if (bossSprites == null) bossSprites = BuildBossSprites();
            if (spriteMat == null) spriteMat = new Material(FindSpriteShader());
            if (gridTile == null) gridTile = BuildGridTile();
            if (bought == null) bought = new HashSet<string>();          // 직렬화 안 됨
            if (splatSprite == null) splatSprite = BuildSplatSprite();
            if (nodeRingTex == null) nodeRingTex = BuildNodeRingTex();
            if (stats == null) stats = new Stats();
            if (wave == null) wave = new WaveConfig();
            if (cam == null) return;

            switch (state)
            {
                case State.Title:    DrawTitle();    return;
                case State.Map:      DrawMap();      return;
                case State.Meta:     DrawMeta();     return;
                case State.Settings: DrawSettings(); return;
                case State.Result:   DrawResult();   return;
                case State.Tree:     DrawTree();     return;
                case State.Win:      DrawHUD(); DrawGameplayOverlays(); DrawWin(); return;
            }

            if (state == State.Paused)
            {
                // 일시정지 배경 = ESC(또는 톱니바퀴)를 누른 화면.
                // GUI.enabled=false 로 그려서 뒤 화면의 버튼이 딤 너머로 눌리는 일이 없게 한다.
                bool ge = GUI.enabled; GUI.enabled = false;
                if (pauseReturn == State.Tree) DrawTree();
                else if (pauseReturn == State.Result) DrawResult();
                else if (pauseReturn == State.Map) DrawMap();      // 지도에서 열었으면 지도가 배경
                else { DrawHUD(); DrawGameplayOverlays(); }
                GUI.enabled = ge;
                DrawPause();
                return;
            }

            DrawHUD();
            DrawGameplayOverlays();
        }

        // ---- 눈꺼풀 전환 (현미경에 눈을 가져다 대는 연출) ----
        //   닫힘(위·아래 검은 눈꺼풀이 중앙으로) → 화면 전환 → 열림. 각 TransDur 초.
        float transT;                 // 0..1 현재 구간 진행도
        int transDir;                 // -1 닫히는 중, +1 열리는 중, 0 없음
        System.Action transAction;    // 완전히 닫힌 순간 실행할 전환
        const float TransDur = 0.34f;

        public bool InTransition => transDir != 0;

        void BeginTransition(System.Action onClosed)
        {
            if (transDir != 0) return;   // 이미 진행 중이면 무시
            transAction = onClosed;
            transDir = -1;
            transT = 0f;
        }

        void UpdateTransition(float dt)
        {
            if (transDir == 0) return;
            transT += dt / TransDur;
            if (transT < 1f) return;
            transT = 0f;
            if (transDir < 0)
            {
                var a = transAction; transAction = null;
                if (a != null) a();      // 눈을 감은 사이에 화면을 바꾼다
                transDir = 1;
            }
            else transDir = 0;
        }

        void DrawTransition()
        {
            if (transDir == 0) return;
            float cover = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transDir < 0 ? transT : 1f - transT));
            if (cover <= 0.001f) return;

            float w = Screen.width, h = Screen.height;
            float lid = h * 0.5f * cover;
            var gc = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.04f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, w, lid), Texture2D.whiteTexture);          // 위 눈꺼풀
            GUI.DrawTexture(new Rect(0f, h - lid, w, lid), Texture2D.whiteTexture);     // 아래 눈꺼풀
            // 경계를 살짝 흐려 눈꺼풀처럼
            float soft = 12f * uiScale;
            GUI.color = new Color(0.02f, 0.03f, 0.04f, 0.45f);
            GUI.DrawTexture(new Rect(0f, lid, w, soft), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, h - lid - soft, w, soft), Texture2D.whiteTexture);
            GUI.color = gc;
        }

        // ============================================================
        // round44: Figma UI 스킨 — Resources/UI/*.png (Figma 'POPCell_UIKit' 시트에서 잘라 넣음).
        //   파일이 없으면 전부 예전 단색 그리기로 돌아간다(깨지지 않게).
        // ============================================================
        Texture2D uiBtnN, uiBtnH, uiBtnP, uiBtnD, uiPanel, uiPanelGold, uiLogo, uiBg;
        bool uiSkinLoaded;
        static readonly Color SkinInk = new Color(0.04f, 0.20f, 0.21f);   // 민트 버튼 위 글자(진한 청록)

        void LoadUiSkin()
        {
            if (uiSkinLoaded) return;
            uiSkinLoaded = true;
            uiBtnN = Resources.Load<Texture2D>("UI/btn_normal");
            uiBtnH = Resources.Load<Texture2D>("UI/btn_hover");
            uiBtnP = Resources.Load<Texture2D>("UI/btn_pressed");
            uiBtnD = Resources.Load<Texture2D>("UI/btn_disabled");
            uiPanel = Resources.Load<Texture2D>("UI/panel_dark");
            uiPanelGold = Resources.Load<Texture2D>("UI/panel_gold");
            uiLogo = Resources.Load<Texture2D>("UI/logo");
            uiBg = Resources.Load<Texture2D>("UI/bg_glass");
        }

        // 9-slice — L/R/T/B 는 텍스처 픽셀, s = 화면 픽셀 / 텍스처 픽셀. 모서리는 늘어나지 않는다.
        static void DrawNineSlice(Rect dst, Texture2D t, float L, float R, float T, float B, float s)
        {
            float l = L * s, r = R * s, tp = T * s, b = B * s;
            if (l + r > dst.width)  { float k = dst.width / (l + r);  l *= k; r *= k; }
            if (tp + b > dst.height) { float k = dst.height / (tp + b); tp *= k; b *= k; }
            float W = t.width, H = t.height;
            float[] xs = { dst.x, dst.x + l, dst.xMax - r, dst.xMax };
            float[] ys = { dst.y, dst.y + tp, dst.yMax - b, dst.yMax };
            float[] us = { 0f, L / W, 1f - R / W, 1f };
            float[] vs = { 1f, 1f - T / H, B / H, 0f };   // uv 는 아래가 0
            for (int iy = 0; iy < 3; iy++)
                for (int ix = 0; ix < 3; ix++)
                {
                    var rr = Rect.MinMaxRect(xs[ix], ys[iy], xs[ix + 1], ys[iy + 1]);
                    if (rr.width <= 0.01f || rr.height <= 0.01f) continue;
                    GUI.DrawTextureWithTexCoords(rr, t, Rect.MinMaxRect(us[ix], vs[iy + 1], us[ix + 1], vs[iy]));
                }
        }

        // 어두운 세포막 패널(gold = 반복 강화 창). 스킨이 없으면 false — 호출한 쪽이 예전 방식으로 그린다.
        bool DrawSkinPanel(Rect r, bool gold)
        {
            LoadUiSkin();
            var t = gold ? (uiPanelGold != null ? uiPanelGold : uiPanel) : uiPanel;
            if (t == null) return false;
            float sc = Mathf.Clamp(uiScale * 0.7f, 0.5f, 1.2f);
            float m = 4f * sc;   // 텍스처 가장자리 여백(몸통은 4px 안쪽부터)
            var gc = GUI.color; GUI.color = Color.white;
            DrawNineSlice(new Rect(r.x - m, r.y - m, r.width + m * 2f, r.height + m * 2f), t, 26f, 26f, 26f, 26f, sc);
            GUI.color = gc;
            return true;
        }

        bool DrawSkinBackground()
        {
            LoadUiSkin();
            if (uiBg == null) return false;
            var gc = GUI.color; GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), uiBg, ScaleMode.ScaleAndCrop);
            GUI.color = gc;
            return true;
        }

        // 스킨 버튼 위에 얹을 글자 전용 스타일 — 배경은 비우고 글자만 진한 청록
        static GUIStyle SkinTextStyle(GUIStyle st, bool down, float s)
        {
            var t = new GUIStyle(st);
            t.normal.background = t.hover.background = t.active.background = t.focused.background = null;
            t.onNormal.background = t.onHover.background = t.onActive.background = t.onFocused.background = null;
            t.normal.textColor = t.hover.textColor = t.active.textColor = t.focused.textColor = SkinInk;
            t.onNormal.textColor = t.onHover.textColor = t.onActive.textColor = t.onFocused.textColor = SkinInk;
            t.fontStyle = FontStyle.Bold;
            t.contentOffset = new Vector2(0f, (down ? 1f : -2f) * s);   // 눌리면 글자도 같이 내려간다
            return t;
        }

        void FillScreen(Color col)
        {
            var c = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }

        void DrawGridOverlay(Color dot)
        {
            var c = GUI.color;
            GUI.color = dot;
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, Screen.width, Screen.height), gridTile, new Rect(0, 0, 16, 16));
            GUI.color = c;
        }

        void DrawHUD()
        {
            float s = uiScale;
            bool bossWave = boss != null;

            // 전장이 어두운 감염지로 바뀌어(round35) HUD 글자는 밝은 색으로 — 어두운 바닥 위에서 읽히게
            var hGold = new GUIStyle(sGold) { normal = { textColor = Gold } }; FlatText(hGold);
            var hCen  = new GUIStyle(sCenter) { normal = { textColor = Ink } }; FlatText(hCen);

            GUI.Label(new Rect(14f * s, 10f * s, 520f * s, 32f * s), $"$ {gold:N0}", hGold);
            GUI.Label(new Rect(0, 8f * s, Screen.width, 30f * s),
                bossWave ? Loc.T("hud.boss") : Loc.F("hud.wave", waveNum, wave.totalWaves), hCen);

            var tStyle = new GUIStyle(sBig)
            { normal = { textColor = timeLeft <= 5f ? new Color(1f, 0.34f, 0.30f) : Ink } };
            FlatText(tStyle);
            GUI.Label(new Rect(0, 30f * s, Screen.width, 44f * s), timeLeft.ToString("0.0"), tStyle);

            if (!bossWave)
                GUI.Label(new Rect(0, 72f * s, Screen.width, 28f * s), $"{kills} / {quota}", hCen);
            else
            {
                // 보스 체력 바 (화면 상단 가로)
                float bw = Screen.width * 0.6f, bx = Screen.width * 0.2f, by = 66f * s, bh = 20f * s;
                float ratio = Mathf.Clamp01(boss.hp / boss.hpMax);
                var gc = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(bx, by, bw, bh), Texture2D.whiteTexture);
                GUI.color = new Color(0.85f, 0.15f, 0.18f, 1f);
                GUI.DrawTexture(new Rect(bx, by, bw * ratio, bh), Texture2D.whiteTexture);
                GUI.color = gc;
                var bs = new GUIStyle(sCenter) { fontSize = Mathf.RoundToInt(14f * uiScale), fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(bx, by - 2f, bw, bh + 4f), $"{Mathf.CeilToInt(boss.hp):N0} / {boss.hpMax:N0}", bs);
            }
        }

        // 텍스트 크기에 딱 맞는 rect (가운데 정렬) — 자릿수 늘어도 안 잘림
        static Rect FitRect(GUIStyle st, string txt, Vector2 center)
        {
            Vector2 sz = st.CalcSize(new GUIContent(txt));
            return new Rect(center.x - sz.x * 0.5f - 1f, center.y - sz.y * 0.5f - 1f, sz.x + 4f, sz.y + 4f);
        }

        void DrawGameplayOverlays()
        {
            float s = uiScale;

            foreach (var f in floaters)
            {
                Vector3 sp = cam.WorldToScreenPoint(f.world);
                if (sp.z < 0f) continue;
                float a = Mathf.Clamp01(f.life / 0.55f);
                var fs = new GUIStyle(sLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt((f.crit ? 20f : 14f) * uiScale),
                    fontStyle = f.crit ? FontStyle.Bold : FontStyle.Normal,
                    normal = { textColor = f.crit ? new Color(1f, 0.78f, 0.26f, a) : new Color(0.90f, 0.93f, 0.95f, a) }
                };
                FlatText(fs);
                GUI.Label(FitRect(fs, f.text, new Vector2(sp.x, Screen.height - sp.y - 14f * uiScale)), f.text, fs);
            }

            // $ 드랍 — 죽은 위치 정중앙에서 떠오르다 좌상단 총액으로 날아가 사라짐
            Vector2 anchor = new Vector2(14f * s + 8f, 10f * s + 14f);
            foreach (var gf in goldFloats)
            {
                Vector3 sp = cam.WorldToScreenPoint(gf.startWorld);
                if (sp.z < 0f) continue;
                Vector2 from = new Vector2(sp.x, Screen.height - sp.y);
                float k = gf.age / gf.life;
                Vector2 p; float alpha; float fsz;
                if (k < 0.35f)
                {
                    p = from + new Vector2(0f, -34f * (k / 0.35f));
                    alpha = 1f; fsz = 16f;
                }
                else
                {
                    float u = (k - 0.35f) / 0.65f;
                    u = u * u * (3f - 2f * u);
                    p = Vector2.Lerp(from + new Vector2(0f, -34f), anchor, u);
                    alpha = 1f - u; fsz = Mathf.Lerp(16f, 10f, u);
                }
                var gs = new GUIStyle(sLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(fsz * uiScale),
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(Gold.r, Gold.g, Gold.b, alpha) }   // 어두운 감염지 위 — 밝은 노랑
                };
                FlatText(gs);   // 마우스 오버해도 흰색으로 안 바뀜
                string gtxt = $"+${gf.amount:N0}";
                GUI.Label(FitRect(gs, gtxt, p), gtxt, gs);
            }

            // 적 체력 — 맞은 적만 (보스는 상단 큰 바로 따로 표시)
            float pxPerUnit = Screen.height / (cam.orthographicSize * 2f);
            foreach (var e in enemies)
            {
                if (e == boss || e.hp >= e.hpMax) continue;
                Vector3 esp = cam.WorldToScreenPoint(e.tr.position);
                if (esp.z < 0f) continue;
                float ey = Screen.height - esp.y;
                float rPix = e.r * pxPerUnit;
                float ratio = Mathf.Clamp01(e.hp / e.hpMax);

                float bw = Mathf.Max(rPix * 2f, 12f);
                float bh = Mathf.Max(3f, rPix * 0.18f);
                float bx = esp.x - bw * 0.5f;
                float by = ey - rPix - bh - 3f;
                var gcPrev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(bx, by, bw, bh), Texture2D.whiteTexture);
                GUI.color = Color.Lerp(new Color(0.88f, 0.28f, 0.26f), new Color(0.30f, 0.78f, 0.42f), ratio);
                GUI.DrawTexture(new Rect(bx, by, bw * ratio, bh), Texture2D.whiteTexture);
                GUI.color = gcPrev;

                // round39: 가운데 남은 체력 숫자가 (특히 작은 종의) 몹 그림을 완전히 가려서 제거 —
                // 체력바만으로 충분히 보인다.
            }
        }

        void DrawPause()
        {
            FillScreen(new Color(0.13f, 0.14f, 0.16f, 0.86f)); // 어두운 반투명 (뒤 화면 살짝 비침)
            float w = Screen.width, h = Screen.height;
            bool fromPlay = pauseReturn == State.Playing;

            var title = new GUIStyle(sBig) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(28f * uiScale) };
            GUI.Label(new Rect(0, h * 0.20f, w, 72f), fromPlay ? Loc.T("pause.title") : Loc.T("pause.menu"), title);

            float bw = 260f, bh = 52f, gap = 12f, by = h * 0.40f;
            var bst = Btn(16);
            int row = 0;
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                Loc.T("pause.resume"), bst)) state = pauseReturn;
            if (fromPlay && UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                Loc.T("pause.endWave"), bst)) state = State.Result;
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                Loc.T("title.settings"), bst)) { settingsReturn = State.Paused; state = State.Settings; }
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                Loc.T("common.toTitle"), bst)) GoToTitle();
        }

        // ---- 지역(스테이지) 선택 — 좌우 캐러셀. 가운데 카드에 그 지역 보스 그림자. ----
        void DrawMap()
        {
            if (!DrawSkinBackground()) FillScreen(Glass);
            DrawGridOverlay(GlassGrid);
            float w = Screen.width, h = Screen.height, s = uiScale;
            int n = StageConfig.Stages.Length;

            if (mapScroll < 0f)
            { mapIndex = Mathf.Clamp(Mathf.Min(stagesCleared, n - 1), 0, n - 1); mapScroll = mapIndex; }
            mapIndex = Mathf.Clamp(mapIndex, 0, n - 1);

            var tt = new GUIStyle(sBig) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(32f * s), normal = { textColor = InkDark } };
            FlatText(tt);
            GUI.Label(new Rect(0, h * 0.045f, w, 50f * s), Loc.T("map.title"), tt);

            var sub = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(13f * s), normal = { textColor = new Color(0.20f, 0.24f, 0.28f) } };
            FlatText(sub);
            string infoTxt = ascensionLevel > 0
                ? Loc.F("map.infoAsc", gold.ToString("N0"), retryCount, stagesCleared, n, ascensionLevel)
                : Loc.F("map.info", gold.ToString("N0"), retryCount, stagesCleared, n);
            GUI.Label(new Rect(0, h * 0.045f + 38f * s, w, 24f * s), infoTxt, sub);

            // ---- 우측 상단 설정(톱니바퀴) — 누르면 계속하기/설정/타이틀로 메뉴 ----
            {
                float gs = Mathf.Round(42f * s);
                var gr = new Rect(w - gs - 22f * s, 18f * s, gs, gs);
                bool gHover = gr.Contains(Event.current.mousePosition);
                var gc0 = GUI.color;
                GUI.color = new Color(0.32f, 0.36f, 0.40f, gHover ? 0.22f : 0.12f);
                GUI.DrawTexture(gr, Texture2D.whiteTexture);
                GUI.color = gHover ? InkDark : new Color(0.26f, 0.30f, 0.34f, 0.85f);
                if (iconTex != null && iconTex.TryGetValue("gear", out var gtex) && gtex != null)
                {
                    float ins = gs * 0.16f;
                    GUI.DrawTexture(new Rect(gr.x + ins, gr.y + ins, gr.width - ins * 2f, gr.height - ins * 2f), gtex);
                }
                GUI.color = gc0;
                // 아이콘 위에 투명 버튼을 겹쳐서 클릭/사운드는 공용 UiBtn 규칙을 그대로 따른다
                if (UiBtn(gr, GUIContent.none.text, GUIStyle.none))
                { pauseReturn = State.Map; state = State.Paused; }
            }

            // ---- 캐러셀 ----
            float cardW = Mathf.Min(300f * s, w * 0.40f);
            float cardH = Mathf.Min(cardW * 1.15f, h * 0.46f);
            float slot = cardW * 1.16f;
            float cyMid = h * 0.50f;
            float cxMid = w * 0.5f;

            var ev = Event.current;
            if (ev.type == EventType.ScrollWheel)
            { mapIndex = Mathf.Clamp(mapIndex + (ev.delta.y > 0f ? 1 : -1), 0, n - 1); ev.Use(); }

            if (!Mathf.Approximately(mapScroll, mapIndex))
                mapScroll = Mathf.MoveTowards(mapScroll, mapIndex, Mathf.Max(0.05f, Time.deltaTime * 9f));

            // 먼 카드부터 그려서 가운데 카드가 위에 오도록
            var order = new List<int>();
            for (int i = 0; i < n; i++) if (Mathf.Abs(i - mapScroll) <= 2.3f) order.Add(i);
            order.Sort((x, y2) => Mathf.Abs(y2 - mapScroll).CompareTo(Mathf.Abs(x - mapScroll)));

            foreach (int i in order)
            {
                float rel = i - mapScroll;
                float k = Mathf.Clamp01(1f - Mathf.Abs(rel) * 0.34f);
                float cw = cardW * (0.62f + 0.38f * k), ch = cardH * (0.62f + 0.38f * k);
                var r = new Rect(cxMid + rel * slot - cw * 0.5f, cyMid - ch * 0.5f, cw, ch);
                bool center = Mathf.Abs(rel) < 0.5f;
                DrawStageCard(r, i, center, k);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && transDir == 0)
                {
                    if (center && i <= stagesCleared)
                    { currentStage = i; if (sound != null) sound.Play(Sfx.Click, 0.7f); BeginTransition(StartRun); }
                    else { mapIndex = i; if (sound != null) sound.Play(Sfx.Click, 0.55f); }
                }
            }

            // 좌우 화살표
            var arrow = new GUIStyle(sBig) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(34f * s), normal = { textColor = InkDark } };
            FlatText(arrow);
            float aw = 54f * s, ah = 66f * s;
            if (mapIndex > 0 && UiBtn(new Rect(Mathf.Max(6f, w * 0.02f), cyMid - ah * 0.5f, aw, ah), "<", arrow))
                mapIndex--;
            if (mapIndex < n - 1 && UiBtn(new Rect(Mathf.Min(w - aw - 6f, w * 0.98f - aw), cyMid - ah * 0.5f, aw, ah), ">", arrow))
                mapIndex++;

            // 페이지 점
            float dotY = Mathf.Min(cyMid + cardH * 0.60f, h * 0.80f);
            float dgap = 16f * s, dtot = (n - 1) * dgap;
            for (int i = 0; i < n; i++)
            {
                var dc = GUI.color;
                bool on = i == mapIndex;
                GUI.color = on ? InkDark : new Color(0.40f, 0.44f, 0.48f, 0.5f);
                float dd = (on ? 9f : 6f) * s;
                GUI.DrawTexture(new Rect(cxMid - dtot * 0.5f + i * dgap - dd * 0.5f, dotY, dd, dd), Texture2D.whiteTexture);
                GUI.color = dc;
            }

            // ---- 하단 버튼 ----
            var bst = Btn(Mathf.RoundToInt(13f * s));
            bst.normal.textColor = InkDark; FlatText(bst);
            float bw = Mathf.Min(180f * s, w * 0.28f), bh = 44f * s, bgap = 12f * s;
            bool everUnlocked = everRebirth;   // 한 번이라도 열렸으면 환생해서 리셋돼도 계속 보임
            bool canRebirth = stagesCleared >= 2;   // 지금 이 루프에서 실제로 누를 수 있는지
            int nbtn = everUnlocked ? 3 : 1;
            float totw = nbtn * bw + (nbtn - 1) * bgap;
            float bx = (w - totw) * 0.5f;
            float byy = Mathf.Min(h * 0.87f, h - bh - 34f * s);
            if (UiBtn(new Rect(bx, byy, bw, bh), Loc.T("map.upgrade"), bst))
            { state = State.Tree; treeZoom = 0f; treePan = Vector2.zero; treeFromResult = false; }
            if (everUnlocked)
            {
                bx += bw + bgap;
                // 메타 강화는 환생을 한 번이라도 열었으면 항상 사용 가능(진행 리셋과 무관)
                if (UiBtn(new Rect(bx, byy, bw, bh), metaCurrency > 0 ? Loc.F("map.metaN", metaCurrency) : Loc.T("map.meta"), bst))
                    state = State.Meta;
                bx += bw + bgap;
                var rbRect = new Rect(bx, byy, bw, bh);
                if (canRebirth)
                {
                    if (UiBtn(rbRect, Loc.F("map.rebirth", ShardGainNow()), bst))
                        BeginTransition(Prestige);
                    GUI.Label(new Rect(0, byy + bh + 3f * s, w, 18f * s),
                        Loc.F("map.rebirthInfo", ShardGainNow(), metaCurrency, ascensionLevel, ascensionLevel + 1), sub);
                }
                else
                {
                    // 버튼은 계속 보이되 이번 루프에서 2지역을 깨기 전까진 눌리지 않고 안내 문구만
                    var gc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    GUI.Box(rbRect, Loc.T("map.rebirthLock"), bst);
                    GUI.color = gc;
                }
            }
        }

        void DrawStageCard(Rect r, int i, bool center, float k)
        {
            var st = StageConfig.Stages[i];
            float s = uiScale;
            bool unlocked = i <= stagesCleared;
            bool cleared = i < stagesCleared;
            bool bossSeen = unlocked && (bossSeenMask & (1 << i)) != 0;
            var gc = GUI.color;

            GUI.color = unlocked ? new Color(st.theme.r, st.theme.g, st.theme.b, cleared ? 0.55f : 0.92f)
                                 : new Color(0.50f, 0.52f, 0.55f, 0.5f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);

            GUI.color = center ? new Color(1f, 1f, 1f, 0.95f) : new Color(0f, 0f, 0f, 0.22f);
            float bt = center ? 4f : 2f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, bt), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - bt, r.width, bt), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, bt, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - bt, r.y, bt, r.height), Texture2D.whiteTexture);
            GUI.color = gc;

            // 보스 그림자 / 살아있는 보스
            if (bossSprites != null && i < bossSprites.Length && bossSprites[i] != null && bossSprites[i].texture != null)
            {
                var texB = bossSprites[i].texture;
                float img = Mathf.Min(r.width, r.height) * 0.60f;
                var ir = new Rect(r.center.x - img * 0.5f, r.center.y - img * 0.42f, img, img);
                if (bossSeen)
                {
                    Matrix4x4 m0 = GUI.matrix;
                    float wob = Mathf.Sin(Time.time * 2.2f + i * 1.3f) * 4.5f;
                    GUIUtility.RotateAroundPivot(wob, ir.center);
                    float sc = 1f + 0.05f * Mathf.Sin(Time.time * 3.3f + i * 2f);
                    var ir2 = new Rect(ir.center.x - ir.width * sc * 0.5f, ir.center.y - ir.height * sc * 0.5f,
                                       ir.width * sc, ir.height * sc);
                    GUI.color = new Color(Mathf.Min(1f, st.theme.r * 1.15f + 0.12f), st.theme.g * 0.45f, st.theme.b * 0.45f, 0.96f);
                    GUI.DrawTexture(ir2, texB, ScaleMode.ScaleToFit, true);
                    GUI.matrix = m0;
                }
                else
                {
                    GUI.color = new Color(0f, 0f, 0f, unlocked ? 0.48f : 0.30f);
                    GUI.DrawTexture(ir, texB, ScaleMode.ScaleToFit, true);
                }
                GUI.color = gc;
            }

            // 이름 (클립 방지: CalcHeight 로 필요한 높이 확보)
            var nm = new GUIStyle(sLabel) { alignment = TextAnchor.UpperCenter, wordWrap = true,
                fontSize = Mathf.RoundToInt((center ? 15f : 12f) * s), fontStyle = FontStyle.Bold,
                normal = { textColor = unlocked ? Color.white : new Color(0.86f, 0.87f, 0.89f) } };
            FlatText(nm);
            string nmTxt = (i + 1) + ". " + (unlocked ? Loc.T(st.name) : "???");
            float innerW = r.width - 12f * s;
            float nmH = nm.CalcHeight(new GUIContent(nmTxt), innerW);
            GUI.Label(new Rect(r.x + 6f * s, r.y + 7f * s, innerW, Mathf.Min(nmH + 2f, r.height * 0.5f)), nmTxt, nm);

            var meta = new GUIStyle(sLabel) { alignment = TextAnchor.LowerCenter, wordWrap = true,
                fontSize = Mathf.RoundToInt(11f * s),
                normal = { textColor = unlocked ? new Color(0.96f, 0.97f, 0.99f) : new Color(0.80f, 0.82f, 0.85f) } };
            FlatText(meta);
            string mTxt = !unlocked ? Loc.T("map.locked")
                        : cleared ? Loc.T("map.cleared")
                        : Loc.F("map.wavesBoss", st.waves);
            GUI.Label(new Rect(r.x + 6f * s, r.yMax - 32f * s, innerW, 28f * s), mTxt, meta);
        }

        // ---- 메타(환생) 강화 화면 ----
        void DrawMeta()
        {
            FillScreen(new Color(0.18f, 0.16f, 0.22f, 1f));
            DrawGridOverlay(new Color(0.30f, 0.26f, 0.36f, 1f));
            float w = Screen.width, h = Screen.height, s = uiScale;

            var tt = new GUIStyle(sBig) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(30f * s) };
            FlatText(tt);
            GUI.Label(new Rect(0, h * 0.06f, w, 52f * s), Loc.T("meta.title"), tt);
            var sub2 = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(13f * s), normal = { textColor = Gold } };
            FlatText(sub2);
            GUI.Label(new Rect(0, h * 0.06f + 42f * s, w, 24f * s), Loc.F("meta.shard", metaCurrency), sub2);

            float rowH = 56f * s, listW = Mathf.Min(620f * s, w * 0.9f);
            float lx = (w - listW) * 0.5f, ly = h * 0.20f;
            var name = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(15f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var desc = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(12f * s), normal = { textColor = new Color(0.72f,0.70f,0.78f) }, alignment = TextAnchor.MiddleLeft };
            FlatText(name); FlatText(desc);
            var bst = Btn(Mathf.RoundToInt(12f * s));
            for (int i = 0; i < metaNodes.Count; i++)
            {
                var m = metaNodes[i];
                bool locked = maxAscensionUnlocked < m.unlockAsc;
                int lv = MetaLv(m.id);
                int maxLv = MetaMaxLvAt(m);
                var r = new Rect(lx, ly + i * (rowH + 8f * s), listW, rowH);
                var gc = GUI.color;
                GUI.color = locked ? new Color(0.14f, 0.13f, 0.17f, 0.92f) : new Color(0.24f, 0.22f, 0.30f, 0.92f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = gc;
                var nameS = locked ? new GUIStyle(name) { normal = { textColor = new Color(0.55f, 0.54f, 0.60f) } } : name;
                FlatText(nameS);
                GUI.Label(new Rect(r.x + 14f * s, r.y + 6f * s, listW * 0.62f, 22f * s), Loc.F("meta.lv", Loc.T(m.label), lv, maxLv), nameS);
                GUI.Label(new Rect(r.x + 14f * s, r.y + 28f * s, listW * 0.62f, 20f * s),
                    locked ? Loc.F("meta.lockAsc", m.unlockAsc) : Loc.T(m.desc), desc);
                var br = new Rect(r.xMax - 130f * s, r.y + 8f * s, 116f * s, rowH - 16f * s);
                if (locked)
                {
                    var gc3 = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.20f);
                    GUI.Box(br, GUIContent.none, bst);   // 잠김 — 글자 없이 빈 칩만(텍스트는 위 desc 줄에)
                    GUI.color = gc3;
                    continue;
                }
                bool maxed = lv >= maxLv;
                int cost = MetaCostAt(m, lv);
                bool afford = metaCurrency >= cost;
                string btxt = maxed ? Loc.T("meta.max") : Loc.F("meta.cost", cost);
                if (!maxed && afford)
                {
                    if (UiBtn(br, btxt, bst)) BuyMeta(m);
                }
                else
                {
                    var gc2 = GUI.color; GUI.color = new Color(1f,1f,1f,0.4f);
                    GUI.Box(br, btxt); GUI.color = gc2;
                }
            }
            var back = Btn(Mathf.RoundToInt(14f * s));
            if (UiBtn(new Rect(w * 0.5f - 110f * s, h * 0.90f, 220f * s, 44f * s), Loc.T("pause.return"), back))
                state = State.Map;
        }

        void DrawTitle()
        {
            if (!DrawSkinBackground()) FillScreen(Glass);
            DrawGridOverlay(GlassGrid);
            float w = Screen.width, h = Screen.height;

            var tt = new GUIStyle(sBig)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(64f * uiScale),
              normal = { textColor = InkDark } };
            FlatText(tt);
            LoadUiSkin();
            if (uiLogo != null)
            {
                // Figma 로고(글자+거품). 원래 글자 칸(높이 90*uiScale) 가운데에 맞춰 조금 크게.
                float lh = 150f * uiScale, lw = lh * uiLogo.width / Mathf.Max(1f, uiLogo.height);
                float cy = h * 0.13f + 45f * uiScale;
                var gcL = GUI.color; GUI.color = Color.white;
                GUI.DrawTexture(new Rect(w * 0.5f - lw * 0.5f, cy - lh * 0.5f, lw, lh), uiLogo, ScaleMode.ScaleToFit);
                GUI.color = gcL;
            }
            else GUI.Label(new Rect(0, h * 0.13f, w, Mathf.RoundToInt(90f * uiScale)), "POP Cell", tt);
            var sub = new GUIStyle(sLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15f * uiScale),
              normal = { textColor = new Color(0.16f, 0.20f, 0.24f) } };
            FlatText(sub);
            GUI.Label(new Rect(0, h * 0.13f + 92f * uiScale, w, 34f * uiScale), Loc.T("title.sub"), sub);

            // ---- 우측 상단 언어 드롭다운 (설정창에서 이 자리로 옮김, round37) ----
            // 닫혀 있을 땐 현재 언어만, 누르면 3개가 펼쳐지고 고르면 바로 적용된다.
            {
                float lw = Mathf.Min(150f * uiScale, w * 0.22f), lh = 34f * uiScale;
                float lx = w - lw - 22f * uiScale, ly = 18f * uiScale;
                var lst = Btn(Mathf.RoundToInt(14f * uiScale));
                lst.normal.textColor = InkDark; FlatText(lst);
                // 펼친 목록이 먼저 클릭을 받도록, 열려 있으면 목록을 "나중에" 그린다(IMGUI 는 먼저 그린 쪽이 우선)
                if (langOpen)
                {
                    for (int li = 0; li < Loc.LangNames.Length; li++)
                    {
                        bool sel = (int)Loc.Cur == li;
                        var ist = Btn(Mathf.RoundToInt(14f * uiScale));
                        ist.normal.textColor = sel ? new Color(0.12f, 0.42f, 0.22f) : InkDark;
                        FlatText(ist);
                        var ir = new Rect(lx, ly + (lh + 4f * uiScale) * (li + 1), lw, lh);
                        if (UiBtn(ir, (sel ? "▶ " : "   ") + Loc.LangNames[li], ist))
                        { Loc.SetLang((Lang)li); langOpen = false; }
                    }
                }
                if (UiBtn(new Rect(lx, ly, lw, lh), Loc.LangNames[(int)Loc.Cur] + (langOpen ? "  ▲" : "  ▼"), lst))
                    langOpen = !langOpen;
                // 목록 밖을 누르면 닫힌다
                if (langOpen && Event.current.type == EventType.MouseDown)
                {
                    var box = new Rect(lx, ly, lw, lh + (lh + 4f * uiScale) * Loc.LangNames.Length);
                    if (!box.Contains(Event.current.mousePosition)) langOpen = false;
                }
            }

            // 승천 난이도 선택 — 한 번이라도 승천을 해봤으면(8지역 클리어) 타이틀에서 미리 고르고 플레이 시작.
            // 그 전엔 통째로 안 보임(버튼 위치는 기본값 h*0.56 그대로). 보일 때는 실제로 그린 높이만큼
            // 버튼 줄을 아래로 밀어서 겹치지 않게 한다(해상도/uiScale 달라져도 안전하도록 계산으로).
            float titleButtonsY = h * 0.56f;
            if (maxAscensionUnlocked > 0)
            {
                float selY = h * 0.34f;
                var selLbl = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(14f * uiScale), normal = { textColor = new Color(0.30f, 0.34f, 0.38f) } };
                FlatText(selLbl);
                GUI.Label(new Rect(0, selY, w, 22f * uiScale), Loc.T("title.ascendLevel"), selLbl);

                var arrow = new GUIStyle(sBig) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(22f * uiScale), normal = { textColor = InkDark } };
                FlatText(arrow);
                var lvlS = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(22f * uiScale), fontStyle = FontStyle.Bold, normal = { textColor = InkDark } };
                FlatText(lvlS);
                float aw = 40f * uiScale, ah = 38f * uiScale, lvlW = 80f * uiScale;
                float cx0 = w * 0.5f, arrY = selY + 26f * uiScale;
                if (UiBtn(new Rect(cx0 - lvlW * 0.5f - aw - 8f, arrY, aw, ah), "<", arrow) && ascensionLevel > 0)
                { ascensionLevel--; wave.LoadStage(0); wave.ApplyAscension(ascensionLevel); }
                GUI.Label(new Rect(cx0 - lvlW * 0.5f, arrY, lvlW, ah), ascensionLevel.ToString(), lvlS);
                if (UiBtn(new Rect(cx0 + lvlW * 0.5f + 8f, arrY, aw, ah), ">", arrow) && ascensionLevel < maxAscensionUnlocked)
                { ascensionLevel++; wave.LoadStage(0); wave.ApplyAscension(ascensionLevel); }

                // 이 난이도를 고르면 실제로 뭐가 바뀌는지 — 수치로 바로 보여준다.
                float mobMul = Mathf.Pow(WaveConfig.AscMobHpMul, ascensionLevel);
                float bossMul = Mathf.Pow(WaveConfig.AscBossHpMul, ascensionLevel);
                float goldMul = Mathf.Pow(WaveConfig.AscGoldMul, ascensionLevel);
                float descY = arrY + ah + 8f * uiScale;
                var descS = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(12.5f * uiScale), fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.30f, 0.34f, 0.38f) } };
                FlatText(descS);
                GUI.Label(new Rect(0, descY, w, 20f * uiScale),
                    Loc.F("title.ascendDesc", mobMul.ToString("0.00"), bossMul.ToString("0.00"), goldMul.ToString("0.00")), descS);

                // 이 난이도에서 켜지는 보스 패턴 — "그냥 더 세짐"이 아니라 "더 까다로워짐"을 미리 알려준다.
                var patParts = new List<string>();
                if (ascensionLevel >= 1) patParts.Add(Loc.T("title.patCrit"));
                if (ascensionLevel >= 2) patParts.Add(Loc.T("title.patTeleport"));
                if (ascensionLevel >= 3) patParts.Add(Loc.T("title.patShield"));
                string patText = patParts.Count > 0
                    ? Loc.F("title.patPrefix", string.Join(" · ", patParts))
                    : Loc.T("title.patNone");
                float patY = descY + 20f * uiScale + 2f * uiScale;
                var patS = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(12f * uiScale), fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.18f, 0.16f) } };
                FlatText(patS);
                GUI.Label(new Rect(0, patY, w, 20f * uiScale), patText, patS);

                float noteY = patY + 20f * uiScale + 2f * uiScale;
                var noteS = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(11f * uiScale), normal = { textColor = new Color(0.42f, 0.46f, 0.50f) } };
                FlatText(noteS);
                GUI.Label(new Rect(0, noteY, w, 18f * uiScale), Loc.T("title.ascendNote"), noteS);

                titleButtonsY = Mathf.Max(titleButtonsY, noteY + 18f * uiScale + 20f * uiScale);
            }

            // 버튼: 기존 대비 1.2배 + 화면 비례(uiScale). 밝은 배경이라 글자는 검게.
            float bw = 260f * 1.2f * uiScale, bh = 54f * 1.2f * uiScale, gap = 12f * uiScale, by = titleButtonsY;
            // 변이 선택 UI 가 보이면 버튼 줄이 그만큼 아래로 밀린다 — 맨 아래 버튼이 화면 밖으로
            // 나가지 않게 스택 전체를 위로 당긴다(창이 낮거나 가로로 길 때 안전).
            int rows = (hasSave ? 2 : 1) + 2;
            float stackH = rows * bh + (rows - 1) * gap;
            by = Mathf.Min(by, h - stackH - 16f * uiScale);
            var bst = Btn(Mathf.RoundToInt(17f * 1.2f * uiScale));
            bst.normal.textColor = InkDark; FlatText(bst);
            int rowN = 0;
            if (hasSave)
            {
                if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * rowN++, bw, bh), Loc.T("title.continue"), bst))
                    BeginTransition(ContinueGame);
                // 세이브를 바로 지우는 버튼이라 두 번 눌러야 — 첫 클릭은 경고로 바뀌고 3초 안에 한 번 더
                bool armed = Time.unscaledTime < newConfirmUntil;
                if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * rowN++, bw, bh),
                          Loc.T(armed ? "title.newConfirm" : "title.new"), bst))
                {
                    if (armed) { newConfirmUntil = -1f; BeginTransition(() => { SaveSystem.Wipe(); NewGame(); }); }
                    else newConfirmUntil = Time.unscaledTime + 3f;
                }
            }
            else
            {
                if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * rowN++, bw, bh), Loc.T("title.start"), bst))
                    BeginTransition(NewGame);
            }
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * rowN++, bw, bh), Loc.T("title.settings"), bst)) { settingsReturn = State.Title; state = State.Settings; }
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * rowN++, bw, bh), Loc.T("title.quit"), bst)) QuitGame();
        }

        void DrawSettings()
        {
            FillScreen(new Color(0.27f, 0.28f, 0.31f, 1f));
            DrawGridOverlay(new Color(0.42f, 0.44f, 0.48f, 1f));
            float w = Screen.width, h = Screen.height;

            var title = new GUIStyle(sBig)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(30f * uiScale) };
            GUI.Label(new Rect(0, h * 0.16f, w, Mathf.RoundToInt(48f * uiScale)), Loc.T("title.settings"), title);

            var row = new GUIStyle(sLabel)
            { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(17f * uiScale) };
            FlatText(row);
            var val = new GUIStyle(row)
            { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            FlatText(val);

            // 슬라이더 두 줄 — 가운데 정렬된 하나의 블록으로 배치
            float rowH = 46f * uiScale;
            float labelW = 150f * uiScale, sliderW = 380f * uiScale, valW = 80f * uiScale;
            float blockW = labelW + sliderW + valW + 28f;
            float x0 = w * 0.5f - blockW * 0.5f;
            float y0 = h * 0.34f;

            if (sound != null)
            {
                float before;

                // BGM
                GUI.Label(new Rect(x0, y0, labelW, rowH), Loc.T("set.bgm"), row);
                before = sound.BgmVolume;
                float bv = GUI.HorizontalSlider(
                    new Rect(x0 + labelW + 14f, y0 + rowH * 0.5f - 9f, sliderW, 18f),
                    before, 0f, 1f);
                if (!Mathf.Approximately(bv, before)) { sound.BgmVolume = bv; audioDirty = true; }
                GUI.Label(new Rect(x0 + labelW + sliderW + 28f, y0, valW, rowH),
                          Mathf.RoundToInt(sound.BgmVolume * 100f) + "%", val);

                // 효과음
                float y1 = y0 + rowH + 18f * uiScale;
                GUI.Label(new Rect(x0, y1, labelW, rowH), Loc.T("set.sfx"), row);
                before = sound.SfxVolume;
                float sv = GUI.HorizontalSlider(
                    new Rect(x0 + labelW + 14f, y1 + rowH * 0.5f - 9f, sliderW, 18f),
                    before, 0f, 1f);
                if (!Mathf.Approximately(sv, before))
                {
                    sound.SfxVolume = sv;
                    audioDirty = true;
                    // 조절하는 동안 실제 크기가 들리도록 (너무 잦지 않게 간격을 둔다)
                    if (Time.unscaledTime - lastSfxPreview > 0.12f)
                    { lastSfxPreview = Time.unscaledTime; sound.Play(Sfx.Pop, 0.6f); }
                }
                GUI.Label(new Rect(x0 + labelW + sliderW + 28f, y1, valW, rowH),
                          Mathf.RoundToInt(sound.SfxVolume * 100f) + "%", val);
            }

            // 언어 선택은 타이틀 화면 우측 상단 드롭다운으로 옮겼다(round37) — 여기선 뺀다.

            if (UiBtn(new Rect(w * 0.5f - 110f, h * 0.60f, 220f, 50f), Loc.T("common.back"), Btn(16)))
            {
                if (audioDirty && sound != null) { sound.Save(); audioDirty = false; }
                state = settingsReturn;
            }
        }

        // 엔드 화면 — 뒤 게임화면이 비치는 반투명, 중앙 640×360 패널
        void DrawResult()
        {
            FillScreen(new Color(0.10f, 0.11f, 0.13f, 0.55f)); // 딤 (게임화면 비침)

            // 전체를 K배로 — "웨이브 종료" 화면이 작아서 잘 안 보인다는 피드백(round37)
            const float K = 1.2f;
            const float pw = 640f * K, ph = 360f * K;
            var panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
            var c = GUI.color;
            if (!DrawSkinPanel(panel, false))
            {
                GUI.color = new Color(0.16f, 0.17f, 0.19f, 0.93f);       // 어두운 반투명 패널
                GUI.DrawTexture(panel, Texture2D.whiteTexture);
                GUI.color = new Color(0.55f, 0.62f, 0.58f, 0.7f);        // 상단 라인
                GUI.DrawTexture(new Rect(panel.x, panel.y, pw, 2f * K), Texture2D.whiteTexture);
            }
            GUI.color = c;

            float cx = panel.center.x;
            var title = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(22f * K), fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(panel.x, panel.y + 24f * K, pw, 40f * K),
                resultCleared ? Loc.F("res.titleClear", currentStage + 1) : Loc.F("res.title", currentStage + 1, waveNum), title);

            var head  = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(13f * K), normal = { textColor = new Color(0.68f, 0.72f, 0.70f) } };
            var big   = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(19f * K), fontStyle = FontStyle.Bold };
            var bigY  = new GUIStyle(big)  { normal = { textColor = Gold } };
            var headY = new GUIStyle(head) { normal = { textColor = Gold } };
            float ly = panel.y + 94f * K, lh = 33f * K;
            GUI.Label(new Rect(panel.x, ly, pw, lh), Loc.T("res.cycle"), head);
            GUI.Label(new Rect(panel.x, ly + lh, pw, lh), Loc.F("res.kills", cycleKills), big);
            GUI.Label(new Rect(panel.x, ly + lh * 2f, pw, lh), Loc.F("res.gain", cycleGold.ToString("N0")), bigY);
            GUI.Label(new Rect(panel.x, ly + lh * 3f + 6f * K, pw, lh), Loc.F("res.have", gold.ToString("N0")), headY);

            // 버튼 3개가 패널 폭(pw)을 거의 꽉 채워 좌우 여백이 4px 밖에 안 됐다 — 폭을 조금 줄여 숨통을
            const float bw = 186f * K, bh = 46f * K, gap = 16f * K;
            float by = panel.yMax - bh - 26f * K;
            var bst = Btn(Mathf.RoundToInt(14f * K));
            if (UiBtn(new Rect(cx - bw * 1.5f - gap, by, bw, bh), Loc.T("map.upgrade"), bst))
            { state = State.Tree; treeZoom = 0f; treePan = Vector2.zero; treeFromResult = true; }
            if (UiBtn(new Rect(cx - bw * 0.5f, by, bw, bh), Loc.T("res.retry"), bst))
            { BeginTransition(StartRun); }
            if (UiBtn(new Rect(cx + bw * 0.5f + gap, by, bw, bh), Loc.T("common.toMap"), bst))
            { state = State.Map; }
        }

        // 보스 처치 = 엔딩
        void DrawWin()
        {
            FillScreen(new Color(0.10f, 0.11f, 0.13f, 0.62f));
            const float pw = 640f;
            float ph = lastAscendWasFirst ? 426f : 374f;
            var panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
            var c = GUI.color;
            if (!DrawSkinPanel(panel, false))
            {
                GUI.color = new Color(0.14f, 0.17f, 0.16f, 0.95f);
                GUI.DrawTexture(panel, Texture2D.whiteTexture);
                GUI.color = new Color(0.45f, 0.90f, 0.55f, 0.9f);
                GUI.DrawTexture(new Rect(panel.x, panel.y, pw, 3f), Texture2D.whiteTexture);
            }
            GUI.color = c;

            float cx = panel.center.x;
            var title = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.95f, 0.62f) } };
            GUI.Label(new Rect(panel.x, panel.y + 20f, pw, 42f), Loc.T("win.title"), title);

            float curY = panel.y + 68f;

            if (lastAscendWasFirst)
            {
                var bannerR = new Rect(panel.x + 30f, curY, pw - 60f, 42f);
                var gcB = GUI.color; GUI.color = new Color(0.20f, 0.45f, 0.30f, 0.92f);
                GUI.DrawTexture(bannerR, Texture2D.whiteTexture);
                GUI.color = gcB;
                var bannerS = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.80f, 1f, 0.88f) }, wordWrap = true };
                FlatText(bannerS);
                GUI.Label(bannerR, Loc.T("win.unlocked"), bannerS);
                curY += 54f;
            }

            var head  = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            var goldL = new GUIStyle(head) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } };
            var ascL  = new GUIStyle(head) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.95f, 0.62f) } };
            const float lh = 30f;

            int mm = Mathf.FloorToInt(winTimeSec / 60f), ss = Mathf.FloorToInt(winTimeSec % 60f);
            GUI.Label(new Rect(panel.x, curY, pw, lh), Loc.F("win.time", mm, ss.ToString("00")), head); curY += lh;
            GUI.Label(new Rect(panel.x, curY, pw, lh), Loc.F("win.line1", winKills), head); curY += lh;
            GUI.Label(new Rect(panel.x, curY, pw, lh), Loc.F("win.goldEarned", winGold.ToString("N0")), goldL); curY += lh;
            GUI.Label(new Rect(panel.x, curY, pw, lh), Loc.F("win.ascend", lastAscendLevelPlayed, lastAscendShardGain), ascL); curY += lh + 14f;

            // 버튼은 "타이틀로" 하나만 — 다음 승천 난이도는 타이틀 화면에서 고른다(여기서 바로 이어가는
            // 지름길 버튼은 없앰).
            const float bw = 240f, bh = 50f;
            float by = panel.yMax - bh - 24f;
            var bst = Btn(16);
            if (UiBtn(new Rect(cx - bw * 0.5f, by, bw, bh), Loc.T("common.toTitle"), bst)) GoToTitle();
        }

        // 부모 a → 자식 b 를 ㄱ자(축 정렬) 선으로 연결. 회전을 안 쓰므로 확대/이동해도 노드에 딱 붙는다.
        // 수직/수평 선분 하나 — 회전 없이 축 정렬 사각형으로.
        //   round41 버그: 예전엔 GUIUtility.RotateAroundPivot 로 돌려 그렸는데, 이 함수는 기준점을
        //   "화면 좌표"로 받는다(내부에서 GUI.matrix 를 identity 로 되돌린 뒤 pivot 을 해석). 트리는
        //   확대/이동 행렬 안에서 그리므로 트리 좌표를 넘기면 엉뚱한 점을 기준으로 돌아가서,
        //   세로선(90°)·왼쪽 가로선(180°)이 배율/이동에 따라 노드에서 떨어져 나갔다.
        //   선이 전부 직각이 된 지금은 회전 자체가 필요 없다 — 노드와 같은 행렬만 거치므로 어긋날 수 없다.
        //   w: 선 굵기(트리 좌표). 끝을 반 굵기만큼 늘려 꺾이는 모서리가 빈틈 없이 맞물리게.
        static void DrawAxisLine(Vector2 a, Vector2 b, float w, Color col)
        {
            float h = w * 0.5f;
            Rect r;
            if (Mathf.Abs(a.x - b.x) < 0.01f)
            {
                if (Mathf.Abs(a.y - b.y) < 0.01f) return;
                r = new Rect(a.x - h, Mathf.Min(a.y, b.y) - h, w, Mathf.Abs(a.y - b.y) + w);
            }
            else
                r = new Rect(Mathf.Min(a.x, b.x) - h, a.y - h, Mathf.Abs(a.x - b.x) + w, w);
            var gc = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = gc;
        }

        // round41: 직각(ㄱ자) 꺾임 3단 — 부모(a, 아래)에서 수직으로 올라가다 부모·자식 행의 중간
        //   높이에서 꺾여 자식의 x로 이동한 뒤 다시 수직으로 자식(b)까지. 같은 부모를 둔 형제들은
        //   전부 같은 중간 높이에서 꺾이므로 "선반(shelf)" 하나에 나란히 매달린 것처럼 보인다.
        static void DrawConnector(Vector2 a, Vector2 b, float w, Color col)
        {
            float midY = (a.y + b.y) * 0.5f;
            DrawAxisLine(a, new Vector2(a.x, midY), w, col);
            DrawAxisLine(new Vector2(a.x, midY), new Vector2(b.x, midY), w, col);
            DrawAxisLine(new Vector2(b.x, midY), b, w, col);
        }

        // round45: 반복 강화 — 트리 안이 아니라 능력치 패널 바로 아래 고정 패널.
        //   트리를 스크롤해도 자리가 안 바뀌어서 "지금 뭘 몇 레벨 올렸는지" 가 항상 같은 위치에 보인다.
        void DrawRepeatPanel(Rect statRect)
        {
            if (nodes == null) return;
            var list = new List<UpgradeNode>();
            foreach (var n in nodes) if (n.repeat) list.Add(n);
            if (list.Count == 0) return;

            // round46: 우측 하단 고정 — 능력치 패널(우측 상단)과 짝을 이루고, 트리를 스크롤해도 자리가 같다.
            float pad = 12f * uiScale, rowH = 46f * uiScale, head = 26f * uiScale;
            float w = statRect.width;
            float x = statRect.x;
            float h = pad + head + rowH * list.Count + pad;
            float bottom = Screen.height - 118f * uiScale - 12f;   // 하단 바 바로 위
            float top = statRect.yMax + 10f * uiScale;             // 능력치 패널과는 겹치지 않게
            if (h > bottom - top) { rowH = Mathf.Max(28f * uiScale, (bottom - top - pad * 2f - head) / list.Count); h = pad + head + rowH * list.Count + pad; }
            float y = bottom - h;
            var area = new Rect(x, y, w, h);
            if (!DrawSkinPanel(area, true))
            {
                var gcP = GUI.color;
                GUI.color = new Color(0.13f, 0.14f, 0.17f, 0.94f);
                GUI.DrawTexture(area, Texture2D.whiteTexture);
                GUI.color = new Color(0.90f, 0.64f, 0.08f, 1f);
                GUI.DrawTexture(new Rect(x, y, w, 2f), Texture2D.whiteTexture);
                GUI.color = gcP;
            }

            var hs = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(15f * uiScale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            hs.normal.textColor = new Color(1f, 0.82f, 0.30f); FlatText(hs);
            GUI.Label(new Rect(x + pad, y + pad * 0.6f, w - pad * 2f, head), Loc.T("tree.repTitle"), hs);

            var ns = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(12f * uiScale), alignment = TextAnchor.MiddleLeft };
            ns.normal.textColor = Ink; FlatText(ns);
            var ls = new GUIStyle(ns) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            ls.normal.textColor = new Color(1f, 0.82f, 0.30f); FlatText(ls);
            var bst = Btn(Mathf.RoundToInt(11f * uiScale));

            float rowY = y + pad + head;
            foreach (var n in list)
            {
                bool locked = n.tier > stagesCleared;
                bool open = IsBuyable(n);   // 부모 가지를 다 뚫어야 열린다
                float iconSz = Mathf.Min(rowH - 8f * uiScale, 26f * uiScale);
                var ir = new Rect(x + pad, rowY + (rowH - iconSz) * 0.5f, iconSz, iconSz);
                var tex = (iconTex != null && iconTex.TryGetValue(n.icon, out var it)) ? it : null;
                var gc0 = GUI.color;
                if (tex != null)
                {
                    GUI.color = locked || !open ? new Color(1f, 1f, 1f, 0.28f) : new Color(1f, 0.86f, 0.42f);
                    GUI.DrawTexture(ir, tex, ScaleMode.ScaleToFit);
                }
                GUI.color = gc0;

                float tx = ir.xMax + 6f * uiScale;
                float btnW = 40f * uiScale, btnH2 = Mathf.Min(22f * uiScale, rowH * 0.46f), bgap = 4f * uiScale;
                float btnsW = btnW * 3f + bgap * 2f;
                float nameW = Mathf.Max(30f, w - pad * 2f - (ir.width + 6f * uiScale) - btnsW - 6f * uiScale);
                GUI.Label(new Rect(tx, rowY, nameW, rowH * 0.55f), Loc.T(n.label), ns);
                string sub = locked ? Loc.F("tree.lockTier", n.tier)
                           : !open ? Loc.T("tree.repLocked")
                           : "Lv " + RepLevel(n.id) + " · $" + n.cost.ToString("N0");
                var ss = new GUIStyle(ns) { fontSize = Mathf.RoundToInt(10f * uiScale) };
                ss.normal.textColor = new Color(0.72f, 0.74f, 0.78f); FlatText(ss);
                GUI.Label(new Rect(tx, rowY + rowH * 0.48f, nameW, rowH * 0.5f), sub, ss);

                if (!locked && open)
                {
                    int maxN = (int)System.Math.Min(int.MaxValue, gold / System.Math.Max(1L, n.cost));
                    int[] counts = { 1, 10, maxN };
                    for (int i = 0; i < 3; i++)
                    {
                        int cnt = counts[i];
                        var r = new Rect(x + w - pad - btnsW + i * (btnW + bgap), rowY + (rowH - btnH2) * 0.5f, btnW, btnH2);
                        bool prev = GUI.enabled;
                        GUI.enabled = (i == 2 ? maxN >= 1 : maxN >= cnt) && transDir == 0;
                        if (UiBtn(r, i == 2 ? Loc.T("tree.repMaxShort") : "+" + cnt, bst)) BuyRepeat(n, i == 2 ? maxN : cnt);
                        GUI.enabled = prev;
                    }
                }
                rowY += rowH;
            }
        }

        void DrawTree()
        {
            // round41: 어두운 배경 -> 타이틀/지도와 같은 밝은 유리 톤으로 통일
            if (!DrawSkinBackground()) FillScreen(Glass);
            DrawGridOverlay(GlassGrid);
            var c0 = GUI.color;

            // 하단 바 높이 + 좌측 능력치 패널 폭을 먼저 알아야 트리를 남는 공간 중앙에 맞출 수 있다
            float barH = 118f * uiScale;
            Rect statRect = StatPanelRect(barH);   // 우측 상단 능력치 패널

            const float nodeSz = 80f, rowSpacing = 230f, colUnit = 120f;   // round41: 노드 2배 확대 요청 — 열 간격도 겹치지 않게 같이 키움

            // round41: 부채꼴로 뻗던 걸 걷어내고 "티어 = 한 행"인 계층형(org-chart) 레이아웃으로 —
            //   같은 부모의 자식들이 한 선반(shelf)처럼 가로로 늘어서고, 세분화될수록(자식이 많을수록)
            //   그 행이 넓어지며 위로 쌓인다. 고전적인 재귀 서브트리-너비 배분 알고리즘.
            float availTop = Screen.height - barH;
            // round43: 가로는 고정 — 가지 14개 전부가 능력치 패널 왼쪽 공간에 딱 들어오는 배율로 두고,
            //   확대/축소 대신 휠·우클릭 드래그로 위아래 스크롤만 한다(사용자 요청).
            Rect area = new Rect(16f, 16f, Mathf.Max(240f, statRect.xMin - 32f), Mathf.Max(120f, availTop - 32f));
            Vector2 pivot = new Vector2(area.center.x, availTop - 20f);   // y 는 배율 계산 뒤 다시 잡는다

            var childrenOf = new Dictionary<string, List<UpgradeNode>>();
            UpgradeNode rootNode = null;
            foreach (var n in nodes)
            {
                if (n.IsRoot) { rootNode = n; continue; }
                if (n.repeat) continue;   // round45: 반복 강화는 트리가 아니라 능력치 패널 아래 고정 패널에
                if (!childrenOf.TryGetValue(n.parentId, out var list)) { list = new List<UpgradeNode>(); childrenOf[n.parentId] = list; }
                list.Add(n);
            }

            var depthOf = new Dictionary<string, int>();
            var weightOf = new Dictionary<string, int>();
            // 가중치 = 그 아래 "잎(가지 끝)" 개수 — 가지 하나가 차지하는 가로 칸 수.
            //   round43: 전체 트리 기준(드러난 것만이 아니라). 스크롤 방식에선 열이 제자리에 있어야
            //   한다 — 드러난 것 기준이면 노드를 살 때마다 가지들이 옆으로 밀려 움직였다.
            int ComputeDepthWeight(UpgradeNode n, int depth)
            {
                depthOf[n.id] = depth;
                int w = 0;
                if (childrenOf.TryGetValue(n.id, out var kids))
                    foreach (var k in kids) w += ComputeDepthWeight(k, depth + 1);
                if (w == 0) w = 1;   // 가지 끝 = 한 칸
                weightOf[n.id] = w;
                return w;
            }
            if (rootNode != null) ComputeDepthWeight(rootNode, 0);

            var xOf = new Dictionary<string, float>();
            void AssignX(UpgradeNode n, float xMin, float xMax)
            {
                xOf[n.id] = (xMin + xMax) * 0.5f;
                if (!childrenOf.TryGetValue(n.id, out var kids)) return;
                int total = 0;
                foreach (var k in kids) total += weightOf[k.id];
                // 자식이 전부 아직 안 드러났어도(가중치 합 0) 위치는 줘야 한다 — 안 그러면 아래 npos
                // 루프가 xOf 를 못 찾아 KeyNotFoundException. 그땐 균등 분배(어차피 안 그려짐).
                float cursor = xMin;
                foreach (var k in kids)
                {
                    float w = total > 0 ? (xMax - xMin) * weightOf[k.id] / total : (xMax - xMin) / kids.Count;
                    AssignX(k, cursor, cursor + w);
                    cursor += w;
                }
            }
            if (rootNode != null)
            {
                float rootHalfW = weightOf[rootNode.id] * colUnit * 0.5f;
                AssignX(rootNode, -rootHalfW, rootHalfW);
            }

            // 배율 = 전체 가지 폭이 area 가로에 딱 맞게(좌우 여백 = 노드 반 개 + 약간). 고정.
            float fullW = (rootNode != null ? weightOf[rootNode.id] : 1) * colUnit;
            float zoom = Mathf.Clamp(area.width / (fullW + nodeSz * 0.5f), 0.2f, 1.6f);
            pivot.y = availTop - 22f - nodeSz * 0.5f * zoom;   // 스크롤 맨 아래에서 코어가 하단 바 바로 위

            npos.Clear();
            foreach (var n in nodes)
                if (!n.repeat) npos[n.id] = new Vector2(pivot.x + xOf[n.id], pivot.y - depthOf[n.id] * rowSpacing);

            // 위로는 "지금까지 드러난 것"까지만 올라간다 — 뚫을수록 더 위까지 볼 수 있다(round41 규칙 유지).
            float minY = pivot.y;
            foreach (var n in nodes)
                if (!n.repeat && IsRevealed(n) && npos[n.id].y < minY) minY = npos[n.id].y;   // 화면 좌표는 위로 갈수록 y가 작아짐
            float topScreen0 = pivot.y + (minY - pivot.y) * zoom;          // 스크롤 0 일 때 가장 높은 노드의 화면 y
            float maxPan = Mathf.Max(0f, (area.yMin + nodeSz * 0.5f * zoom + 8f) - topScreen0);

            treeMaxPan = maxPan;
            if (treeZoom <= 0f)   // 들어올 때/"화면 리셋" — 지금 살 수 있는 맨 위(경계)가 보이게 끝까지 올려둔다
                treePan = new Vector2(0f, maxPan);
            treeZoom = zoom;

            Event ev = Event.current;
            if (ev.type == EventType.ScrollWheel)
            { treePan.y -= ev.delta.y * 45f; ev.Use(); }   // 휠 위로 = 트리의 위쪽(더 뚫은 곳)으로
            else if (ev.type == EventType.MouseDrag && (ev.button == 1 || ev.button == 2))
            { treePan.y += ev.delta.y; ev.Use(); }

            // round46: 웹페이지식 세로 스크롤바 — 트리 영역 오른쪽 끝. 트랙을 누르면 그 위치로 점프,
            //   핸들은 끌어서 이동. treePan.y 는 0(맨 아래) ~ maxPan(맨 위) 이라 화면과는 방향이 반대다.
            if (maxPan > 1f)
            {
                float barW = 14f * uiScale;
                var track = new Rect(area.xMax - barW, area.yMin, barW, area.height);
                // 핸들 크기 = 보이는 높이 / 전체 높이
                float viewH = area.height;
                float totalH = viewH + maxPan;
                float handH = Mathf.Max(36f * uiScale, track.height * (viewH / totalH));
                float tRaw = 1f - treePan.y / maxPan;            // 0 = 맨 위(스크롤 최대), 1 = 맨 아래
                float handY = track.y + (track.height - handH) * tRaw;
                var handle = new Rect(track.x, handY, barW, handH);

                if (ev.type == EventType.MouseDown && ev.button == 0 && track.Contains(ev.mousePosition))
                {
                    if (handle.Contains(ev.mousePosition)) { treeBarDrag = true; treeBarGrabOff = ev.mousePosition.y - handle.y; }
                    else   // 트랙 클릭 = 그 지점으로
                    {
                        treeBarDrag = true; treeBarGrabOff = handH * 0.5f;
                        float t2 = Mathf.Clamp01((ev.mousePosition.y - treeBarGrabOff - track.y) / Mathf.Max(1f, track.height - handH));
                        treePan.y = (1f - t2) * maxPan;
                    }
                    ev.Use();
                }
                if (treeBarDrag)
                {
                    if (ev.type == EventType.MouseDrag)
                    {
                        float t2 = Mathf.Clamp01((ev.mousePosition.y - treeBarGrabOff - track.y) / Mathf.Max(1f, track.height - handH));
                        treePan.y = (1f - t2) * maxPan;
                        ev.Use();
                    }
                    else if (ev.type == EventType.MouseUp) { treeBarDrag = false; ev.Use(); }
                }

                treePan = new Vector2(0f, Mathf.Clamp(treePan.y, 0f, maxPan));
                // 다시 계산(드래그로 바뀌었을 수 있음)
                tRaw = 1f - treePan.y / maxPan;
                handle.y = track.y + (track.height - handH) * tRaw;

                var gcB = GUI.color;
                GUI.color = new Color(0.20f, 0.26f, 0.30f, 0.30f);
                GUI.DrawTexture(track, Texture2D.whiteTexture);
                bool hot = treeBarDrag || handle.Contains(ev.mousePosition);
                GUI.color = hot ? new Color(0.22f, 0.40f, 0.46f, 0.95f) : new Color(0.28f, 0.46f, 0.52f, 0.75f);
                GUI.DrawTexture(handle, Texture2D.whiteTexture);
                GUI.color = gcB;
            }
            else treePan = new Vector2(0f, Mathf.Clamp(treePan.y, 0f, maxPan));

            Matrix4x4 saved = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(treeZoom, treeZoom), pivot);
            GUI.matrix = Matrix4x4.Translate(new Vector3(treePan.x, treePan.y, 0f)) * GUI.matrix;

            // 연결선 — 드러난 노드까지만(아직 안 드러난 가지는 선도 안 그린다).
            //   기본은 검정, 그 노드를 뚫으면 초록. 형제끼리 부모의 세로선·가로 선반을 공유하므로
            //   검정을 먼저 다 그리고 초록을 위에 덮어야 뚫은 경로가 중간에 끊겨 보이지 않는다.
            //   굵기는 트리 좌표 5 — 단, 축소해도 화면에서 1.5px 밑으로는 안 가늘어지게.
            float lineW = Mathf.Max(5f, 1.5f / Mathf.Max(0.01f, treeZoom));
            Color lineOff = new Color(0.05f, 0.05f, 0.06f, 0.85f);   // 검정 — 아직
            Color lineOn  = new Color(0.10f, 0.62f, 0.18f, 1f);      // 초록 — 뚫음
            for (int pass = 0; pass < 2; pass++)
                foreach (var n in nodes)
                    if (!n.IsRoot && !n.repeat && IsRevealed(n) && IsBought(n.id) == (pass == 1))
                        DrawConnector(npos[n.parentId], npos[n.id], lineW, pass == 1 ? lineOn : lineOff);

            // 노드 — 아이콘만. 효과는 마우스 오버 툴팁으로.
            UpgradeNode hovered = null;
            foreach (var n in nodes)
            {
                if (n.repeat) continue;         // 반복 강화는 아래 고정 패널에서 따로 그린다
                if (!IsRevealed(n)) continue;   // 아직 자라지 않은 가지는 그리지 않는다
                Vector2 p = npos[n.id];
                var rect = new Rect(p.x - nodeSz * 0.5f, p.y - nodeSz * 0.5f, nodeSz, nodeSz);
                bool bt = IsBought(n.id);
                bool by = IsBuyable(n);                    // 부모가 뚫려 바로 살 수 있음
                bool pathAfford = by && gold >= n.cost;    // 지금 이 노드를 살 돈이 있음

                bool locked = n.tier > stagesCleared;
                // round40: 사각 칩 -> 원형 네온 뱃지. 안쪽 부드러운 원(discSprite) + 바깥 글로우 링(nodeRingTex).
                //  구매함 = 하늘색  ·  지금 살 수 있음(돈 있음) = 초록  ·  살 순 있으나 골드 부족 = 갈색  ·  잠김 = 어두움
                Color fillCol = locked ? new Color(0.08f, 0.08f, 0.10f, 0.92f)
                             : bt ? new Color(0.10f, 0.22f, 0.34f, 1f)
                             : pathAfford ? new Color(0.09f, 0.22f, 0.13f, 1f)
                             : by ? new Color(0.24f, 0.17f, 0.07f, 1f)
                             : new Color(0.10f, 0.10f, 0.12f, 1f);
                // round41: 밝은 배경에서도 또렷하도록 링 색도 짙게 — 예전 파스텔톤은 Glass 배경에
                // 묻혀서 거의 안 보였다.
                Color glowCol = bt ? new Color(0.06f, 0.32f, 0.62f, 1f)      // 짙은 하늘색(구매함)
                             : pathAfford ? new Color(0.08f, 0.42f, 0.20f, 1f)  // 짙은 초록(살 수 있음)
                             : by ? new Color(0.62f, 0.34f, 0.06f, 1f)          // 짙은 주황(부족)
                             : new Color(0.40f, 0.42f, 0.46f, 0.8f);            // 짙은 회색(잠김)
                if (discSprite != null)
                {
                    GUI.color = fillCol;
                    GUI.DrawTexture(rect, discSprite.texture);
                }
                if (nodeRingTex != null)
                {
                    float ringPad = nodeSz * (bt ? 0.06f : 0.10f);   // 구매함은 글로우가 더 두꺼워 보이게
                    GUI.color = glowCol;
                    GUI.DrawTexture(new Rect(rect.x - ringPad, rect.y - ringPad, rect.width + ringPad * 2f, rect.height + ringPad * 2f), nodeRingTex);
                }

                var tex = (iconTex != null && iconTex.TryGetValue(n.icon, out var it)) ? it : (iconTex != null ? iconTex["dot"] : null);
                if (tex != null)
                {
                    GUI.color = locked ? new Color(1f, 1f, 1f, 0.13f)
                              : bt ? Color.white
                              : (pathAfford || by) ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                    float pad = nodeSz * 0.18f;
                    GUI.DrawTexture(new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height - pad * 2f), tex, ScaleMode.ScaleToFit);
                }
                GUI.color = c0;

                if (rect.Contains(Event.current.mousePosition)) hovered = n;
                if (!locked && !bt && transDir == 0 && GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    BuyNode(n);
            }

            GUI.matrix = saved;

            DrawStatPanel(statRect, hovered);
            DrawRepeatPanel(statRect);

            // 마우스 오버 툴팁 (스크린 좌표)
            if (hovered != null)
            {
                bool bt = IsBought(hovered.id);
                string status;
                if (bt) status = Loc.T("tree.owned");
                else if (hovered.IsRoot) status = Loc.T("tree.free");
                else if (hovered.tier > stagesCleared) status = Loc.F("tree.lockTier", hovered.tier);
                else status = $"${hovered.cost:N0}";
                string top = Loc.T(hovered.label) + "\n" + (hovered.descArg != 0f ? Loc.F(hovered.desc, hovered.descArg) : Loc.T(hovered.desc));
                var ts  = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(13f * uiScale), wordWrap = true, alignment = TextAnchor.UpperLeft };
                var tsY = new GUIStyle(ts) { fontStyle = FontStyle.Bold, normal = { textColor = bt ? Ink : Gold } };
                FlatText(tsY);
                const float padX = 12f, padTop = 10f, gapMid = 6f, padBot = 12f;
                float tw = 340f, innerW = tw - padX * 2f;
                float topH = ts.CalcHeight(new GUIContent(top), innerW);
                float statusH = tsY.CalcHeight(new GUIContent(status), innerW);
                float th = padTop + topH + gapMid + statusH + padBot;
                Vector2 mp = Event.current.mousePosition;
                var tr = new Rect(Mathf.Min(mp.x + 16f, Screen.width - tw - 8f), Mathf.Min(mp.y + 10f, Screen.height - th - 8f), tw, th);
                if (!DrawSkinPanel(tr, false))
                {
                    GUI.color = new Color(0.12f, 0.13f, 0.15f, 0.97f);
                    GUI.DrawTexture(tr, Texture2D.whiteTexture);
                    GUI.color = new Color(0.56f, 0.62f, 0.58f, 0.8f);
                    GUI.DrawTexture(new Rect(tr.x, tr.y, tr.width, 2f), Texture2D.whiteTexture);
                }
                GUI.color = c0;
                GUI.Label(new Rect(tr.x + padX, tr.y + padTop, innerW, topH), top, ts);
                GUI.Label(new Rect(tr.x + padX, tr.y + padTop + topH + gapMid, innerW, statusH), status, tsY);
            }

            // 고정 하단 바 — 화면 맨 아래에 그룹으로 고정(게임뷰가 살짝 잘려도 버튼이 안쪽에 오도록 여유 확보)
            // barH 는 위에서 이미 계산됨
            // 버튼 3개(화면리셋/재도전/지도로) — 좁고 긴 창에서도 안 밀리게 폭을 화면비로도 제한
            float resetBtnW = Mathf.Min(116f * uiScale, Screen.width * 0.15f);
            float retryBtnW = Mathf.Min(110f * uiScale, Screen.width * 0.14f);
            float mapBtnW   = Mathf.Min(130f * uiScale, Screen.width * 0.16f);
            float jumpBtnW  = Mathf.Min(92f * uiScale, Screen.width * 0.11f);   // round45: 맨 위로 / 맨 아래로
            float btnH = 34f * uiScale;
            float margin = 20f, btnGap = 10f * uiScale;
            bool showRetry = treeFromResult;   // 전투 직후에 들어온 경우에만 재도전
            float groupW = jumpBtnW * 2f + btnGap * 2f + resetBtnW + btnGap + mapBtnW + (showRetry ? retryBtnW + btnGap : 0f);
            GUI.BeginGroup(new Rect(0f, Screen.height - barH, Screen.width, barH));
            {
                // round41: 배경을 밝은 Glass 로 바꾸자 노란 보유금·흰 도움말이 거의 안 보였고, 트리 선도
                //   이 위로 지나갔다 — 능력치 패널과 같은 어두운 판을 깔아 글씨를 살리고 트리를 가린다.
                var gcBar = GUI.color;
                // 스킨이면 좌우·아래로 넘치게 그려서(그룹 밖은 잘림) 위쪽 가장자리만 세포막처럼 보이게
                if (!DrawSkinPanel(new Rect(-40f, 0f, Screen.width + 80f, barH + 60f), false))
                {
                    GUI.color = new Color(0.13f, 0.14f, 0.17f, 0.92f);
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, barH), Texture2D.whiteTexture);
                    GUI.color = new Color(0.56f, 0.62f, 0.58f, 0.55f);
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 2f), Texture2D.whiteTexture);
                }
                GUI.color = gcBar;

                var goldS = new GUIStyle(sGold) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(18f * uiScale), fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(margin, 8f, 520f * uiScale, 30f * uiScale), Loc.F("res.have", gold.ToString("N0")), goldS);

                var info = new GUIStyle(sLabel) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(13f * uiScale), wordWrap = true };
                float infoTop = 8f + 30f * uiScale;
                float bx1 = Screen.width - margin - groupW;
                float helpW = Mathf.Max(60f, bx1 - margin - btnGap);
                float helpAvailH = barH - infoTop - 6f;
                string helpTxt = Loc.T("tree.help");
                float neededH = info.CalcHeight(new GUIContent(helpTxt), helpW);
                if (neededH > helpAvailH && neededH > 1f)   // 좁은 창에서 줄바꿈이 늘어나면 폰트를 줄여서 안 잘리게
                    info.fontSize = Mathf.Max(9, Mathf.RoundToInt(info.fontSize * (helpAvailH / neededH)));
                GUI.Label(new Rect(margin, infoTop, helpW, helpAvailH), helpTxt, info);

                var bst = Btn(Mathf.RoundToInt(14f * uiScale));
                float btnY = barH - btnH - margin;
                // 맨 위로 / 맨 아래로 — 150노드를 휠로 훑지 않아도 되게
                if (UiBtn(new Rect(bx1, btnY, jumpBtnW, btnH), Loc.T("tree.toTop"), bst))
                { treePan = new Vector2(0f, treeMaxPan); }
                if (UiBtn(new Rect(bx1 + jumpBtnW + btnGap, btnY, jumpBtnW, btnH), Loc.T("tree.toBottom"), bst))
                { treePan = Vector2.zero; }
                bx1 += (jumpBtnW + btnGap) * 2f;
                float bx2 = bx1 + resetBtnW + btnGap;
                float bx3 = bx2 + retryBtnW + btnGap;
                if (UiBtn(new Rect(bx1, btnY, resetBtnW, btnH), Loc.T("tree.reset"), bst))
                { treeZoom = 0f; treePan = Vector2.zero; }
                if (showRetry && UiBtn(new Rect(bx2, btnY, retryBtnW, btnH), Loc.T("res.retry"), bst))
                { BeginTransition(StartRun); }
                if (UiBtn(new Rect(showRetry ? bx3 : bx2, btnY, mapBtnW, btnH), Loc.T("common.toMap"), bst))
                { state = State.Map; }
            }
            GUI.EndGroup();
        }
    }
}
