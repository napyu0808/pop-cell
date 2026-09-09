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
        // 힐링 게임풍 — 느긋한 66bpm, C장조에 메이저7 코드로 따뜻하게.
        //   단조 + 8분음표 아르페지오는 긴장감을 주므로 쓰지 않는다.
        //   대신 마디당 3음 정도로 성기게 흐르는 오르골 선율 + 아주 느린 패드.
        //   8마디 29초 루프라 반복이 잘 안 느껴진다.
        //   Unity API를 쓰지 않으므로 백그라운드 스레드에서 생성해도 안전하다.
        public static float[] BgmBuffer()
        {
            const float bpm = 66f;
            float beat = 60f / bpm, bar = beat * 4f;
            const int BARS = 8;
            int SRB = BgmRate;
            int n = (int)(SRB * bar * BARS);
            var d = new float[n];

            //              Cmaj7  Fmaj7  Am7    Fmaj7  Cmaj7  Fmaj7  Dm7    G6sus
            float[] bass = { 36, 41, 45, 41, 36, 41, 38, 43 };
            float[,] chord =
            {
                { 52, 55, 59 },   // Cmaj7  E3 G3 B3
                { 57, 60, 64 },   // Fmaj7  A3 C4 E4
                { 60, 64, 67 },   // Am7    C4 E4 G4
                { 57, 60, 64 },   // Fmaj7
                { 52, 55, 59 },   // Cmaj7
                { 57, 60, 64 },   // Fmaj7
                { 53, 57, 60 },   // Dm7    F3 A3 C4
                { 60, 62, 67 },   // G6sus  C4 D4 G4
            };
            // 오르골 선율: (박 위치, 코드 구성음 인덱스, 옥타브 이동)
            float[] mBeat = { 0f, 1.5f, 2.5f };
            int[] mTone = { 0, 2, 1 };

            for (int b = 0; b < BARS; b++)
            {
                float barT = b * bar;

                // 베이스 — 마디당 1음, 아주 둥글고 낮게
                {
                    float f = Freq(bass[b]), ph = 0f;
                    int i0 = (int)(barT * SRB), i1 = Mathf.Min(n, (int)((barT + bar) * SRB));
                    for (int i = i0; i < i1; i++)
                    {
                        float t = (float)i / SRB - barT;
                        ph += 2f * Mathf.PI * f / SRB;
                        d[i] += Mathf.Sin(ph) * AR(t, bar, 0.10f, 0.9f) * 0.26f;
                    }
                }

                // 패드 — 마디 전체, 느린 호흡. 디튠 2개를 겹쳐 따뜻하게
                for (int c = 0; c < 3; c++)
                {
                    float f = Freq(chord[b, c]);
                    float ph = 0f, ph2 = 0f;
                    int i0 = (int)(barT * SRB), i1 = Mathf.Min(n, (int)((barT + bar) * SRB));
                    for (int i = i0; i < i1; i++)
                    {
                        float t = (float)i / SRB - barT;
                        ph += 2f * Mathf.PI * f / SRB;
                        ph2 += 2f * Mathf.PI * (f * 1.0035f) / SRB;
                        float breathe = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.16f * (barT + t));
                        d[i] += (Mathf.Sin(ph) + Mathf.Sin(ph2)) * 0.5f
                                * AR(t, bar, 0.85f, 0.85f) * breathe * 0.095f;
                    }
                }

                // 오르골 선율 — 성기게. 마디마다 옥타브를 살짝 바꿔 흐름을 준다
                for (int k = 0; k < mBeat.Length; k++)
                {
                    float t0 = barT + mBeat[k] * beat;
                    int oct = (b % 4 == 2 && k == 2) ? 24 : 12;      // 가끔 위에서 반짝
                    float f = Freq(chord[b, mTone[k]] + oct), ph = 0f;
                    float amp = oct > 12 ? 0.055f : 0.085f;
                    int i0 = (int)(t0 * SRB), i1 = Mathf.Min(n, (int)((t0 + beat * 2f) * SRB));
                    for (int i = i0; i < i1; i++)
                    {
                        float t = (float)i / SRB - t0;
                        ph += 2f * Mathf.PI * f / SRB;
                        d[i] += Bell(ph) * Pluck(t, 3.2f) * amp;
                    }
                }
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

        // 한 번의 타격 펄스로 20~30마리가 동시에 죽는다 → 그대로 재생하면 소리가 뭉개진다.
        // 토큰 버킷으로 초당 개수를 제한하고 피치를 흩어 "뽁뽁뽁"으로 들리게 한다.
        float popBudget;
        const float POP_PER_SEC = 18f, POP_BURST = 6f;

        public float BgmVolume
        {
            get => bgmVol;
            set { bgmVol = Mathf.Clamp01(value); if (bgmSrc) bgmSrc.volume = bgmVol * 0.85f; }
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

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 은 스레드가 없다 → Task.Run 이 돌지 않아 BGM 이 영영 안 만들어진다(브라우저 무음).
            // 여기서는 그냥 동기로 만든다. 로딩 직후 ~0.4초 멈칫하지만 스플래시 구간이라 티가 안 난다.
            bgmSrc.clip = SynthClips.FromBuffer("bgm_loop", SynthClips.BgmBuffer(), SynthClips.BgmRate);
            bgmSrc.Play();
#else
            bgmTask = System.Threading.Tasks.Task.Run(() => SynthClips.BgmBuffer());
#endif
        }

        public void Tick(float dt)
        {
            popBudget = Mathf.Min(POP_BURST, popBudget + dt * POP_PER_SEC);

            // 브라우저는 사용자 조작 전까지 AudioContext 를 정지시켜 둔다 → 로드 직후의 Play() 가
            // 씹힐 수 있다. 클립이 있는데 멈춰 있으면(볼륨이 0이 아닌 한) 다시 틀어준다.
            // 첫 클릭(타이틀의 게임 시작 등)으로 컨텍스트가 살아나면 이 시점에 재생이 붙는다.
            if (bgmSrc != null && bgmSrc.clip != null && !bgmSrc.isPlaying && bgmVol > 0.0001f)
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
