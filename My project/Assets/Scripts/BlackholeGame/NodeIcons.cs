using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlackholeGame
{
    // 노드 효과별 아이콘 — 작은 흑백(흰 선) 텍스처를 절차적으로 생성. 노드에서 GUI.color로 틴트해서 사용.
    public static class NodeIcons
    {
        const int S = 28;

        static void P(Color32[] px, int x, int y)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            px[y * S + x] = new Color32(255, 255, 255, 255);
        }
        static void Dot(Color32[] px, int x, int y, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r * r) P(px, x + dx, y + dy);
        }
        static void Ln(Color32[] px, float x0, float y0, float x1, float y1, int w)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2f) + 1;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                Dot(px, Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), w);
            }
        }
        static void Ring(Color32[] px, float cx, float cy, float rad, int w)
        {
            int steps = Mathf.CeilToInt(rad * 9f) + 10;
            for (int s = 0; s < steps; s++)
            {
                float a = s / (float)steps * Mathf.PI * 2f;
                Dot(px, Mathf.RoundToInt(cx + Mathf.Cos(a) * rad), Mathf.RoundToInt(cy + Mathf.Sin(a) * rad), w);
            }
        }
        static Texture2D Make(Action<Color32[]> draw)
        {
            var px = new Color32[S * S];
            draw(px);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // round40: 약물 컨셉을 걷어내고 "보자마자 아는" 직관적인 아이콘으로 되돌림.
        public static Dictionary<string, Texture2D> Build()
        {
            return new Dictionary<string, Texture2D>
            {
                // 공격력(Flat) — 검: 칼날+손잡이+날밑
                ["sword"]     = Make(px => {
                    Ln(px, 5, 23, 20, 8, 1);
                    Ln(px, 20, 8, 23, 5, 1);
                    Ln(px, 15, 13, 19, 9, 0);
                    Ln(px, 3, 20, 8, 25, 1);
                    Dot(px, 3, 20, 1); Dot(px, 8, 25, 1);
                }),
                // 배수(Mult) — 곱하기 X
                ["mult"]      = Make(px => { Ln(px, 7, 7, 21, 21, 1); Ln(px, 21, 7, 7, 21, 1); }),
                // 공속/자동공격(Speed·Auto) — 번개
                ["bolt"]      = Make(px => { Ln(px, 17, 3, 9, 15, 1); Ln(px, 9, 15, 15, 15, 1); Ln(px, 15, 15, 8, 25, 1); }),
                // 치명 확률(CritC) — 조준선
                ["crosshair"] = Make(px => { Ring(px, 14, 14, 8, 1); Ln(px, 14, 1, 14, 6, 0); Ln(px, 14, 22, 14, 27, 0); Ln(px, 1, 14, 6, 14, 0); Ln(px, 22, 14, 27, 14, 0); Dot(px, 14, 14, 1); }),
                // 치명 배율(CritX) — 별
                ["star"]      = Make(px => { Ln(px, 14, 2, 14, 26, 0); Ln(px, 2, 14, 26, 14, 0); Ln(px, 6, 6, 22, 22, 0); Ln(px, 22, 6, 6, 22, 0); Dot(px, 14, 14, 2); }),
                // 범위(Range) — 확장 원
                ["ring"]      = Make(px => { Ring(px, 14, 14, 9, 2); }),
                // 골드(Gold) — 달러 + 증가 화살표
                ["coin"]      = Make(px => {
                    Ln(px, 10, 8, 10, 22, 1);
                    Ring(px, 10, 12, 4, 1); Ring(px, 10, 18, 4, 1);
                    Ln(px, 18, 20, 24, 20, 1); Ln(px, 24, 20, 24, 12, 1);
                    Ln(px, 24, 12, 20, 8, 1); Ln(px, 24, 12, 28, 8, 1);
                }),
                // 제한시간(Time) — 시계
                ["clock"]     = Make(px => { Ring(px, 14, 14, 10, 1); Ln(px, 14, 14, 14, 6, 0); Ln(px, 14, 14, 20, 17, 0); }),
                // 소환가속·소환수(Spawn·SCount) — 겹화살표(더 빠르게/더 많이)
                ["chevrons"]  = Make(px => { Ln(px, 8, 6, 14, 14, 0); Ln(px, 14, 14, 8, 22, 0); Ln(px, 14, 6, 20, 14, 0); Ln(px, 20, 14, 14, 22, 0); }),
                // 동시 소환(SCount) — 세포 셋(한 번에 여러 마리). 소환 가속(겹화살표)과 구분
                ["cells"]     = Make(px => { Ring(px, 9, 17, 5, 1); Ring(px, 19, 17, 5, 1); Ring(px, 14, 8, 5, 1); }),
                // ---- 백신(round42) ----
                // 해금 — 알약 캡슐(굵은 선 = 둥근 캡슐)
                ["pill"]      = Make(px => { Ln(px, 8, 20, 20, 8, 5); }),
                // 백신 대미지 — 터지는 폭발(8방향 파편 + 중심)
                ["blast"]     = Make(px => {
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k / 8f * 6.2832f;
                        Ln(px, 14 + Mathf.Cos(a) * 6f, 14 + Mathf.Sin(a) * 6f, 14 + Mathf.Cos(a) * 12f, 14 + Mathf.Sin(a) * 12f, 1);
                    }
                    Dot(px, 14, 14, 3);
                }),
                // 백신 범위 — 퍼지는 겹 원
                ["blastring"] = Make(px => { Ring(px, 14, 14, 11, 0); Ring(px, 14, 14, 6, 1); Dot(px, 14, 14, 2); }),
                // 백신 빈도 — 작은 캡슐 + 빨리감기
                ["pillfast"]  = Make(px => { Ln(px, 4, 19, 11, 12, 3); Ln(px, 15, 8, 20, 14, 1); Ln(px, 20, 14, 15, 20, 1); Ln(px, 21, 8, 26, 14, 1); Ln(px, 26, 14, 21, 20, 1); }),
                // 웨이브 스킵(Skip) — 빨리감기
                ["skip"]      = Make(px => { Ln(px, 6, 6, 14, 14, 1); Ln(px, 14, 14, 6, 22, 1); Ln(px, 14, 6, 22, 14, 1); Ln(px, 22, 14, 14, 22, 1); Ln(px, 24, 5, 24, 23, 0); }),
                // 코어(루트) — 육각형
                ["hex"]       = Make(px => { const float r = 10f; for (int k = 0; k < 6; k++) { float a0 = k / 6f * 6.2832f, a1 = (k + 1) / 6f * 6.2832f; Ln(px, 14 + Mathf.Cos(a0) * r, 14 + Mathf.Sin(a0) * r, 14 + Mathf.Cos(a1) * r, 14 + Mathf.Sin(a1) * r, 1); } Dot(px, 14, 14, 2); }),
                // 폴백
                ["dot"]       = Make(px => { Dot(px, 14, 14, 3); }),
                // 설정(톱니바퀴) — 지도 화면 우측 상단 메뉴 버튼용(노드 아님, 그대로)
                ["gear"]      = Make(px => {
                    Ring(px, 14, 14, 8, 1);
                    Ring(px, 14, 14, 3, 0);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k / 8f * 6.2832f;
                        Ln(px, 14 + Mathf.Cos(a) * 8f, 14 + Mathf.Sin(a) * 8f,
                               14 + Mathf.Cos(a) * 12f, 14 + Mathf.Sin(a) * 12f, 1);
                    }
                }),
            };
        }
    }
}
