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

        enum State { Title, Playing, Paused, Result, Tree, Settings, Win }
        State state = State.Title;
        State settingsReturn = State.Title;
        State pauseReturn = State.Playing;   // 일시정지에서 "계속/돌아가기" 시 복귀할 화면

        int gold, waveNum = 1, kills, quota;
        int cycleKills, cycleGold;
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
            cam.backgroundColor = new Color(0.40f, 0.41f, 0.44f); // 회색 바탕 (흰 글씨·진한 세포색 둘 다 잘 보이게)

            spriteMat = new Material(FindSpriteShader());
            cellSprite = BuildCellSprite();
            discSprite = BuildDiscSprite();
            splatSprite = BuildSplatSprite();
            gridTile = BuildGridTile();
            iconTex = NodeIcons.Build();

            BuildBackground();
            BuildCursor();

            nodes = UpgradeTree.BuildAll();
            baseStats = stats.Clone();

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
            sr.color = new Color(0.30f, 0.31f, 0.34f, 1f); // 그리드 선 (배경 0.40보다 어둡게 = 보임)
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
                ssr.color = new Color(0.30f, 0.32f, 0.38f, Random.Range(0.03f, 0.08f));
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
            vsr.color = new Color(0.05f, 0.06f, 0.08f, 0.50f);
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
            cursorSr.color = new Color(0.20f, 0.55f, 0.45f, 0.16f);
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
            StartRun();
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
            waveNum = Mathf.Clamp(stats.startWave, 1, wave.totalWaves);  // 스킵 노드 반영
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
                else if (state == State.Tree || state == State.Result) { pauseReturn = state; state = State.Paused; }
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
            while (attackTimer >= stats.attackInterval && state == State.Playing && guard++ < 10)
            {
                attackTimer -= stats.attackInterval;
                AttackPulse();
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
                        state = State.Win; return;
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
                if (gold < n.cost) break;
                gold -= n.cost;
                bought.Add(n.id);
                n.apply(stats);
                boughtNow++;
            }
            if (boughtNow > 0 && sound != null)
                sound.Play(Sfx.Buy, 0.75f, boughtNow > 1 ? 1.12f : 1f);   // 여러 개 한 번에 = 살짝 높게
        }

        // ============================================================
        // 렌더
        // ============================================================
        void EnsureStyles()
        {
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
        { "공격력", "치명타", "공격 속도", "공격 범위", "골드 획득", "소환", "제한 시간", "시작 웨이브" };

        string[] StatValues(Stats s)
        {
            float aps = 1f / Mathf.Max(0.0001f, s.attackInterval);
            return new[]
            {
                s.GetHitDamage().ToString("N0"),
                $"{s.critChance * 100f:0}% × {s.critMult:0.0}",
                $"초당 {aps:0.0}회",
                $"{s.cursorRadius:0.00}",
                $"+{s.goldMultPercent:0}%",
                $"×{s.spawnIntervalMult:0.00} · {s.spawnCount}마리",
                $"{wave.baseTimeLimit + s.bonusTimeSec:0}초",
                $"{Mathf.Max(1, s.startWave)}",
            };
        }

        // 트리 화면 좌측 능력치 패널. 반환값 = 패널이 차지한 폭(트리를 그 오른쪽에 배치하기 위해)
        void DrawStatPanel(float panelW, float barH, UpgradeNode hovered)
        {
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
            float x = 22f, y = Mathf.Max(20f, (Screen.height - barH) * 0.5f - h * 0.5f);

            GUI.color = new Color(0.13f, 0.14f, 0.17f, 0.92f);
            GUI.DrawTexture(new Rect(x, y, panelW, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.56f, 0.62f, 0.58f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, panelW, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float ix = x + pad, iw = panelW - pad * 2f, iy = y + pad;
            GUI.Label(new Rect(ix, iy, iw, rowH), "현재 능력치", head);
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
            GUI.Label(new Rect(ix, iy, iw * 0.46f, rowH), "해금 노드", lab);
            GUI.Label(new Rect(ix + iw * 0.46f, iy, iw * 0.54f, rowH),
                      $"{(bought != null ? bought.Count : 0)} / {(nodes != null ? nodes.Count : 0)}", nodeS);
        }

        // 라벨 버튼 전부 이걸 통해 그린다 — 클릭음이 한 곳에서 붙도록
        bool UiBtn(Rect r, string label, GUIStyle st)
        {
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
            GUI.Label(new Rect(14f * s, 10f * s, 520f * s, 32f * s), $"$ {gold:N0}", sGold);
            GUI.Label(new Rect(0, 8f * s, Screen.width, 30f * s),
                bossWave ? "── 보스 ──" : $"웨이브 {waveNum} / {wave.totalWaves}", sCenter);

            var tStyle = new GUIStyle(sBig)
            { normal = { textColor = timeLeft <= 5f ? new Color(1f, 0.45f, 0.45f) : Ink } };
            GUI.Label(new Rect(0, 30f * s, Screen.width, 44f * s), timeLeft.ToString("0.0"), tStyle);

            if (!bossWave)
                GUI.Label(new Rect(0, 72f * s, Screen.width, 28f * s), $"{kills} / {quota}", sCenter);
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
                    normal = { textColor = f.crit ? new Color(Gold.r, Gold.g, Gold.b, a) : new Color(0.97f, 0.97f, 0.98f, a) }
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
                    normal = { textColor = new Color(Gold.r, Gold.g, Gold.b, alpha) }
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
            GUI.Label(new Rect(0, h * 0.20f, w, 72f), fromPlay ? "일시정지" : "메뉴", title);

            float bw = 260f, bh = 52f, gap = 12f, by = h * 0.40f;
            var bst = Btn(16);
            int row = 0;
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                fromPlay ? "계속하기" : "돌아가기", bst)) state = pauseReturn;
            if (fromPlay && UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                "웨이브 종료 (상점으로)", bst)) state = State.Result;
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                "설정", bst)) { settingsReturn = State.Paused; state = State.Settings; }
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * row++, bw, bh),
                "타이틀로", bst)) state = State.Title;
        }

        void DrawTitle()
        {
            FillScreen(new Color(0.27f, 0.28f, 0.31f, 1f));
            DrawGridOverlay(new Color(0.42f, 0.44f, 0.48f, 1f));
            float w = Screen.width, h = Screen.height;

            var tt = new GUIStyle(sBig)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(64f * uiScale) };
            GUI.Label(new Rect(0, h * 0.13f, w, Mathf.RoundToInt(90f * uiScale)), "POP Cell", tt);
            var sub = new GUIStyle(sLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15f * uiScale), normal = { textColor = new Color(0.70f, 0.73f, 0.78f) } };
            GUI.Label(new Rect(0, h * 0.13f + 92f * uiScale, w, 34f * uiScale), "세포 팝 — 인크리멘탈 클리커", sub);

            float bw = 260f, bh = 54f, gap = 12f, by = h * 0.56f;
            var bst = Btn(17);
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by, bw, bh), "게임 시작", bst)) NewGame();
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap), bw, bh), "설정", bst)) { settingsReturn = State.Title; state = State.Settings; }
            if (UiBtn(new Rect(w * 0.5f - bw * 0.5f, by + (bh + gap) * 2f, bw, bh), "게임 종료", bst)) QuitGame();
        }

        void DrawSettings()
        {
            FillScreen(new Color(0.27f, 0.28f, 0.31f, 1f));
            DrawGridOverlay(new Color(0.42f, 0.44f, 0.48f, 1f));
            float w = Screen.width, h = Screen.height;

            var title = new GUIStyle(sBig)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(30f * uiScale) };
            GUI.Label(new Rect(0, h * 0.16f, w, Mathf.RoundToInt(48f * uiScale)), "설정", title);

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
                GUI.Label(new Rect(x0, y0, labelW, rowH), "배경음악", row);
                before = sound.BgmVolume;
                float bv = GUI.HorizontalSlider(
                    new Rect(x0 + labelW + 14f, y0 + rowH * 0.5f - 9f, sliderW, 18f),
                    before, 0f, 1f);
                if (!Mathf.Approximately(bv, before)) { sound.BgmVolume = bv; audioDirty = true; }
                GUI.Label(new Rect(x0 + labelW + sliderW + 28f, y0, valW, rowH),
                          Mathf.RoundToInt(sound.BgmVolume * 100f) + "%", val);

                // 효과음
                float y1 = y0 + rowH + 18f * uiScale;
                GUI.Label(new Rect(x0, y1, labelW, rowH), "효과음", row);
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

            GUI.Label(new Rect(0, h * 0.34f + rowH * 2f + 46f * uiScale, w, 40f * uiScale),
                      "해상도      1920 × 1080            (준비 중)",
                      new GUIStyle(row) { alignment = TextAnchor.MiddleCenter });

            if (UiBtn(new Rect(w * 0.5f - 110f, h * 0.66f, 220f, 50f), "뒤로", Btn(16)))
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
            GUI.Label(new Rect(panel.x, panel.y + 24f, pw, 40f), $"— 웨이브 {waveNum} 종료 —", title);

            var head  = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = new Color(0.68f, 0.72f, 0.70f) } };
            var big   = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 19, fontStyle = FontStyle.Bold };
            var bigY  = new GUIStyle(big)  { normal = { textColor = Gold } };
            var headY = new GUIStyle(head) { normal = { textColor = Gold } };
            float ly = panel.y + 94f, lh = 33f;
            GUI.Label(new Rect(panel.x, ly, pw, lh), "이번 싸이클", head);
            GUI.Label(new Rect(panel.x, ly + lh, pw, lh), $"처치  {cycleKills}", big);
            GUI.Label(new Rect(panel.x, ly + lh * 2f, pw, lh), $"획득  +${cycleGold:N0}", bigY);
            GUI.Label(new Rect(panel.x, ly + lh * 3f + 6f, pw, lh), $"보유  ${gold:N0}", headY);

            const float bw = 220f, bh = 46f, gap = 20f;
            float by = panel.yMax - bh - 26f;
            var bst = Btn(15);
            if (UiBtn(new Rect(cx - bw - gap * 0.5f, by, bw, bh), "업그레이드", bst))
            { state = State.Tree; treeZoom = 0f; treePan = Vector2.zero; }
            if (UiBtn(new Rect(cx + gap * 0.5f, by, bw, bh), $"계속 (웨이브 {Mathf.Max(1, stats.startWave)})", bst))
            { StartRun(); }
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
            GUI.Label(new Rect(panel.x, panel.y + 34f, pw, 48f), "감염원 제거 완료 — CLEAR", title);

            var head = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            var goldL = new GUIStyle(head) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } };
            float ly = panel.y + 118f, lh = 34f;
            GUI.Label(new Rect(panel.x, ly, pw, lh), $"이번 판  ·  처치 {cycleKills}", head);
            GUI.Label(new Rect(panel.x, ly + lh, pw, lh), $"획득  +${cycleGold:N0}      보유  ${gold:N0}", goldL);

            const float bw = 240f, bh = 50f, gap = 20f;
            float by = panel.yMax - bh - 28f;
            var bst = Btn(16);
            if (UiBtn(new Rect(cx - bw - gap * 0.5f, by, bw, bh), "타이틀로", bst)) state = State.Title;
            if (UiBtn(new Rect(cx + gap * 0.5f, by, bw, bh), "계속 (무한)", bst))
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
            float panelW = 300f * uiScale;
            const float panelX = 22f, panelGap = 24f;

            // firstRing: 코어에서 첫 노드까지의 반경. 9갈래가 40° 간격이라 spacing을 그대로 쓰면
            //   현(chord) = 2·R·sin20° 이 노드 크기보다 작아져 가운데에서 칩이 서로 겹친다.
            //   R=104 → 현 ≈ 71px, 노드 40px → 좌우 31px 여유.
            const float nodeSz = 40f, spacing = 56f, firstRing = 104f;

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
            float availW = Screen.width * 0.5f - (panelX + panelW + panelGap);
            float availH = (Screen.height - barH) * 0.5f - 16f;
            float fitZoom = Mathf.Clamp(Mathf.Min(availW, availH) / Mathf.Max(1f, maxR), 0.10f, 2.2f);
            if (treeZoom <= 0f) treeZoom = fitZoom;   // 0 = "아직 안 정해짐 / 리셋됨"

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

                GUI.color = bt ? new Color(0.15f, 0.35f, 0.30f, 1f)
                         : pathAfford ? new Color(0.17f, 0.42f, 0.26f, 1f)
                         : by ? new Color(0.36f, 0.26f, 0.16f, 1f)
                         : new Color(0.13f, 0.14f, 0.16f, 1f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                if (!bt)
                {
                    GUI.color = pathAfford ? new Color(0.49f, 0.92f, 0.62f)
                              : by ? new Color(0.95f, 0.68f, 0.42f)
                              : new Color(0.40f, 0.42f, 0.46f);
                    GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x, rect.y, 2f, rect.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), Texture2D.whiteTexture);
                }

                var tex = (iconTex != null && iconTex.TryGetValue(n.icon, out var it)) ? it : (iconTex != null ? iconTex["dot"] : null);
                if (tex != null)
                {
                    GUI.color = bt ? new Color(1f, 1f, 1f, 0.55f) : (pathAfford || by) ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                    float pad = nodeSz * 0.18f;
                    GUI.DrawTexture(new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height - pad * 2f), tex, ScaleMode.ScaleToFit);
                }
                GUI.color = c0;

                if (rect.Contains(Event.current.mousePosition)) hovered = n;
                if (!bt && GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    BuyPath(n);   // 앞의 안 산 노드들까지 살 수 있는 만큼 한 번에
            }

            GUI.matrix = saved;

            DrawStatPanel(panelW, barH, hovered);

            // 마우스 오버 툴팁 (스크린 좌표)
            if (hovered != null)
            {
                bool bt = IsBought(hovered.id);
                string status;
                if (bt) status = "보유 중";
                else if (hovered.IsRoot) status = "무료";
                else if (IsBuyable(hovered)) status = $"${hovered.cost:N0}";
                else { var pc = PathCost(hovered); status = $"{pc.steps}개 한번에  ·  ${pc.cost:N0}"; }
                string top = $"{hovered.label}\n{hovered.desc}";
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
                GUI.Label(new Rect(margin, 8f, 520f * uiScale, 30f * uiScale), $"보유  ${gold:N0}", goldS);

                var info = new GUIStyle(sLabel) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(13f * uiScale), wordWrap = true };
                float infoTop = 8f + 30f * uiScale;
                GUI.Label(new Rect(margin, infoTop, Screen.width - startBtnW - resetBtnW - margin * 3f, barH - infoTop - 6f),
                    "노드에 마우스를 올리면 효과.  ·  먼 노드를 눌러도 앞쪽까지 한 번에 구매됩니다.\n휠: 확대/축소  ·  우클릭 드래그: 이동  ·  F12: 전체 해금(치트)", info);

                var bst = Btn(Mathf.RoundToInt(14f * uiScale));
                float btnY = barH - btnH - margin;
                if (UiBtn(new Rect(Screen.width - startBtnW - resetBtnW - margin * 2f, btnY, resetBtnW, btnH), "화면 리셋", bst))
                { treeZoom = 0f; treePan = Vector2.zero; }
                if (UiBtn(new Rect(Screen.width - startBtnW - margin, btnY, startBtnW, btnH), $"웨이브 {Mathf.Max(1, stats.startWave)} 시작", bst))
                { StartRun(); }
            }
            GUI.EndGroup();
        }
    }
}
