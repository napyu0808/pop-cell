using UnityEngine;

namespace BlackholeGame
{
    // round48: UI 스킨을 절차적으로 생성한다.
    //   Figma UI 키트(POPCell_UIKit)의 사양을 그대로 옮긴 것 — 버튼 몸통 240x72 @(8,6), 반경 36,
    //   세로 그라데이션 #A8EDD4 -> #3DB39E, 테두리 #145C57 3px, 위쪽 글로스 타원, 소기관 점 3개.
    //   PNG 로 내보내지 않고 코드로 만드는 이유: 상태 4종을 색만 바꿔 일관되게 찍을 수 있고,
    //   WebGL 빌드에 이미지가 안 들어가며, 해상도가 달라져도 다시 구울 수 있다.
    //   Resources/UI/*.png 가 있으면 GameManager 가 그쪽을 먼저 쓴다(디자이너가 교체 가능).
    public static class UiSkin
    {
        // ---- 색 (Figma 사양) ----
        public static readonly Color BtnTop      = Hex(0xA8EDD4), BtnBot      = Hex(0x3DB39E);
        public static readonly Color BtnTopHi    = Hex(0xC6F7E8), BtnBotHi    = Hex(0x56C9B4);
        public static readonly Color BtnTopDn    = Hex(0x3AA996), BtnBotDn    = Hex(0x1F8575);
        public static readonly Color BtnTopOff   = Hex(0xD6DDDC), BtnBotOff   = Hex(0xA2ADAC);
        public static readonly Color BtnEdge     = Hex(0x145C57), BtnEdgeOff  = Hex(0x707B7A);
        public static readonly Color Ink         = Hex(0x0E3B37);   // 버튼 글자
        public static readonly Color PanelFill   = Hex(0x222C31), PanelEdge   = Hex(0x33434A);
        public static readonly Color PanelGoldEd = Hex(0xE0A53C);

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        // ---- 캐시 ----
        static Texture2D btnN, btnH, btnP, btnD, panelDark, panelGold, bubble;

        public static Texture2D BtnNormal   => btnN ??= MakeButton(BtnTop, BtnBot, BtnEdge, 0.55f, 0);
        public static Texture2D BtnHover    => btnH ??= MakeButton(BtnTopHi, BtnBotHi, BtnEdge, 0.70f, 0);
        public static Texture2D BtnPressed  => btnP ??= MakeButton(BtnTopDn, BtnBotDn, BtnEdge, 0.28f, 1);
        public static Texture2D BtnDisabled => btnD ??= MakeButton(BtnTopOff, BtnBotOff, BtnEdgeOff, 0.25f, 2);
        public static Texture2D PanelDark   => panelDark ??= MakePanel(PanelEdge, 3f);
        public static Texture2D PanelGold   => panelGold ??= MakePanel(PanelGoldEd, 4f);
        // 부드러운 원 — 배경 세포(거품)와 글로우에 쓴다
        public static Texture2D Bubble      => bubble ??= MakeBubble();

        // 둥근 사각형 SDF — 음수 = 내부, 0 = 테두리
        static float RoundRect(float x, float y, float cx, float cy, float hw, float hh, float r)
        {
            float dx = Mathf.Abs(x - cx) - (hw - r);
            float dy = Mathf.Abs(y - cy) - (hh - r);
            float ax = Mathf.Max(dx, 0f), ay = Mathf.Max(dy, 0f);
            return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
        }

        static Color Over(Color dst, Color src)
        {
            float a = src.a + dst.a * (1f - src.a);
            if (a <= 0f) return new Color(0, 0, 0, 0);
            return new Color((src.r * src.a + dst.r * dst.a * (1f - src.a)) / a,
                             (src.g * src.a + dst.g * dst.a * (1f - src.a)) / a,
                             (src.b * src.a + dst.b * dst.a * (1f - src.a)) / a, a);
        }

