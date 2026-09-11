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

        public static Dictionary<string, Texture2D> Build()
        {
            return new Dictionary<string, Texture2D>
            {
                ["sword"]     = Make(px => { Ln(px, 5, 22, 20, 7, 1); Ln(px, 20, 7, 24, 3, 1); Ln(px, 13, 10, 18, 15, 1); Dot(px, 5, 22, 1); Dot(px, 3, 25, 1); }),
                ["mult"]      = Make(px => { Ln(px, 7, 7, 21, 21, 1); Ln(px, 21, 7, 7, 21, 1); }),
                ["bolt"]      = Make(px => { Ln(px, 17, 3, 9, 15, 1); Ln(px, 9, 15, 15, 15, 1); Ln(px, 15, 15, 8, 25, 1); }),
                ["crosshair"] = Make(px => { Ring(px, 14, 14, 8, 1); Ln(px, 14, 1, 14, 6, 0); Ln(px, 14, 22, 14, 27, 0); Ln(px, 1, 14, 6, 14, 0); Ln(px, 22, 14, 27, 14, 0); Dot(px, 14, 14, 1); }),
                ["star"]      = Make(px => { Ln(px, 14, 2, 14, 26, 0); Ln(px, 2, 14, 26, 14, 0); Ln(px, 6, 6, 22, 22, 0); Ln(px, 22, 6, 6, 22, 0); Dot(px, 14, 14, 2); }),
                ["ring"]      = Make(px => { Ring(px, 14, 14, 9, 2); }),
                ["coin"]      = Make(px => { Ring(px, 14, 14, 10, 1); Ln(px, 14, 8, 14, 20, 0); Ln(px, 11, 10, 17, 10, 0); Ln(px, 11, 18, 17, 18, 0); }),
                ["clock"]     = Make(px => { Ring(px, 14, 14, 10, 1); Ln(px, 14, 14, 14, 6, 0); Ln(px, 14, 14, 20, 17, 0); }),
                ["chevrons"]  = Make(px => { Ln(px, 8, 6, 14, 14, 0); Ln(px, 14, 14, 8, 22, 0); Ln(px, 14, 6, 20, 14, 0); Ln(px, 20, 14, 14, 22, 0); }),
                ["skip"]      = Make(px => { Ln(px, 6, 6, 14, 14, 1); Ln(px, 14, 14, 6, 22, 1); Ln(px, 14, 6, 22, 14, 1); Ln(px, 22, 14, 14, 22, 1); Ln(px, 24, 5, 24, 23, 0); }),
                ["hex"]       = Make(px => { const float r = 10f; for (int k = 0; k < 6; k++) { float a0 = k / 6f * 6.2832f, a1 = (k + 1) / 6f * 6.2832f; Ln(px, 14 + Mathf.Cos(a0) * r, 14 + Mathf.Sin(a0) * r, 14 + Mathf.Cos(a1) * r, 14 + Mathf.Sin(a1) * r, 1); } Dot(px, 14, 14, 2); }),
                ["dot"]       = Make(px => { Dot(px, 14, 14, 3); }),
                // 설정(톱니바퀴) — 지도 화면 우측 상단 메뉴 버튼용
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
