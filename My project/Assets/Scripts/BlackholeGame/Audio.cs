using UnityEngine;

namespace BlackholeGame
{
    public enum Sfx { Pop, Crit, Pulse, Wave, Buy, Click, Boss, Win, Timeout }

    // ============================================================
    // 절차적 사운드 — 오디오 파일 없이 PCM을 직접 합성한다.
    //   스프라이트/아이콘을 코드로 만드는 것과 같은 방침 (에셋 의존 0).
    // ============================================================
    public static class SynthClips
    {
        const int SR = 44100;              // 효과음
        public const int BgmRate = 22050;  // BGM은 절반 레이트로 (생성 비용/메모리 절감)

        // 소프트 리미터 — 레이어가 겹쳐도 하드클리핑으로 지직거리지 않게.
        // (Unity API를 안 쓰므로 백그라운드 스레드에서 호출해도 안전)
        static void Limit(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float x = d[i];
                d[i] = Mathf.Clamp(x / (1f + Mathf.Abs(x) * 0.35f), -1f, 1f);
            }
        }

        public static AudioClip FromBuffer(string name, float[] d, int sr)
        {
            var c = AudioClip.Create(name, d.Length, 1, sr, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip Make(string name, float[] d, int sr)
        {
            Limit(d);
            return FromBuffer(name, d, sr);
        }

        static float Freq(float midi) => 440f * Mathf.Pow(2f, (midi - 69f) / 12f);
        static float Pluck(float t, float decay) => t < 0f ? 0f : Mathf.Exp(-t * decay);

        static float AR(float t, float dur, float atk, float rel)
        {
            if (t < 0f || t > dur) return 0f;
            float a = atk > 0f ? Mathf.Clamp01(t / atk) : 1f;
            float r = rel > 0f ? Mathf.Clamp01((dur - t) / rel) : 1f;
            return a * r;
        }

        static float Bell(float ph) => Mathf.Sin(ph) + 0.34f * Mathf.Sin(ph * 2f) + 0.16f * Mathf.Sin(ph * 3f);

        // 사인 테이블 — BGM 한 벌에 사인을 천만 번 넘게 부른다. WebGL은 스레드가 없어 이 생성이
        //   그대로 시작 멈칫함이 되므로 직접 호출 대신 표를 쓴다. 4096칸 선형보간이면 패드·드론에는
        //   차이가 들리지 않는다. 인자는 라디안이 아니라 '회전수'다(1 = 한 주기).
        const int SinN = 4096;
        static readonly float[] SinT = BuildSinTable();
        static float[] BuildSinTable()
        {
            var t = new float[SinN + 1];
            for (int i = 0; i <= SinN; i++) t[i] = Mathf.Sin(2f * Mathf.PI * i / SinN);
            return t;
        }
        // 뜯은 현(Karplus-Strong) — 짧은 지연선에 잡음을 채우고 한 칸씩 평균내며 돌린다.
        //   높은 배음부터 차례로 죽어서 기타·하프처럼 들린다. 사인을 쌓는 것보다 싸고,
        //   무엇보다 지속음이 아니라 사그라든다 — 계속 울리는 소리가 없어야 귀가 쉰다.
        static void PluckString(float[] d, int sr, float t0, float f, float amp, float damp, float dur)
        {
            int N = Mathf.Max(2, Mathf.RoundToInt(sr / f));
            var buf = new float[N];
            var rng = new System.Random(unchecked((int)(f * 1000f) ^ (int)(t0 * 97f)));
            float prev = 0f;
            for (int i = 0; i < N; i++)
            {
                // 잡음을 한 번 저역통과해서 넣는다 — 생으로 넣으면 손톱으로 긁는 소리가 난다
                prev = prev * 0.62f + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.38f;
                buf[i] = prev;
            }
            int i0 = Mathf.Max(0, (int)(t0 * sr));
            int i1 = Mathf.Min(d.Length, i0 + (int)(dur * sr));
            int k = 0;
            for (int i = i0; i < i1; i++)
            {
                float cur = buf[k];
                buf[k] = (cur + buf[(k + 1) % N]) * 0.5f * damp;
                k = (k + 1) % N;
                // 버퍼 끝에서 잘릴 때 딱 소리가 나지 않게 꼬리만 깎는다
                float left = (i1 - i) / (float)sr;
                d[i] += cur * amp * (left < 0.30f ? left / 0.30f : 1f);
            }
        }

        // 신스 리드 — 즐거운 쪽. 배음을 쌓아 톱니에 가깝게 만들고 어택을 8ms 로 끊어 통통 튀게 한다.
        //   뜯은 현이 바탕을 깔아 둔 뒤에 들어와야 "합류했다"로 들린다. 혼자 나오면 그냥 삐 소리다.
        static void SynthLead(float[] d, int sr, float t0, float f, float amp, float dur)
        {
            int i0 = Mathf.Max(0, (int)(t0 * sr));
            int i1 = Mathf.Min(d.Length, i0 + (int)(dur * sr));
            double ph = 0, inc = f / (double)sr, ph2 = 0, inc2 = f * 1.006 / sr;
            for (int i = i0; i < i1; i++)
            {
                float t = (i - i0) / (float)sr;
                float env = Mathf.Clamp01(t / 0.008f) * Mathf.Exp(-t / (dur * 0.38f));
                float s = Sin01(ph) + 0.5f * Sin01(ph * 2.0) + 0.33f * Sin01(ph * 3.0) + 0.22f * Sin01(ph * 4.0);
                float s2 = Sin01(ph2) + 0.5f * Sin01(ph2 * 2.0);
                d[i] += (s * 0.70f + s2 * 0.30f) * env * amp;
                ph += inc; ph2 += inc2;
            }
        }

        static float Sin01(double turns)
        {
            double p = turns - System.Math.Floor(turns);
            double x = p * SinN;                 // float 로 먼저 줄이면 p 가 1 에 가까울 때
            int i = (int)x;                      //   반올림으로 4096 이 되어 표 밖을 짚는다
            if (i >= SinN) i = SinN - 1;
            float f = (float)(x - i);
            return SinT[i] + (SinT[i + 1] - SinT[i]) * f;
        }

        // ---- 효과음 ----

        // 세포가 "뽁" 터지는 소리 (게임 제목이 POP Cell).
        //   핵심은 피치 하강이 아주 빨라야 한다는 것 — 시정수 9ms.
        //   느리면 "뿅"(레이저)이 되고, 빠르면 비닐 뽁뽁이 터지는 소리가 된다.
        //   전체 길이도 85ms로 짧아야 여러 마리가 동시에 죽을 때 "뽁뽁뽁"으로 들린다.
        public static AudioClip Pop()
        {
            float dur = 0.085f;
            int n = (int)(SR * dur);
            var d = new float[n];
            float ph = 0f, ph2 = 0f;
            var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = 175f + 1500f * Mathf.Exp(-t / 0.009f);   // 1675Hz -> 175Hz, 9ms 시정수
                ph += 2f * Mathf.PI * f / SR;
                ph2 += 2f * Mathf.PI * (f * 0.5f) / SR;            // 한 옥타브 아래 = 통통한 몸통
                float atk = Mathf.Clamp01(t / 0.0012f);            // 1.2ms 어택 → 딱 끊어지는 시작
                float env = atk * Mathf.Exp(-t / 0.020f);
                float body = Mathf.Sin(ph) + 0.32f * Mathf.Sin(ph2);
                float spit = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.0016f) * 0.45f;
                d[i] = (body * env + spit) * 0.62f;
            }
            return Make("sfx_pop", d, SR);
        }

