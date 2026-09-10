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
        public Vector2 fieldSize = new Vector2(16f, 10f);

        [Header("UI")]
        [Tooltip("HUD·텍스트 전체 배율 — Awake에서 항상 코드 기본값으로 리셋(씬 직렬화 무시)")]
        [HideInInspector] public float uiScale = 1.5f;

        enum State { Title, Map, Playing, Paused, Result, Tree, Settings, Meta, Win }
        State state = State.Title;
        State settingsReturn = State.Title;
        State pauseReturn = State.Playing;   // 일시정지에서 "계속/돌아가기" 시 복귀할 화면

        int gold, waveNum = 1, kills, quota;
        int cycleKills, cycleGold;
        int currentStage = 0;     // 지금 플레이 중인 지역 (0~7)
        int stagesCleared = 0;    // 클리어한 지역 수 = 해금된 tier
        int retryCount = 0;       // 부대 카운터(리트라이 횟수) — 표시용
        int maxScore = 0;         // 프레스티지 보상 기준 (stagesCleared*100 + 최고 웨이브)
        bool hasSave = false;     // 타이틀 '이어하기' 표시용
        int metaCurrency = 0;     // 환생 재화 (shard)
        int bestScore = 0;        // 역대 최고 score
        readonly Dictionary<string, int> metaLv = new Dictionary<string, int>();
        List<UpgradeTree.MetaNode> metaNodes;
        Stats pristineStats;      // 메타 적용 전의 순정 기본값
        float timeLeft, attackTimer, spawnTimer, spawnInterval;

        Stats baseStats;
        HashSet<string> bought = new HashSet<string>();
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
            public int spawnWave;   // 이 적이 소환된 웨이브 (골드·체력 기준)
        }
        readonly List<Enemy> enemies = new List<Enemy>();
        Enemy boss;          // 30웨이브 보스 (없으면 null)
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

        GameAudio sound;     // 절차 생성 효과음 + BGM (Audio.cs)
        bool audioDirty;             // 설정에서 볼륨을 건드렸으면 나갈 때 PlayerPrefs 저장
        float manualClickCd;        // 수동 공격 쿨다운 — 오토마우스/연타로 이득 못 보게
        float lastSfxPreview;        // 효과음 슬라이더 미리듣기 쿨다운

        Camera cam;
        Sprite cellSprite, discSprite, splatSprite;
        Texture2D gridTile;
        Dictionary<string, Texture2D> iconTex;
        Material spriteMat;
        Transform cursorRing;
        SpriteRenderer cursorSr;
        Vector3 cursorWorld;

        GUIStyle sLabel, sGold, sCenter, sBig, sSmall, sBtn;
        float lastUiScale = -1f;
        Font uiFont;   // 번들된 한글 폰트 (Assets/Resources/PopCellKR.ttf) — 웹 빌드엔 OS 폰트 폴백이 없어서 필수

        // 현미경 유리 팔레트 — 타이틀 화면과 게임 배경이 공유한다
        static readonly Color Glass     = new Color(0.74f, 0.86f, 0.91f); // 뿌연 하늘색(유리)
        static readonly Color GlassGrid = new Color(0.60f, 0.74f, 0.82f); // 그 위 격자(계수판)
        static readonly Color InkDark   = new Color(0.09f, 0.11f, 0.13f); // 밝은 배경용 진한 텍스트
        static readonly Color HudGold   = new Color(0.52f, 0.36f, 0.02f); // 밝은 배경용 달러색(노랑은 안 보임)

        static readonly Color Ink  = new Color(0.94f, 0.94f, 0.95f); // 기본 텍스트(흰색) — 회색 바탕용
        static readonly Color Gold = new Color(1f, 0.82f, 0.30f);    // 달러 관련 텍스트(노랑)

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
            fieldSize = new Vector2(16f, 10f);
            uiScale = 1.5f;            // 씬에 2가 직렬화돼 UI가 화면 밖으로 넘치던 문제 차단

            cam = Camera.main;
            if (cam == null) { Debug.LogError("[POP Cell] Main Camera 없음"); enabled = false; return; }

            cam.orthographic = true;
            cam.orthographicSize = fieldSize.y * 0.5f + 0.5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Glass;   // 현미경 유리 너머의 뿌연 하늘색

            spriteMat = new Material(FindSpriteShader());
            cellSprite = BuildCellSprite();
            discSprite = BuildDiscSprite();
            splatSprite = BuildSplatSprite();
            gridTile = BuildGridTile();
            iconTex = NodeIcons.Build();

            BuildBackground();
            BuildCursor();

            nodes = UpgradeTree.BuildAll();
            metaNodes = UpgradeTree.BuildMeta();
            pristineStats = stats.Clone();
            baseStats = stats.Clone();
            LoadProgress();
            RebuildBaseStats();

            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            sound = new GameAudio(transform);

            state = State.Title; // 타이틀에서 시작
        }

        // ---- 절차적 스프라이트 ----

        Sprite BuildCellSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float rad = size * 0.5f - 2f;
            var c = new Vector2(size * 0.5f, size * 0.5f);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / rad;
                    float v, a;
                    if (d >= 1.02f) { v = 0f; a = 0f; }
                    else
                    {
                        if (d < 0.34f)       v = 0.55f;   // 핵 (약간 어둡게)
                        else if (d < 0.82f)  v = 0.96f;   // 몸통 (밝고 단색 — 색이 진하게 보임)
                        else if (d < 0.90f)  v = 1.00f;   // 세포막 하이라이트
                        else                 v = 0.10f;   // 굵고 진한 테두리 (배경과 확실히 분리)
                        a = d < 0.93f ? 1.0f : Mathf.Lerp(1.0f, 0f, (d - 0.93f) / 0.09f);
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

            var board = new GameObject("GridBoard");
            var sr = board.AddComponent<SpriteRenderer>();
            sr.sprite = gsprite;
            sr.sharedMaterial = spriteMat;
            sr.color = GlassGrid;   // 계수판 격자 — 유리색보다 살짝 진하게
            sr.sortingOrder = -60;
            board.transform.position = new Vector3(0f, 0f, 1f);
            board.transform.localScale = new Vector3(vw * 1.25f, vh * 1.25f, 1f);

            var fr = FieldRect;
            for (int i = 0; i < 24; i++)
            {
                var sp = new GameObject("Speck");
                var ssr = sp.AddComponent<SpriteRenderer>();
                ssr.sprite = discSprite;
                ssr.sharedMaterial = spriteMat;
                ssr.color = new Color(1f, 1f, 1f, Random.Range(0.10f, 0.24f));   // 유리 위 기포/먼지
                ssr.sortingOrder = -40;
                sp.transform.localScale = Vector3.one * Random.Range(0.04f, 0.10f);
                sp.transform.position = new Vector3(Random.Range(fr.xMin, fr.xMax), Random.Range(fr.yMin, fr.yMax), 0f);
                specks.Add(sp.transform);
                speckVel.Add(new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f)));
            }

            // 비네트는 세포(정렬 10)보다 아래에 둔다 — 가장자리 세포까지 어두워지면 안 보이므로
            var vig = new GameObject("Vignette");
            var vsr = vig.AddComponent<SpriteRenderer>();
            vsr.sprite = BuildVignetteSprite();
            vsr.sharedMaterial = spriteMat;
            vsr.color = new Color(0.06f, 0.16f, 0.24f, 0.55f);   // 경통 안을 들여다보는 푸른 어둠
            vsr.sortingOrder = -15;
            vig.transform.position = new Vector3(0f, 0f, 0.5f);
            vig.transform.localScale = new Vector3(vw * 1.2f, vh * 1.2f, 1f);
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
            // 물감처럼 덧칠되도록 — 세포색보다 어둡게, 알파는 조금씩 다르게(겹칠수록 불균일하게 짙어짐)
            float a = Random.Range(0.44f, 0.62f);
            var c = new Color(col.r * 0.78f, col.g * 0.78f, col.b * 0.78f, a);
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
            var go = new GameObject("CursorField");
            cursorRing = go.transform;
            cursorSr = go.AddComponent<SpriteRenderer>();
            cursorSr.sprite = discSprite;
            cursorSr.sharedMaterial = spriteMat;
            // 밝은 유리 배경에서도 보이도록 진한 청록 + 알파 상향 (항체 영역)
            cursorSr.color = new Color(0.06f, 0.34f, 0.34f, 0.26f);
            cursorSr.sortingOrder = 40;
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

        int ShardReward(int score) => Mathf.FloorToInt(score / 8f);

        // 환생 — 시간여행. 진행 리셋, 메타는 유지, shard 획득.
        void Prestige()
        {
            metaCurrency += ShardReward(maxScore);
            bestScore = Mathf.Max(bestScore, maxScore);
            gold = 2000 * MetaLv("m_start");
            bought.Clear();
            stagesCleared = 0;
            currentStage = 0;
            retryCount = Mathf.Max(0, retryCount / 2);
            maxScore = 0;
            RebuildBaseStats();
            stats = baseStats.Clone();
            GrabRoot();
            wave.LoadStage(0);
            SaveProgress();
            state = State.Map;
        }

        void BuyMeta(UpgradeTree.MetaNode m)
        {
            int lv = MetaLv(m.id);
            if (lv >= m.maxLv || metaCurrency < m.cost) return;
            metaCurrency -= m.cost;
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
                metaCurrency = metaCurrency,
            };
            foreach (var id in bought) d.bought.Add(id);
            foreach (var kv in metaLv) if (kv.Value > 0) d.metaBought.Add(kv.Key + ":" + kv.Value);
            SaveSystem.Save(d);
        }

        void LoadProgress()
        {
            var d = SaveSystem.Load();
            hasSave = d != null && d.HasProgress;
            metaLv.Clear();
            metaCurrency = 0;
            if (d != null)
            {
                metaCurrency = d.metaCurrency;
                foreach (var e in d.metaBought)
                {
                    var pp = e.Split(':');
                    if (pp.Length == 2 && int.TryParse(pp[1], out var lv)) metaLv[pp[0]] = lv;
                }
            }
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
            GrabRoot();
            gold = d.gold;
            stagesCleared = d.stagesCleared;
            currentStage = Mathf.Clamp(d.currentStage, 0, StageConfig.Stages.Length - 1);
            retryCount = d.retryCount;
            maxScore = d.maxScore;
            foreach (var id in d.bought)
            {
                var n = NodeById(id);
                if (n != null && bought.Add(id)) n.apply(stats);
            }
            wave.LoadStage(currentStage);
            state = State.Map;
        }

        void NewGame()
        {
            // 무식하게: 이전 세션·디버그·스테일 어셈블리가 남긴 적/연출 오브젝트를 씬에서 싹 제거
            PurgeSpawnedObjects();
            // 스프라이트/머티리얼 안전망 (Awake가 어중간하게 끝났을 경우 대비)
            if (spriteMat == null)  spriteMat = new Material(Shader.Find("Sprites/Default"));
            if (cellSprite == null) cellSprite = BuildCellSprite();
            if (discSprite == null) discSprite = BuildDiscSprite();

            gold = 0;
            bought.Clear();
            stats = baseStats != null ? baseStats.Clone() : stats;
            GrabRoot();
            gold = 2000 * MetaLv("m_start");
            currentStage = 0;
            stagesCleared = 0;
            retryCount = 0;
            wave.LoadStage(currentStage);
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

        Enemy SpawnEnemy()
        {
            if (enemies.Count >= wave.maxEnemies) return null;

            if (cellSprite == null) cellSprite = BuildCellSprite();
            if (spriteMat == null) spriteMat = new Material(FindSpriteShader());

            var go = new GameObject("Enemy");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = cellSprite;
            sr.sharedMaterial = spriteMat;
            sr.sortingOrder = 10;
            sr.enabled = true;

            float sizeMul = Random.Range(wave.enemySizeRange.x, wave.enemySizeRange.y);

            var e = new Enemy
            {
                go = go,
                tr = go.transform,
                sr = sr,
                r = wave.enemyRadius * sizeMul,
                baseColor = WaveColor(waveNum),
                wobSpeed = Random.Range(1.4f, 3.4f),
                wobPhase = Random.value * 6.2832f,
                spawnWave = waveNum,
            };
            e.hpMax = e.hp = Mathf.CeilToInt(wave.EnemyHpAt(waveNum) * sizeMul);

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

            enemies.Add(e);
            return e;
        }

        void SpawnBoss()
        {
            if (boss != null) return;
            if (cellSprite == null) cellSprite = BuildCellSprite();
            if (spriteMat == null) spriteMat = new Material(FindSpriteShader());

            var go = new GameObject("Boss");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = cellSprite;
            sr.sharedMaterial = spriteMat;
            sr.sortingOrder = 12;
            sr.enabled = true;

            boss = new Enemy
            {
                go = go,
                tr = go.transform,
                sr = sr,
                r = 2.2f,
                baseColor = new Color(0.80f, 0.12f, 0.14f), // 위협적인 진한 빨강
                wobSpeed = 0.9f,
                wobPhase = 0f,
                spawnWave = wave.totalWaves,
            };
            boss.hpMax = boss.hp = wave.bossHp * (bossesKilled + 1);  // 무한 모드마다 +100%
            float ang = Random.value * Mathf.PI * 2f;
            boss.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.35f;
            boss.tr.position = Vector3.zero;
            boss.tr.localScale = Vector3.one * (boss.r * 2f);
            sr.color = boss.baseColor;

            enemies.Add(boss); // 이동·피격·타격 로직을 그대로 태움
            if (sound != null) sound.Play(Sfx.Boss, 0.9f);
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
                else if (state == State.Win)     { state = State.Title; }
                else if (state == State.Map)     { state = State.Title; }
                else if (state == State.Meta)    { state = State.Map; }
                else if (state == State.Result)  { state = State.Map; }
                else if (state == State.Tree)    { pauseReturn = state; state = State.Paused; }
            }

            // F12 — 치트: 모든 업그레이드 즉시 해금
            if (kb != null && kb.f12Key.wasPressedThisFrame && nodes != null)
            {
                foreach (var n in nodes)
                    if (!bought.Contains(n.id)) { bought.Add(n.id); n.apply(stats); }
            }

            if (state != State.Playing) return;

            timeLeft -= dt;
            if (timeLeft <= 0f) { timeLeft = 0f; OnTimeout(); return; }

            int guard;
            if (waveNum < wave.totalWaves)   // 보스 웨이브엔 일반 적 안 나옴
            {
                spawnTimer += dt;
                guard = 0;
                while (spawnTimer >= spawnInterval && guard++ < 24)
                {
                    spawnTimer -= spawnInterval;
                    int n = Mathf.Max(2, stats.spawnCount);   // 한 번에 최소 2마리 (노드로 최대 5)
                    for (int k = 0; k < n; k++) SpawnEnemy();
                }
                if (enemies.Count >= wave.maxEnemies)
                    spawnTimer = Mathf.Min(spawnTimer, spawnInterval);
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

                float dd = e.r * 2f;
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
                    manualClickCd = Mathf.Max(0.34f, stats.attackInterval * 1.12f);   // 자동보다 항상 느림
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
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                float dx = e.tr.position.x - cursorWorld.x;
                float dy = e.tr.position.y - cursorWorld.y;
                float rr = R + e.r;
                if (dx * dx + dy * dy > rr * rr) continue;

                // 펄스당 1번만 — 공격 간격이 곧 리듬이 된다 (적 수와 무관하게 일정)
                if (!hitAny) { hitAny = true; if (sound != null) sound.Play(Sfx.Pulse, 0.5f); }

                var (amount, crit) = stats.RollDamage();
                e.hp -= amount;
                e.flash = 0.08f;

                if (floaters.Count < 40)
                    floaters.Add(new Floater
                    {
                        world = e.tr.position + Vector3.up * (e.r + 0.1f),
                        life = 0.55f,
                        text = Mathf.RoundToInt(amount).ToString() + (crit ? "!" : ""),
                        crit = crit,
                    });

                if (e.hp <= 0f)
                {
                    bool wasBoss = e == boss;
                    int g = Mathf.RoundToInt(wave.GoldAt(e.spawnWave) * (1f + stats.goldMultPercent / 100f));
                    SpawnBurst(e.tr.position, e.baseColor, e.r);
                    SpawnSplat(e.tr.position, e.baseColor, e.r);
                    if (wasBoss) { SpawnBurst(e.tr.position, e.baseColor, e.r * 1.6f); SpawnBurst(e.tr.position, Color.white, e.r); }
                    if (goldFloats.Count < 30)
                        goldFloats.Add(new GoldFloat { startWorld = e.tr.position, age = 0f, life = 1.0f, amount = g });

                    if (e.go) Destroy(e.go);
                    enemies.RemoveAt(i);
                    kills++;
                    cycleKills++;
                    gold += g;
                    cycleGold += g;

                    if (wasBoss)
                    {
                        boss = null; bossesKilled++;
                        if (sound != null) sound.Play(Sfx.Win, 1f);
                        stagesCleared = Mathf.Max(stagesCleared, currentStage + 1);
                        maxScore = Mathf.Max(maxScore, stagesCleared * 100);
                        SaveProgress();
                        if (currentStage >= StageConfig.Stages.Length - 1) state = State.Win;  // 마지막 지역 = 엔딩
                        else state = State.Map;
                        return;
                    }
                    if (sound != null) sound.PlayKill(crit);
                    if (kills >= quota) { OnWaveClear(); return; }
                }
            }
        }

        void UpdateCursor()
        {
            if (Mouse.current == null) return;
            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, -cam.transform.position.z));
            w.z = 0f;
            cursorWorld = w;
            cursorRing.position = w;
            float pulse = 1f + 0.04f * Mathf.Sin(Time.time * 4f);
            cursorRing.localScale = Vector3.one * (stats.cursorRadius * 2f * pulse);
            cursorSr.enabled = state == State.Playing;
        }

        // ---- 스킬트리 로직 ----
        bool IsBought(string id) => bought.Contains(id);

        bool IsBuyable(UpgradeNode n)
        {
            if (IsBought(n.id)) return false;
            if (n.tier > stagesCleared) return false;   // 지역 N 클리어 전엔 tier N 잠김
            if (n.IsRoot) return true;
            return IsBought(n.parentId);
        }

        UpgradeNode NodeById(string id) => nodes.Find(m => m.id == id);

        // target 까지 오는 데 필요한 (아직 안 산) 노드들 — 루트 쪽부터 순서대로
        List<UpgradeNode> UnboughtPath(UpgradeNode target)
        {
            var path = new List<UpgradeNode>();
            for (var n = target; n != null && !IsBought(n.id); n = n.IsRoot ? null : NodeById(n.parentId))
                path.Add(n);
            path.Reverse();
            return path;
        }

        (int steps, int cost) PathCost(UpgradeNode target)
        {
            int c = 0, s = 0;
            foreach (var n in UnboughtPath(target)) { c += n.cost; s++; }
            return (s, c);
        }

        // 노드를 누르면, 그 앞의 안 산 노드들까지 "살 수 있는 만큼" 한 번에 구매
        void BuyPath(UpgradeNode target)
        {
            var path = UnboughtPath(target);
            if (path.Count == 0) return;
            var first = path[0];
            if (!first.IsRoot && !IsBought(first.parentId)) return; // 방어 (연쇄상 있을 수 없음)
            int boughtNow = 0;
            foreach (var n in path)
            {
                if (n.tier > stagesCleared) break;   // 잠긴 지역 tier 는 못 삼
                if (gold < n.cost) break;
                gold -= n.cost;
                bought.Add(n.id);
                n.apply(stats);
                boughtNow++;
            }
            if (boughtNow > 0)
            {
                if (sound != null) sound.Play(Sfx.Buy, 0.75f, boughtNow > 1 ? 1.12f : 1f);
                SaveProgress();
            }
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

        // 이 노드를 사면(경로 포함) 스탯이 어떻게 되는지 — 트리 화면 미리보기용
        Stats PreviewStats(UpgradeNode target)
        {
            var s = stats.Clone();
            foreach (var n in UnboughtPath(target)) n.apply(s);
            return s;
        }

        // 현재 능력치 한 줄. preview가 현재와 다르면 "지금 → 바뀔 값"으로 보여준다.
        static readonly Color Up = new Color(0.49f, 0.92f, 0.62f);   // 상승 표시(초록)

        string[] StatLabels => new[]
        { Loc.T("stat.dmg"), Loc.T("stat.crit"), Loc.T("stat.aspd"), Loc.T("stat.range"),
          Loc.T("stat.gold"), Loc.T("stat.spawn"), Loc.T("stat.time"), Loc.T("stat.startWave") };

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
            };
        }

        // 트리 화면 좌측 능력치 패널. 반환값 = 패널이 차지한 폭(트리를 그 오른쪽에 배치하기 위해)
        // 패널 크기·위치를 한 곳에서 계산 — DrawTree 의 트리 배치와 DrawStatPanel 이 같은 값을 쓴다.
        // 우측 상단 고정, 폭은 화면의 30% 이내, 높이는 하단 바 위 공간 안으로 클램프.
        Rect StatPanelRect(float barH)
        {
            int fs0 = Mathf.RoundToInt(14f * uiScale);
            float w = Mathf.Min(300f * uiScale, Screen.width * 0.30f);
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
            float x = area.x, y = area.y;

            GUI.color = new Color(0.13f, 0.14f, 0.17f, 0.92f);
            GUI.DrawTexture(new Rect(x, y, panelW, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.56f, 0.62f, 0.58f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, panelW, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float ix = x + pad, iw = panelW - pad * 2f, iy = y + pad;
            GUI.Label(new Rect(ix, iy, iw, rowH), Loc.T("stat.cur"), head);
            iy += rowH * 1.2f;

            for (int i = 0; i < labels.Length; i++)
            {
                GUI.Label(new Rect(ix, iy, iw * 0.46f, rowH), labels[i], lab);
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
                    GUI.Label(new Rect(ix + iw * 0.46f, iy, iw * 0.54f, rowH), cur[i], valS);
                }
                iy += rowH;
            }

            var nodeS = new GUIStyle(lab) { alignment = TextAnchor.MiddleRight };
            nodeS.normal.textColor = new Color(0.62f, 0.64f, 0.68f);
            FlatText(nodeS);
            GUI.Label(new Rect(ix, iy, iw * 0.46f, rowH), Loc.T("stat.nodes"), lab);
            GUI.Label(new Rect(ix + iw * 0.46f, iy, iw * 0.54f, rowH),
                      $"{(bought != null ? bought.Count : 0)} / {(nodes != null ? nodes.Count : 0)}", nodeS);
        }

        // 라벨 버튼 전부 이걸 통해 그린다 — 클릭음이 한 곳에서 붙도록
        bool UiBtn(Rect r, string label, GUIStyle st)
        {
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
            DrawTransition();   // 눈꺼풀은 항상 맨 위
        }

        void DrawScreens()
        {
            EnsureStyles();

            // 에디터에서 플레이 중 스크립트 재컴파일 → 도메인 리로드로 Awake 산출물이 날아갈 수 있음. 복구.
            if (cam == null) cam = Camera.main;
            if (nodes == null) nodes = UpgradeTree.BuildAll();
            if (iconTex == null) iconTex = NodeIcons.Build();
            if (spriteMat == null) spriteMat = new Material(FindSpriteShader());
            if (gridTile == null) gridTile = BuildGridTile();
            if (bought == null) bought = new HashSet<string>();          // 직렬화 안 됨
            if (splatSprite == null) splatSprite = BuildSplatSprite();
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
                // 일시정지 배경 = ESC를 누른 화면
                if (pauseReturn == State.Tree) DrawTree();
                else if (pauseReturn == State.Result) DrawResult();
                else { DrawHUD(); DrawGameplayOverlays(); }
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

            // 배경이 밝은 유리색이라 HUD 글자는 전부 진한 색으로 (흰 글씨는 안 보임)
            var hGold = new GUIStyle(sGold) { normal = { textColor = HudGold } }; FlatText(hGold);
            var hCen  = new GUIStyle(sCenter) { normal = { textColor = InkDark } }; FlatText(hCen);

            GUI.Label(new Rect(14f * s, 10f * s, 520f * s, 32f * s), $"$ {gold:N0}", hGold);
            GUI.Label(new Rect(0, 8f * s, Screen.width, 30f * s),
                bossWave ? Loc.T("hud.boss") : Loc.F("hud.wave", waveNum, wave.totalWaves), hCen);

            var tStyle = new GUIStyle(sBig)
            { normal = { textColor = timeLeft <= 5f ? new Color(0.72f, 0.06f, 0.08f) : InkDark } };
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
                    normal = { textColor = f.crit ? new Color(0.60f, 0.20f, 0.02f, a) : new Color(0.10f, 0.13f, 0.16f, a) }
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
                    normal = { textColor = new Color(HudGold.r, HudGold.g, HudGold.b, alpha) }
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

                if (rPix >= 9f)
                {
                    string hpTxt = Mathf.CeilToInt(e.hp).ToString("N0");   // 1,234
                    // 세포 크기 대비 자릿수만큼 폰트 축소 → 셀 밖으로 안 삐져나가고 안 잘림
                    int fs2 = Mathf.Clamp(Mathf.RoundToInt(rPix * 2.4f / Mathf.Max(2, hpTxt.Length)), 8, 40);
                    var ns = new GUIStyle(sLabel)
                    { alignment = TextAnchor.MiddleCenter, fontSize = fs2, fontStyle = FontStyle.Bold };
                    var ctr = new Vector2(esp.x, ey);
                    ns.normal.textColor = new Color(0f, 0f, 0f, 0.75f); // 검은 헤일로 (4방향)
                    var box = FitRect(ns, hpTxt, ctr);
                    GUI.Label(new Rect(box.x + 1f, box.y + 1f, box.width, box.height), hpTxt, ns);
                    GUI.Label(new Rect(box.x - 1f, box.y - 1f, box.width, box.height), hpTxt, ns);
                    GUI.Label(new Rect(box.x + 1f, box.y - 1f, box.width, box.height), hpTxt, ns);
                    GUI.Label(new Rect(box.x - 1f, box.y + 1f, box.width, box.height), hpTxt, ns);
                    ns.normal.textColor = new Color(0.98f, 0.98f, 1f);
                    GUI.Label(box, hpTxt, ns);
                }
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
                fromPlay ? Loc.T("pause.resume") : Loc.T("pause.return"), bst)) state = pauseReturn;
            if (fromPlay && UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                Loc.T("pause.endWave"), bst)) state = State.Result;
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                Loc.T("title.settings"), bst)) { settingsReturn = State.Paused; state = State.Settings; }
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                Loc.T("common.toTitle"), bst)) state = State.Title;
        }

        // ---- 지역(스테이지) 선택 맵 ----
        void DrawMap()
        {
            FillScreen(Glass);
            DrawGridOverlay(GlassGrid);
            float w = Screen.width, h = Screen.height, s = uiScale;

            var tt = new GUIStyle(sBig) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(34f * s), normal = { textColor = InkDark } };
            FlatText(tt);
            GUI.Label(new Rect(0, h * 0.06f, w, 60f * s), Loc.T("map.title"), tt);

            var sub = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(13f * s), normal = { textColor = new Color(0.20f,0.24f,0.28f) } };
            FlatText(sub);
            GUI.Label(new Rect(0, h * 0.06f + 44f * s, w, 26f * s),
                Loc.F("map.info", gold.ToString("N0"), retryCount, stagesCleared, StageConfig.Stages.Length), sub);

            // 8개 타일 2행 × 4열
            int cols = 4;
            float gap = 18f * s;
            float gw = Mathf.Min(220f * s, (w * 0.94f - (cols - 1) * gap) / cols);   // 작은 창에서도 안 넘치게
            float gh = gw * 0.44f;
            float gridW = cols * gw + (cols - 1) * gap;
            float x0 = (w - gridW) * 0.5f, y0 = h * 0.26f;
            var name = new GUIStyle(sBtn) { fontSize = Mathf.RoundToInt(14f * s), wordWrap = true };
            var meta = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(11f * s), normal = { textColor = new Color(0.30f,0.33f,0.37f) } };
            FlatText(meta);

            for (int i = 0; i < StageConfig.Stages.Length; i++)
            {
                var st = StageConfig.Stages[i];
                int col = i % cols, row = i / cols;
                var r = new Rect(x0 + col * (gw + gap), y0 + row * (gh + gap + 22f * s), gw, gh);
                bool unlocked = i <= stagesCleared;
                bool cleared = i < stagesCleared;

                var gc = GUI.color;
                GUI.color = unlocked ? new Color(st.theme.r, st.theme.g, st.theme.b, cleared ? 0.55f : 0.95f)
                                     : new Color(0.55f, 0.57f, 0.60f, 0.5f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(0f, 0f, 0f, 0.28f);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 3f, r.width, 3f), Texture2D.whiteTexture);
                GUI.color = gc;

                var lab = new GUIStyle(name) { normal = { textColor = unlocked ? Color.white : new Color(0.85f,0.86f,0.88f) } };
                FlatText(lab);
                if (unlocked)
                {
                    if (UiBtn(r, i + 1 + ". " + Loc.T(st.name), lab))
                    { currentStage = i; BeginTransition(StartRun); }
                }
                else
                {
                    GUI.Label(r, (i + 1) + ". ???", lab);
                    GUI.Label(new Rect(r.x, r.yMax + 2f, r.width, 20f * s), Loc.T("map.locked"), meta);
                }
                if (unlocked)
                    GUI.Label(new Rect(r.x, r.yMax + 2f, r.width, 20f * s),
                        cleared ? Loc.T("map.cleared") : Loc.F("map.wavesBoss", st.waves), meta);
            }

            var bst = Btn(Mathf.RoundToInt(13f * s));
            bst.normal.textColor = InkDark; FlatText(bst);
            float bw = 176f * s, bh = 44f * s, bgap = 12f * s;
            bool canRebirth = stagesCleared >= 2;
            int nbtn = canRebirth ? 3 : 2;
            float totw = nbtn * bw + (nbtn - 1) * bgap;
            float bx = (w - totw) * 0.5f, byy = h * 0.87f;
            if (UiBtn(new Rect(bx, byy, bw, bh), Loc.T("map.upgrade"), bst))
            { state = State.Tree; treeZoom = 0f; treePan = Vector2.zero; }
            bx += bw + bgap;
            if (UiBtn(new Rect(bx, byy, bw, bh), metaCurrency > 0 ? Loc.F("map.metaN", metaCurrency) : Loc.T("map.meta"), bst))
                state = State.Meta;
            bx += bw + bgap;
            if (canRebirth)
            {
                if (UiBtn(new Rect(bx, byy, bw, bh), Loc.F("map.rebirth", ShardReward(maxScore)), bst))
                    BeginTransition(Prestige);
            }
            GUI.Label(new Rect(0, byy + bh + 4f * s, w, 20f * s),
                canRebirth ? Loc.F("map.rebirthInfo", ShardReward(maxScore), metaCurrency)
                           : Loc.T("map.rebirthLock"), sub);
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
                int lv = MetaLv(m.id);
                var r = new Rect(lx, ly + i * (rowH + 8f * s), listW, rowH);
                var gc = GUI.color;
                GUI.color = new Color(0.24f, 0.22f, 0.30f, 0.92f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = gc;
                GUI.Label(new Rect(r.x + 14f * s, r.y + 6f * s, listW * 0.62f, 22f * s), Loc.F("meta.lv", Loc.T(m.label), lv, m.maxLv), name);
                GUI.Label(new Rect(r.x + 14f * s, r.y + 28f * s, listW * 0.62f, 20f * s), Loc.T(m.desc), desc);
                bool maxed = lv >= m.maxLv;
                bool afford = metaCurrency >= m.cost;
                string btxt = maxed ? Loc.T("meta.max") : Loc.F("meta.cost", m.cost);
                var br = new Rect(r.xMax - 130f * s, r.y + 8f * s, 116f * s, rowH - 16f * s);
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
            FillScreen(Glass);
            DrawGridOverlay(GlassGrid);
            float w = Screen.width, h = Screen.height;

            var tt = new GUIStyle(sBig)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(64f * uiScale),
              normal = { textColor = InkDark } };
            FlatText(tt);
            GUI.Label(new Rect(0, h * 0.13f, w, Mathf.RoundToInt(90f * uiScale)), "POP Cell", tt);
            var sub = new GUIStyle(sLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15f * uiScale),
              normal = { textColor = new Color(0.16f, 0.20f, 0.24f) } };
            FlatText(sub);
            GUI.Label(new Rect(0, h * 0.13f + 92f * uiScale, w, 34f * uiScale), Loc.T("title.sub"), sub);

            // 버튼: 기존 대비 1.2배 + 화면 비례(uiScale). 밝은 배경이라 글자는 검게.
            float bw = 260f * 1.2f * uiScale, bh = 54f * 1.2f * uiScale, gap = 12f * uiScale, by = h * 0.56f;
            var bst = Btn(Mathf.RoundToInt(17f * 1.2f * uiScale));
            bst.normal.textColor = InkDark; FlatText(bst);
            int rowN = 0;
            if (hasSave)
            {
                if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * rowN++, bw, bh), Loc.T("title.continue"), bst))
                    BeginTransition(ContinueGame);
                if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * rowN++, bw, bh), Loc.T("title.new"), bst))
                    BeginTransition(() => { SaveSystem.Wipe(); NewGame(); });
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

            // 언어 선택
            float ylang = y0 + rowH * 2f + 24f * uiScale;
            GUI.Label(new Rect(x0, ylang, labelW, rowH), Loc.T("set.lang"), row);
            float lbw = 118f * uiScale, lbg = 8f * uiScale, lbx = x0 + labelW + 14f;
            for (int li = 0; li < 3; li++)
            {
                var lb = Btn(Mathf.RoundToInt(13f * uiScale));
                bool sel = (int)Loc.Cur == li;
                if (sel) { lb.normal.textColor = new Color(0.49f, 0.92f, 0.62f); FlatText(lb); }
                if (UiBtn(new Rect(lbx + li * (lbw + lbg), ylang + 4f * uiScale, lbw, rowH - 8f * uiScale),
                          (sel ? "▸ " : "") + Loc.LangNames[li], lb))
                    Loc.SetLang((Lang)li);
            }

            GUI.Label(new Rect(0, ylang + rowH + 30f * uiScale, w, 40f * uiScale),
                      Loc.T("set.res"),
                      new GUIStyle(row) { alignment = TextAnchor.MiddleCenter });

            if (UiBtn(new Rect(w * 0.5f - 110f, h * 0.66f, 220f, 50f), Loc.T("common.back"), Btn(16)))
            {
                if (audioDirty && sound != null) { sound.Save(); audioDirty = false; }
                state = settingsReturn;
            }
        }

        // 엔드 화면 — 뒤 게임화면이 비치는 반투명, 중앙 640×360 패널
        void DrawResult()
        {
            FillScreen(new Color(0.10f, 0.11f, 0.13f, 0.55f)); // 딤 (게임화면 비침)

            const float pw = 640f, ph = 360f;
            var panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
            var c = GUI.color;
            GUI.color = new Color(0.16f, 0.17f, 0.19f, 0.93f);       // 어두운 반투명 패널
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.62f, 0.58f, 0.7f);        // 상단 라인
            GUI.DrawTexture(new Rect(panel.x, panel.y, pw, 2f), Texture2D.whiteTexture);
            GUI.color = c;

            float cx = panel.center.x;
            var title = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(panel.x, panel.y + 24f, pw, 40f),
                Loc.F("res.title", currentStage + 1, waveNum), title);

            var head  = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = new Color(0.68f, 0.72f, 0.70f) } };
            var big   = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 19, fontStyle = FontStyle.Bold };
            var bigY  = new GUIStyle(big)  { normal = { textColor = Gold } };
            var headY = new GUIStyle(head) { normal = { textColor = Gold } };
            float ly = panel.y + 94f, lh = 33f;
            GUI.Label(new Rect(panel.x, ly, pw, lh), Loc.T("res.cycle"), head);
            GUI.Label(new Rect(panel.x, ly + lh, pw, lh), Loc.F("res.kills", cycleKills), big);
            GUI.Label(new Rect(panel.x, ly + lh * 2f, pw, lh), Loc.F("res.gain", cycleGold.ToString("N0")), bigY);
            GUI.Label(new Rect(panel.x, ly + lh * 3f + 6f, pw, lh), Loc.F("res.have", gold.ToString("N0")), headY);

            const float bw = 200f, bh = 46f, gap = 16f;
            float by = panel.yMax - bh - 26f;
            var bst = Btn(14);
            if (UiBtn(new Rect(cx - bw * 1.5f - gap, by, bw, bh), Loc.T("map.upgrade"), bst))
            { state = State.Tree; treeZoom = 0f; treePan = Vector2.zero; }
            if (UiBtn(new Rect(cx - bw * 0.5f, by, bw, bh), Loc.T("res.retry"), bst))
            { BeginTransition(StartRun); }
            if (UiBtn(new Rect(cx + bw * 0.5f + gap, by, bw, bh), Loc.T("common.toMap"), bst))
            { state = State.Map; }
        }

        // 보스 처치 = 엔딩
        void DrawWin()
        {
            FillScreen(new Color(0.10f, 0.11f, 0.13f, 0.62f));
            const float pw = 640f, ph = 340f;
            var panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
            var c = GUI.color;
            GUI.color = new Color(0.14f, 0.17f, 0.16f, 0.95f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.90f, 0.55f, 0.9f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, pw, 3f), Texture2D.whiteTexture);
            GUI.color = c;

            float cx = panel.center.x;
            var title = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.95f, 0.62f) } };
            GUI.Label(new Rect(panel.x, panel.y + 34f, pw, 48f), Loc.T("win.title"), title);

            var head = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            var goldL = new GUIStyle(head) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } };
            float ly = panel.y + 118f, lh = 34f;
            GUI.Label(new Rect(panel.x, ly, pw, lh), Loc.F("win.line1", cycleKills), head);
            GUI.Label(new Rect(panel.x, ly + lh, pw, lh), Loc.F("win.line2", cycleGold.ToString("N0"), gold.ToString("N0")), goldL);

            const float bw = 240f, bh = 50f, gap = 20f;
            float by = panel.yMax - bh - 28f;
            var bst = Btn(16);
            if (UiBtn(new Rect(cx - bw - gap * 0.5f, by, bw, bh), Loc.T("common.toTitle"), bst)) state = State.Title;
            if (UiBtn(new Rect(cx + gap * 0.5f, by, bw, bh), Loc.T("win.endless"), bst))
            { waveNum = wave.totalWaves - 1; timeLeft = 0f; AdvanceWave(); state = State.Playing; }
        }

        // 부모 a → 자식 b 를 ㄱ자(축 정렬) 선으로 연결. 회전을 안 쓰므로 확대/이동해도 노드에 딱 붙는다.
        static void DrawConnector(Vector2 a, Vector2 b)
        {
            const float t = 3f;
            float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x);
            float y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);
            // a의 높이에서 가로로, b의 x에서 세로로 (직각 꺾임)
            GUI.DrawTexture(new Rect(x0 - t * 0.5f, a.y - t * 0.5f, (x1 - x0) + t, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(b.x - t * 0.5f, y0 - t * 0.5f, t, (y1 - y0) + t), Texture2D.whiteTexture);
        }

        void DrawTree()
        {
            FillScreen(new Color(0.27f, 0.28f, 0.31f, 1f));
            DrawGridOverlay(new Color(0.42f, 0.44f, 0.48f, 1f));
            var c0 = GUI.color;

            // 하단 바 높이 + 좌측 능력치 패널 폭을 먼저 알아야 트리를 남는 공간 중앙에 맞출 수 있다
            float barH = 118f * uiScale;
            Rect statRect = StatPanelRect(barH);   // 우측 상단 능력치 패널

            // firstRing: 코어에서 첫 노드까지의 반경. 9갈래가 40° 간격이라 spacing을 그대로 쓰면
            //   현(chord) = 2·R·sin20° 이 노드 크기보다 작아져 가운데에서 칩이 서로 겹친다.
            //   R=104 → 현 ≈ 71px, 노드 40px → 좌우 31px 여유.
            const float nodeSz = 40f, spacing = 56f, firstRing = 120f;   // 5/4 클러스터라 첫 링을 넓게

            Vector2 pivot = new Vector2(Screen.width * 0.5f, (Screen.height - barH) * 0.5f);

            npos.Clear();
            foreach (var n in nodes)
                npos[n.id] = n.IsRoot ? pivot
                           : npos[n.parentId] + n.dir * (n.parentId == UpgradeTree.RootId ? firstRing : spacing);

            // 트리 전체가 들어가는 배율(= "화면 리셋" 기본값). 노드 수가 바뀌어도 알아서 맞는다.
            float maxR = 0f;
            foreach (var kv in npos) { float d = (kv.Value - pivot).magnitude; if (d > maxR) maxR = d; }
            maxR += nodeSz * 0.5f;
            // 화면 중앙 정렬은 유지하고, 반경이 좌측 패널을 침범하지 않는 선까지만 키운다
            float availW = Screen.width * 0.5f - 16f;
            float availH = (Screen.height - barH) * 0.5f - 16f;
            // 트리는 원형이라, 중심에서 패널 사각형까지의 거리보다 반경이 크면 겹친다.
            float dx = Mathf.Max(0f, Mathf.Max(statRect.xMin - pivot.x, pivot.x - statRect.xMax));
            float dy = Mathf.Max(0f, Mathf.Max(statRect.yMin - pivot.y, pivot.y - statRect.yMax));
            float dPanel = Mathf.Sqrt(dx * dx + dy * dy) - 14f;
            float lim = Mathf.Min(availW, availH);
            if (dPanel > 40f) lim = Mathf.Min(lim, dPanel);   // 창이 아주 작아 중심이 패널에 닿으면 무시
            float fitZoom = Mathf.Clamp(lim / Mathf.Max(1f, maxR), 0.10f, 2.2f);
            if (treeZoom <= 0f)
            {
                // 열 때는 '작업 중인 최전선'(코어+구매+구매가능 노드)에 확대해서 보여준다. fit-all 아님.
                Vector2 c = pivot; int cnt = 0;
                foreach (var n in nodes)
                {
                    if (n.IsRoot || IsBought(n.id) || IsBuyable(n)) { c += npos[n.id]; cnt++; }
                }
                if (cnt > 0) c = (c - pivot) / cnt; else c = pivot;
                treeZoom = Mathf.Clamp(Mathf.Max(fitZoom * 2.3f, 0.62f), fitZoom, 1.05f);
                treePan = -(c - pivot) * treeZoom;
            }

            Event ev = Event.current;
            if (ev.type == EventType.ScrollWheel)
            { treeZoom = Mathf.Clamp(treeZoom * (1f - ev.delta.y * 0.06f), fitZoom * 0.9f, 2.2f); ev.Use(); }
            else if (ev.type == EventType.MouseDrag && ev.button == 1)
            { treePan += ev.delta; ev.Use(); }

            Matrix4x4 saved = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(treeZoom, treeZoom), pivot);
            GUI.matrix = Matrix4x4.Translate(new Vector3(treePan.x, treePan.y, 0f)) * GUI.matrix;

            // 연결선
            GUI.color = new Color(0.56f, 0.62f, 0.58f, 0.55f);
            foreach (var n in nodes)
                if (!n.IsRoot) DrawConnector(npos[n.parentId], npos[n.id]);
            GUI.color = c0;

            // 노드 — 아이콘만. 효과는 마우스 오버 툴팁으로.
            UpgradeNode hovered = null;
            foreach (var n in nodes)
            {
                Vector2 p = npos[n.id];
                var rect = new Rect(p.x - nodeSz * 0.5f, p.y - nodeSz * 0.5f, nodeSz, nodeSz);
                bool bt = IsBought(n.id);
                bool by = IsBuyable(n);                    // 부모가 뚫려 바로 살 수 있음
                bool pathAfford = !bt && gold >= PathCost(n).cost;  // 경로까지 계산해 한 번에 살 수 있음

                bool locked = n.tier > stagesCleared;
                //  구매함 = 파란색(가득 채움+테두리)  ·  지금 살 수 있음 = 초록  ·  살 순 있으나 골드 부족 = 갈색  ·  잠김 = 어두움
                GUI.color = locked ? new Color(0.10f, 0.10f, 0.12f, 0.9f)
                         : bt ? new Color(0.16f, 0.40f, 0.66f, 1f)
                         : pathAfford ? new Color(0.16f, 0.44f, 0.24f, 1f)
                         : by ? new Color(0.40f, 0.30f, 0.14f, 1f)
                         : new Color(0.13f, 0.14f, 0.16f, 1f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                {
                    Color border = bt ? new Color(0.60f, 0.82f, 1f)          // 구매함 = 밝은 하늘색 테두리
                                 : pathAfford ? new Color(0.49f, 0.92f, 0.62f)
                                 : by ? new Color(0.95f, 0.68f, 0.42f)
                                 : new Color(0.34f, 0.36f, 0.40f);
                    float bt2 = bt ? 3f : 2f;                                 // 구매함은 더 굵게
                    GUI.color = border;
                    GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, bt2), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x, rect.yMax - bt2, rect.width, bt2), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x, rect.y, bt2, rect.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.xMax - bt2, rect.y, bt2, rect.height), Texture2D.whiteTexture);
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
                if (!bt && !locked && GUI.Button(rect, GUIContent.none, GUIStyle.none) && transDir == 0)
                    BuyPath(n);   // 앞의 안 산 노드들까지 살 수 있는 만큼 한 번에
            }

            GUI.matrix = saved;

            DrawStatPanel(statRect, hovered);

            // 마우스 오버 툴팁 (스크린 좌표)
            if (hovered != null)
            {
                bool bt = IsBought(hovered.id);
                string status;
                if (bt) status = Loc.T("tree.owned");
                else if (hovered.IsRoot) status = Loc.T("tree.free");
                else if (IsBuyable(hovered)) status = $"${hovered.cost:N0}";
                else if (hovered.tier > stagesCleared) status = Loc.F("tree.lockTier", hovered.tier);
                else { var pc = PathCost(hovered); status = Loc.F("tree.pathBuy", pc.steps, pc.cost.ToString("N0")); }
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
                GUI.color = new Color(0.12f, 0.13f, 0.15f, 0.97f);
                GUI.DrawTexture(tr, Texture2D.whiteTexture);
                GUI.color = new Color(0.56f, 0.62f, 0.58f, 0.8f);
                GUI.DrawTexture(new Rect(tr.x, tr.y, tr.width, 2f), Texture2D.whiteTexture);
                GUI.color = c0;
                GUI.Label(new Rect(tr.x + padX, tr.y + padTop, innerW, topH), top, ts);
                GUI.Label(new Rect(tr.x + padX, tr.y + padTop + topH + gapMid, innerW, statusH), status, tsY);
            }

            // 고정 하단 바 — 화면 맨 아래에 그룹으로 고정(게임뷰가 살짝 잘려도 버튼이 안쪽에 오도록 여유 확보)
            // barH 는 위에서 이미 계산됨
            float startBtnW = 150f * uiScale, resetBtnW = 116f * uiScale, btnH = 34f * uiScale;
            float margin = 20f;
            GUI.BeginGroup(new Rect(0f, Screen.height - barH, Screen.width, barH));
            {
                var goldS = new GUIStyle(sGold) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(18f * uiScale), fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(margin, 8f, 520f * uiScale, 30f * uiScale), Loc.F("res.have", gold.ToString("N0")), goldS);

                var info = new GUIStyle(sLabel) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(13f * uiScale), wordWrap = true };
                float infoTop = 8f + 30f * uiScale;
                GUI.Label(new Rect(margin, infoTop, Screen.width - startBtnW - resetBtnW - margin * 3f, barH - infoTop - 6f),
                    Loc.T("tree.help"), info);

                var bst = Btn(Mathf.RoundToInt(14f * uiScale));
                float btnY = barH - btnH - margin;
                if (UiBtn(new Rect(Screen.width - startBtnW - resetBtnW - margin * 2f, btnY, resetBtnW, btnH), Loc.T("tree.reset"), bst))
                { treeZoom = 0f; treePan = Vector2.zero; }
                if (UiBtn(new Rect(Screen.width - startBtnW - margin, btnY, startBtnW, btnH), Loc.F("tree.startWaveN", Mathf.Max(1, stats.startWave)), bst))
                { BeginTransition(StartRun); }
            }
            GUI.EndGroup();
        }
    }
}