        static Texture2D Bake(Color[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // mode: 0 = 보통, 1 = 눌림(그림자 거의 없음), 2 = 비활성(글로스/소기관 흐리게)
        static Texture2D MakeButton(Color top, Color bot, Color edge, float glossA, int mode)
        {
            const int W = 256, H = 96;
            // Figma: 몸통 x8..248, y6..78 (위가 0), 반경 36
            const float BX = 8f, BY = 6f, BW = 240f, BH = 72f, R = 36f;
            float cx = BX + BW * 0.5f, cy = BY + BH * 0.5f, hw = BW * 0.5f, hh = BH * 0.5f;
            float shadowDy = mode == 1 ? 2f : 5f;
            var px = new Color[W * H];

            for (int iy = 0; iy < H; iy++)
            {
                float fy = H - 1 - iy + 0.5f;        // 텍스처는 아래가 0 — Figma 좌표(위가 0)로 뒤집는다
                for (int ix = 0; ix < W; ix++)
                {
                    float fx = ix + 0.5f;
                    Color c = new Color(0, 0, 0, 0);

                    // 1) 드롭 섀도 — 몸통을 아래로 내린 것, 가장자리를 부드럽게
                    float ds = RoundRect(fx, fy - shadowDy, cx, cy, hw, hh, R);
                    float sa = Mathf.Clamp01(1f - ds / 3f) * (mode == 1 ? 0.22f : 0.38f);
                    if (sa > 0f) c = Over(c, new Color(0.08f, 0.36f, 0.34f, sa));

                    // 2) 몸통 — 세로 그라데이션
                    float d = RoundRect(fx, fy, cx, cy, hw, hh, R);
                    float inside = Mathf.Clamp01(0.5f - d);
                    if (inside > 0f)
                    {
                        float t = Mathf.Clamp01((fy - BY) / BH);
                        var body = Color.Lerp(top, bot, t);
                        c = Over(c, new Color(body.r, body.g, body.b, inside));

                        // 3) 안쪽 윗면 하이라이트(inner shadow 흰색) — 위 가장자리 3px
                        float rim = Mathf.Clamp01((-d - 1.0f) / 3f);
                        float topFade = Mathf.Clamp01(1f - (fy - BY) / 10f);
                        float ia = (1f - rim) * topFade * 0.35f * inside;
                        if (mode == 1) ia *= 0.3f;
                        if (ia > 0f) c = Over(c, new Color(1f, 1f, 1f, ia));

                        // 4) 글로스 — 윗부분 가로로 긴 타원, 흐릿하게
                        float gx = (fx - 128f) / 84f, gy = (fy - 24f) / 10f;
                        float gd = gx * gx + gy * gy;
                        float ga = Mathf.Clamp01(1f - gd) * glossA * inside;
                        if (ga > 0f) c = Over(c, new Color(1f, 1f, 1f, ga * 0.9f));

                        // 5) 소기관 — 세포 느낌을 주는 흰 점 3개.
                        //   9-slice 의 오른쪽 고정 칸(x > 212) 안에 둬야 버튼이 길어져도 안 늘어난다.
                        float oa = mode == 2 ? 0.18f : 0.35f;
                        c = Over(c, Dot(fx, fy, 224f, 45f, 6.5f, oa, inside));
                        c = Over(c, Dot(fx, fy, 237f, 34f, 3.5f, oa, inside));
                        c = Over(c, Dot(fx, fy, 230f, 57f, 2.5f, oa, inside));
                    }

                    // 6) 테두리 3px — 안쪽으로
                    float ea = Mathf.Clamp01(0.5f - Mathf.Abs(d + 1.5f) + 1.0f);
                    if (d < 0.5f && ea > 0f) c = Over(c, new Color(edge.r, edge.g, edge.b, Mathf.Min(1f, ea)));

                    px[iy * W + ix] = c;
                }
            }
            return Bake(px, W, H);
        }

        static Color Dot(float x, float y, float cx, float cy, float r, float a, float mask)
        {
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
            float t = Mathf.Clamp01(0.5f - d) * a * mask;
            return t > 0f ? new Color(1f, 1f, 1f, t) : new Color(0, 0, 0, 0);
        }

        // 9-slice 패널 — 어두운 반투명 바탕 + 테두리. 모서리 반경 18.
        static Texture2D MakePanel(Color edge, float bw)
        {
            const int S = 160;
            const float R = 18f;
            float c = S * 0.5f, h = S * 0.5f - 2f;
            var px = new Color[S * S];
            for (int iy = 0; iy < S; iy++)
                for (int ix = 0; ix < S; ix++)
                {
                    float fx = ix + 0.5f, fy = iy + 0.5f;
                    float d = RoundRect(fx, fy, c, c, h, h, R);
                    float inside = Mathf.Clamp01(0.5f - d);
                    Color col = new Color(0, 0, 0, 0);
                    if (inside > 0f)
                        col = new Color(PanelFill.r, PanelFill.g, PanelFill.b, 0.92f * inside);
                    float ea = Mathf.Clamp01(0.5f - Mathf.Abs(d + bw * 0.5f) + bw * 0.5f);
                    if (d < 0.5f && ea > 0f) col = Over(col, new Color(edge.r, edge.g, edge.b, Mathf.Min(1f, ea)));
                    px[iy * S + ix] = col;
                }
            return Bake(px, S, S);
        }

        // 세로 그라데이션 1px 폭 — 배경 바탕
        static Texture2D vgrad;
        public static Texture2D VGrad => vgrad ??= MakeVGrad(Hex(0xDCF2F4), Hex(0x9FD4DD));
        static Texture2D MakeVGrad(Color top, Color bot)
        {
            const int H = 128;
            var px = new Color[H];
            for (int i = 0; i < H; i++) px[i] = Color.Lerp(bot, top, i / (float)(H - 1));   // 텍스처는 아래가 0
            var t = new Texture2D(1, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply();
            return t;
        }

        // 비네트 — 가운데는 투명, 가장자리로 갈수록 어두워진다
        static Texture2D vignette;
        public static Texture2D Vignette => vignette ??= MakeVignette();
        static Texture2D MakeVignette()
        {
            const int S = 256;
            var px = new Color[S * S];
            float c = S * 0.5f;
            for (int iy = 0; iy < S; iy++)
                for (int ix = 0; ix < S; ix++)
                {
                    float dx = (ix + 0.5f - c) / c, dy = (iy + 0.5f - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float a = Mathf.Clamp01((d - 0.45f) / 0.55f);
                    px[iy * S + ix] = new Color(0.05f, 0.18f, 0.22f, a * a * 0.30f);
                }
            return Bake(px, S, S);
        }

        // 스테이지 카드 — 둥근 모서리 바탕과 테두리를 따로 둬서 색을 각각 틴트한다(9-slice, 경계 26).
        static Texture2D cardFill, cardEdge;
        public static Texture2D CardFill => cardFill ??= MakeCard(false);
        public static Texture2D CardEdge => cardEdge ??= MakeCard(true);
        static Texture2D MakeCard(bool edgeOnly)
        {
            const int S = 128;
            const float R = 26f, BW = 5f;
            float c = S * 0.5f, h = S * 0.5f - 1f;
            var px = new Color[S * S];
            for (int iy = 0; iy < S; iy++)
                for (int ix = 0; ix < S; ix++)
                {
                    float d = RoundRect(ix + 0.5f, iy + 0.5f, c, c, h, h, R);
                    float a = edgeOnly
                        ? Mathf.Clamp01(Mathf.Min(0.5f - d, d + BW + 0.5f))   // 테두리 띠만
                        : Mathf.Clamp01(0.5f - d);
                    px[iy * S + ix] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            return Bake(px, S, S);
        }

        // 가장자리로 갈수록 투명해지는 원 — 배경 거품/글로우용
        static Texture2D MakeBubble()
        {
            const int S = 128;
            var px = new Color[S * S];
            float c = S * 0.5f, r = S * 0.5f - 1f;
            for (int iy = 0; iy < S; iy++)
                for (int ix = 0; ix < S; ix++)
                {
                    float dx = ix + 0.5f - c, dy = iy + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    float a = d >= 1f ? 0f : Mathf.Clamp01(1f - d * d * d);   // 가운데가 넓고 가장자리만 빠르게
                    px[iy * S + ix] = new Color(1f, 1f, 1f, a);
                }
            return Bake(px, S, S);
        }
    }
}