        // 치명타 — 같은 "뽁"인데 더 크고 밝게 터지고 살짝 링이 남는다
        public static AudioClip Crit()
        {
            float dur = 0.16f;
            int n = (int)(SR * dur);
            var d = new float[n];
            float ph = 0f, ph2 = 0f, ph3 = 0f;
            var rng = new System.Random(13);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = 300f + 2600f * Mathf.Exp(-t / 0.010f);
                ph += 2f * Mathf.PI * f / SR;
                ph2 += 2f * Mathf.PI * (f * 0.5f) / SR;
                ph3 += 2f * Mathf.PI * 2350f / SR;                 // 남는 링
                float atk = Mathf.Clamp01(t / 0.0012f);
                float env = atk * Mathf.Exp(-t / 0.026f);
                float body = Mathf.Sin(ph) + 0.30f * Mathf.Sin(ph2);
                float ring = Mathf.Sin(ph3) * Mathf.Exp(-t / 0.055f) * 0.22f;
                float spit = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.0018f) * 0.45f;
                d[i] = (body * env + ring + spit) * 0.62f;
            }
            return Make("sfx_crit", d, SR);
        }

        // 타격 펄스 — 세포 테마에 맞는 낮은 "쿵". 공격 간격마다 1번이라 리듬이 된다.
        public static AudioClip Pulse()
        {
            float dur = 0.09f;
            int n = (int)(SR * dur);
            var d = new float[n];
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = Mathf.Lerp(190f, 95f, Mathf.Clamp01(t / 0.05f));
                ph += 2f * Mathf.PI * f / SR;
                d[i] = Mathf.Sin(ph) * Pluck(t, 34f) * 0.40f;
            }
            return Make("sfx_pulse", d, SR);
        }

        // 웨이브 클리어 — 상승 3음
        public static AudioClip Wave()
        {
            float[] notes = { 72f, 76f, 79f };   // C5 E5 G5
            float step = 0.075f, dur = step * notes.Length + 0.22f;
            int n = (int)(SR * dur);
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                float f = Freq(notes[k]), t0 = k * step, ph = 0f;
                for (int i = (int)(t0 * SR); i < n; i++)
                {
                    float t = (float)i / SR - t0;
                    ph += 2f * Mathf.PI * f / SR;
                    d[i] += Bell(ph) * Pluck(t, 13f) * 0.24f;
                }
            }
            return Make("sfx_wave", d, SR);
        }

        // 노드 구매 — 동전이 "짤랑".
        //   금속성은 비조화 배음(1 : 2.76 : 5.40 : 8.93, 종/금속판 비율)에서 나온다.
        //   여기에 높은 음역대 잔향을 흩뿌려 동전이 부딪히는 짤랑거림을 만든다.
        public static AudioClip Buy()
        {
            float dur = 0.55f;
            int n = (int)(SR * dur);
            var d = new float[n];

            float[] ratio = { 1f, 2.76f, 5.40f, 8.93f };
            float[] pAmp = { 1f, 0.55f, 0.30f, 0.16f };
            float[] notes = { 1318.5f, 1975.5f };     // E6 -> B6 (동전 특유의 밝은 2음)
            float[] t0s = { 0f, 0.058f };

            for (int k = 0; k < notes.Length; k++)
                for (int p = 0; p < ratio.Length; p++)
                {
                    float f = notes[k] * ratio[p], ph = 0f;
                    for (int i = (int)(t0s[k] * SR); i < n; i++)
                    {
                        float t = (float)i / SR - t0s[k];
                        ph += 2f * Mathf.PI * f / SR;
                        d[i] += Mathf.Sin(ph) * pAmp[p] * Mathf.Exp(-t * (6.5f + p * 5f)) * 0.15f;
                    }
                }

            // 짤랑 — 고음역 잔향을 랜덤하게 흩뿌린다
            var rng = new System.Random(23);
            for (int j = 0; j < 7; j++)
            {
                float f = 1900f + (float)rng.NextDouble() * 1900f;
                float t0 = 0.015f + (float)rng.NextDouble() * 0.15f;
                float amp = 0.045f + (float)rng.NextDouble() * 0.05f;
                float ph = 0f;
                for (int i = (int)(t0 * SR); i < n; i++)
                {
                    float t = (float)i / SR - t0;
                    ph += 2f * Mathf.PI * f / SR;
                    d[i] += Mathf.Sin(ph) * amp * Mathf.Exp(-t * 8.5f);
                }
            }
            return Make("sfx_buy", d, SR);
        }

        // UI 클릭 — 아주 짧은 틱
        public static AudioClip Click()
        {
            float dur = 0.04f;
            int n = (int)(SR * dur);
            var d = new float[n];
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                ph += 2f * Mathf.PI * 1150f / SR;
                d[i] = Mathf.Sin(ph) * Pluck(t, 90f) * 0.32f;
            }
            return Make("sfx_click", d, SR);
        }

        // 보스 등장 — 낮게 깔리는 하강 럼블 + 트레몰로
        public static AudioClip Boss()
        {
            float dur = 1.5f;
            int n = (int)(SR * dur);
            var d = new float[n];
            float ph = 0f, ph2 = 0f;
            var rng = new System.Random(11);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = Mathf.Lerp(130f, 48f, Mathf.Clamp01(t / dur));
                ph += 2f * Mathf.PI * f / SR;
                ph2 += 2f * Mathf.PI * (f * 1.005f) / SR;   // 살짝 디튠 → 울렁임
                float trem = 0.78f + 0.22f * Mathf.Sin(2f * Mathf.PI * 6.5f * t);
                float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.10f;
                d[i] = (Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph2) * 0.5f + noise)
                       * AR(t, dur, 0.05f, 0.55f) * trem * 0.6f;
            }
            return Make("sfx_boss", d, SR);
        }

        // 클리어 팡파레 — 상승 아르페지오 + 지속 패드
        public static AudioClip Win()
        {
            float[] notes = { 60f, 64f, 67f, 72f, 76f, 79f };
            float step = 0.13f, dur = 1.9f;
            int n = (int)(SR * dur);
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                float f = Freq(notes[k]), t0 = k * step, ph = 0f;
                for (int i = (int)(t0 * SR); i < n; i++)
                {
                    float t = (float)i / SR - t0;
                    ph += 2f * Mathf.PI * f / SR;
                    d[i] += Bell(ph) * Pluck(t, 4.5f) * 0.18f;
                }
            }
            float t0p = step * notes.Length;
            float[] pad = { 60f, 64f, 67f, 72f };
            foreach (var m in pad)
            {
                float f = Freq(m), ph = 0f;
                for (int i = (int)(t0p * SR); i < n; i++)
                {
                    float t = (float)i / SR - t0p;
                    ph += 2f * Mathf.PI * f / SR;
                    d[i] += Mathf.Sin(ph) * AR(t, dur - t0p, 0.12f, 0.6f) * 0.11f;
                }
            }
            return Make("sfx_win", d, SR);
        }

        // 시간 종료 — 힘 빠지는 하강 2음
        public static AudioClip Timeout()
        {
            float[] notes = { 69f, 62f };        // A4 -> D4
            float step = 0.19f, dur = 0.85f;
            int n = (int)(SR * dur);
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                float f = Freq(notes[k]), t0 = k * step, ph = 0f;
                for (int i = (int)(t0 * SR); i < n; i++)
                {
                    float t = (float)i / SR - t0;
                    ph += 2f * Mathf.PI * f / SR;
                    d[i] += (Mathf.Sin(ph) + 0.25f * Mathf.Sin(ph * 2f)) * Pluck(t, 5.5f) * 0.24f;
                }
            }
            return Make("sfx_timeout", d, SR);
        }

        // ---- BGM ----
        // 뜯은 현으로 시작해서 신스가 합류하는 구성.
        //   드론은 뺐다. 55Hz 사인 두 개를 어긋나게 겹치면 초당 0.2회로 맥놀이가 생기는데,
        //   그 "우우웅" 이 쉬지 않고 깔리면 편안한 게 아니라 머리가 아프다. 계속 울리는 소리를
        //   없애고, 바탕을 사그라드는 소리(뜯은 현)로 바꾸면 소리 사이에 빈틈이 생겨 귀가 쉰다.
        //
        //   40초 루프를 둘로 나눠 앞 20초는 기타만, 뒤 20초에 신스가 들어온다. 루프가 한 바퀴 돌 때마다
        //   "조용히 시작해서 밝아진다"가 반복되는 셈이라, 같은 길이로도 한 덩어리로 들리지 않는다.
        //   화성은 서스펜디드·4도 쌓기다 — 장3도를 넣으면 바로 예쁜 노래가 되고 단3도면 슬퍼진다.
        //   둘 다 피하고, 즐거운 느낌은 신스의 음색과 리듬으로만 낸다.
        //
        //   이음매: 패드는 f*L이 정수라야 루프 끝에서 위상이 0으로 돌아온다(Q가 맞춘다).
        //   뜯은 현과 신스는 감쇠음이고, 버퍼 끝에서 잘리는 꼬리는 각 함수가 깎아 준다.
        //   Unity API를 쓰지 않으므로 백그라운드 스레드에서 생성해도 안전하다.
        public const float BgmLoopSec = 40f;

        public static float[] BgmBuffer()
        {
            int SRB = BgmRate;
            float L = BgmLoopSec;
            int n = (int)(SRB * L);
            var d = new float[n];

            // 루프 경계에서 위상이 딱 맞게 주파수를 격자에 올린다
            float Q(float f) => Mathf.Max(1f, Mathf.Round(f * L)) / L;

            // 네 개의 열린 화음을 10초씩. 3도가 없어 장단이 정해지지 않는다.
            float[,] chord =
            {
                { 57, 59, 64 },   // A3 B3 E4   Asus2
                { 53, 55, 60 },   // F3 G3 C4   Fsus2
                { 60, 62, 67 },   // C4 D4 G4   Csus2
                { 55, 60, 62 },   // G3 C4 D4   Gsus4
            };
            float[] bassNote = { 45, 41, 48, 43 };   // A2 F2 C3 G2 — 기타가 짚기 좋은 자리
            int CH = chord.GetLength(0);
            float seg = L / CH;

            // ---- 1) 뜯은 현 — 베이스와 아르페지오. 이 층이 곡의 바탕이다.
            //      (구간 내 초, 화음 구성음 인덱스, 옥타브 더하기, 세기)
            float[,] arp =
            {
                { 1.3f, 0, 12, 0.56f }, { 2.6f, 2, 12, 0.49f }, { 4.0f, 1, 12, 0.53f },
                { 6.3f, 0, 12, 0.46f }, { 7.6f, 2, 12, 0.42f }, { 8.8f, 1, 24, 0.30f },
            };
            for (int c = 0; c < CH; c++)
            {
                float t0 = c * seg;
                PluckString(d, SRB, t0,        Freq(bassNote[c]), 1.05f, 0.9976f, 4.6f);
                PluckString(d, SRB, t0 + 5.0f, Freq(bassNote[c]), 0.66f, 0.9976f, 4.2f);
                for (int k = 0; k < arp.GetLength(0); k++)
                    PluckString(d, SRB, t0 + arp[k, 0],
                                Freq(chord[c, (int)arp[k, 1]] + arp[k, 2]),
                                arp[k, 3], 0.9962f, 3.0f);
            }

            // ---- 2) 신스 리드 — 뒤 20초에만. 즐거운 쪽을 담당한다.
            //      구간 2는 0.72배로 들어와서 구간 3에 제 음량이 된다 — 합류가 들리게.
            float[,] lead =
            {
                { 0.45f, 0 }, { 0.90f, 2 }, { 1.35f, 1 }, { 2.10f, 2 },
                { 3.00f, 0 }, { 3.45f, 1 }, { 4.35f, 2 }, { 5.25f, 1 },
                { 6.15f, 0 }, { 7.05f, 2 }, { 7.50f, 1 }, { 8.40f, 0 },
            };
            for (int c = 2; c < CH; c++)
            {
                float gain = c == 2 ? 0.72f : 1f;
                float t0 = c * seg;
                for (int k = 0; k < lead.GetLength(0); k++)
                {
                    int oct = (k % 4 == 3) ? 24 : 12;      // 가끔 한 옥타브 위에서 튀게
                    SynthLead(d, SRB, t0 + lead[k, 0],
                              Freq(chord[c, (int)lead[k, 1]] + oct),
                              0.28f * gain, 0.42f);
                }
            }

            // ---- 3) 패드 — 아주 작게. 뜯은 현 사이의 빈틈만 메우는 접착제다.
            //      디튠은 넣지 않는다. 맥놀이가 곧 "우우웅" 이기 때문이다.
            float Weight(int c, float t)
            {
                float dt = t - (c + 0.5f) * seg;
                dt -= Mathf.Round(dt / L) * L;              // 루프를 가로질러 최단 거리
                float u = Mathf.Abs(dt) / seg;
                return u >= 1f ? 0f : 0.5f * (1f + Mathf.Cos(Mathf.PI * u));
            }
            int NV = CH * 3;
            var phC = new double[NV]; var incC = new double[NV];
            for (int c = 0; c < CH; c++)
                for (int v = 0; v < 3; v++) incC[c * 3 + v] = Q(Freq(chord[c, v] + 12)) / SRB;
            double phB = 0, incB = 2.0 / L / SRB;           // 호흡 — 루프당 2회라야 이음매가 맞는다

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SRB;
                float breathe = 0.85f + 0.15f * Sin01(phB);
                int cA = (int)Mathf.Floor(t / seg - 0.5f);
                for (int k = 0; k < 2; k++)
                {
                    int c = ((cA + k) % CH + CH) % CH;
                    float w = Weight(c, t);
                    if (w <= 0f) continue;
                    float s = Sin01(phC[c * 3]) + Sin01(phC[c * 3 + 1]) + Sin01(phC[c * 3 + 2]);
                    d[i] += s * 0.333f * w * breathe * 0.18f;
                }
                for (int v = 0; v < NV; v++) phC[v] += incC[v];
                phB += incB;
            }

            Limit(d);
            return d;
        }

        public static AudioClip Bgm() => FromBuffer("bgm_loop", BgmBuffer(), BgmRate);
    }

    // ============================================================
    // 재생 + 볼륨. BGM/효과음 볼륨은 분리되고 PlayerPrefs에 저장된다.
    // ============================================================
    public class GameAudio
    {
        const string KEY_BGM = "popcell.vol.bgm";
        const string KEY_SFX = "popcell.vol.sfx";
        const int POOL = 12;

        readonly AudioSource bgmSrc;
        readonly AudioSource[] pool = new AudioSource[POOL];
        int next;

        readonly AudioClip[] clips = new AudioClip[9];

        float bgmVol = 0.55f, sfxVol = 0.75f;

        // BGM은 29초짜리라 생성이 무거워 시작이 멈칫한다 → 백그라운드에서 만들고 준비되면 튼다.
        // (WebGL 은 스레드가 없어 생성자에서 동기로 만든다 — 아래 #if 참고)
        System.Threading.Tasks.Task<float[]> bgmTask;
        float bgmRetry;   // 브라우저 자동재생 차단 대비 재시도 쿨다운

        // ---- BGM 재생목록 ----
        //   Resources/BGM 의 파일을 이 순서대로 틀고, 마지막 곡이 끝나면 처음으로 돌아간다.
        //   파일 이름 뒤쪽은 만든 사람과 출처 번호다 — 출처를 잃지 않으려고 그대로 둔다.
        //   파일이 하나도 없으면 아래 합성 BGM 으로 떨어진다(빌드에서 파일이 빠져도 무음이 되지 않게).
        static readonly string[] BgmTracks =
        {
            "BGM/BGM_01",   // 0:56
            "BGM/BGM_02",   // 1:57   (원본 monume-retro-arcade-game-music-577980)
            "BGM/BGM_03",   // 1:18   (원본 mondamusic-retro-arcade-game-music-512837)
            "BGM/BGM_04",   // 0:36
        };
        readonly System.Collections.Generic.List<AudioClip> bgmList = new System.Collections.Generic.List<AudioClip>();
        int bgmIdx = -1;
        // 이 곡이 한 번이라도 실제로 울렸는가. "멈춰 있다"의 이유가 둘이라 이게 필요하다 —
        //   아직 한 번도 안 울렸으면 브라우저가 막은 것이고(다시 틀어야 한다),
        //   울리다가 멈췄으면 곡이 끝난 것이다(다음 곡으로 가야 한다).
        //   경과 시간을 직접 세면 안 된다. GameManager 가 넘기는 dt 는 0.1초로 잘리고, 히트스톱이면
        //   Tick 자체를 건너뛰며, timeScale 도 탄다 — 오디오는 그동안에도 흐르므로 항상 모자라게 쌓인다.
        bool bgmStarted;
        // 곡 끝은 점점 작아지며, 다음 곡은 점점 커지며 들어온다. 뚝 끊고 뚝 시작하면 전환이 사고처럼 들린다.
        //   위치는 bgmSrc.time 을 쓴다 — 오디오 시스템이 주는 실제 재생 위치라 프레임 시간과 달리 어긋나지 않는다.
        const float BgmFadeOut = 2.5f, BgmFadeIn = 1.0f;
        float bgmFade = 1f;

        void ApplyBgmVolume() { if (bgmSrc) bgmSrc.volume = bgmVol * 0.85f * bgmFade; }

        // 한 번의 타격 펄스로 20~30마리가 동시에 죽는다 → 그대로 재생하면 소리가 뭉개진다.
        // 토큰 버킷으로 초당 개수를 제한하고 피치를 흩어 "뽁뽁뽁"으로 들리게 한다.
        float popBudget;
        const float POP_PER_SEC = 18f, POP_BURST = 6f;

        public float BgmVolume
        {
            get => bgmVol;
            set { bgmVol = Mathf.Clamp01(value); ApplyBgmVolume(); }
        }
        public float SfxVolume
        {
            get => sfxVol;
            set => sfxVol = Mathf.Clamp01(value);
        }

        public GameAudio(Transform parent)
        {
            bgmVol = PlayerPrefs.GetFloat(KEY_BGM, 0.55f);
            sfxVol = PlayerPrefs.GetFloat(KEY_SFX, 0.75f);

            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);

            bgmSrc = go.AddComponent<AudioSource>();
            bgmSrc.loop = true;
            bgmSrc.playOnAwake = false;
            bgmSrc.spatialBlend = 0f;
            bgmSrc.volume = bgmVol * 0.85f;

            for (int i = 0; i < POOL; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                pool[i] = s;
            }

            clips[(int)Sfx.Pop] = SynthClips.Pop();
            clips[(int)Sfx.Crit] = SynthClips.Crit();
            clips[(int)Sfx.Pulse] = SynthClips.Pulse();
            clips[(int)Sfx.Wave] = SynthClips.Wave();
            clips[(int)Sfx.Buy] = SynthClips.Buy();
            clips[(int)Sfx.Click] = SynthClips.Click();
            clips[(int)Sfx.Boss] = SynthClips.Boss();
            clips[(int)Sfx.Win] = SynthClips.Win();
            clips[(int)Sfx.Timeout] = SynthClips.Timeout();

            foreach (var path in BgmTracks)
            {
                var c = Resources.Load<AudioClip>(path);
                if (c != null) bgmList.Add(c);
                else Debug.LogWarning("[POP Cell] BGM 파일을 못 찾음: " + path);
            }

            if (bgmList.Count > 0)
            {
                bgmSrc.loop = false;    // 한 곡이 끝나면 Tick 이 다음 곡으로 넘긴다
                PlayNextBgm();
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            // 파일이 없을 때만 합성으로 떨어진다. WebGL 은 스레드가 없어 Task.Run 이 돌지 않으므로
            // (브라우저 무음) 여기서는 동기로 만든다 — 그만큼 시작이 멈칫한다.
            else
            {
                bgmSrc.clip = SynthClips.FromBuffer("bgm_loop", SynthClips.BgmBuffer(), SynthClips.BgmRate);
                bgmSrc.Play();
            }
#else
            else bgmTask = System.Threading.Tasks.Task.Run(() => SynthClips.BgmBuffer());
#endif
        }

        void PlayNextBgm()
        {
            if (bgmList.Count == 0) return;
            bgmIdx = (bgmIdx + 1) % bgmList.Count;
            bgmSrc.clip = bgmList[bgmIdx];
            bgmStarted = false;
            bgmFade = 0f;          // 무음에서 시작해 페이드인
            ApplyBgmVolume();
            bgmSrc.Play();
        }

        public void Tick(float dt)
        {
            popBudget = Mathf.Min(POP_BURST, popBudget + dt * POP_PER_SEC);

            // 브라우저는 사용자 조작 전까지 AudioContext 를 정지시켜 둔다 → 로드 직후의 Play() 가
            // 씹힐 수 있다. 클립이 있는데 멈춰 있으면(볼륨이 0이 아닌 한) 다시 틀어준다.
            // 첫 클릭(타이틀의 게임 시작 등)으로 컨텍스트가 살아나면 이 시점에 재생이 붙는다.
            if (bgmList.Count > 0 && bgmSrc != null)
            {
                if (bgmSrc.isPlaying)
                {
                    bgmStarted = true;
                    var cl = bgmSrc.clip;
                    float pos = bgmSrc.time;
                    float len = cl != null ? cl.length : 0f;
                    // 짧은 곡에서 페이드가 곡을 삼키지 않게 길이의 1/4 로 묶는다
                    float fo = Mathf.Min(BgmFadeOut, len * 0.25f);
                    float fi = Mathf.Min(BgmFadeIn, len * 0.25f);
                    float f = 1f;
                    if (fi > 0f && pos < fi) f = pos / fi;
                    if (fo > 0f && len - pos < fo) f = Mathf.Min(f, Mathf.Max(0f, (len - pos) / fo));
                    bgmFade = f;
                    ApplyBgmVolume();
                }
                else if (bgmStarted)
                {
                    PlayNextBgm();      // 울리다가 멈췄다 = 곡이 끝났다
                }
                else if (bgmVol > 0.0001f)
                {
                    bgmRetry -= dt;     // 아직 한 번도 안 울렸다 = 브라우저가 막고 있다
                    if (bgmRetry <= 0f) { bgmRetry = 0.5f; bgmSrc.Play(); }
                }
            }
            else if (bgmSrc != null && bgmSrc.clip != null && !bgmSrc.isPlaying && bgmVol > 0.0001f)
            {
                bgmRetry -= dt;
                if (bgmRetry <= 0f) { bgmRetry = 0.5f; bgmSrc.Play(); }
            }

            if (bgmTask != null && bgmTask.IsCompleted)
            {
                var t = bgmTask;
                bgmTask = null;
                if (!t.IsFaulted && bgmSrc)
                {
                    bgmSrc.clip = SynthClips.FromBuffer("bgm_loop", t.Result, SynthClips.BgmRate);
                    bgmSrc.Play();
                }
                else if (t.IsFaulted)
                {
                    Debug.LogWarning("[POP Cell] BGM 생성 실패: " + t.Exception);
                }
            }
        }

        public void Play(Sfx id, float vol = 1f, float pitch = 1f)
        {
            if (sfxVol <= 0.0001f) return;
            var c = clips[(int)id];
            if (c == null) return;
            var s = pool[next];
            next = (next + 1) % POOL;
            s.clip = c;
            s.volume = Mathf.Clamp01(vol * sfxVol);
            s.pitch = pitch;
            s.Play();
        }

        /// 처치음. 동시에 수십 마리가 죽어도 예산 안에서만 울리고, 피치를 흩어 뽁뽁이처럼 들리게.
        public void PlayKill(bool crit)
        {
            if (popBudget < 1f) return;
            popBudget -= 1f;
            Play(crit ? Sfx.Crit : Sfx.Pop,
                 crit ? 0.62f : 0.55f,
                 Random.Range(0.84f, 1.26f));
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(KEY_BGM, bgmVol);
            PlayerPrefs.SetFloat(KEY_SFX, sfxVol);
            PlayerPrefs.Save();
        }
    }
}
