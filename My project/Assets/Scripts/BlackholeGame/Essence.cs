namespace BlackholeGame
{
    // 보스 정수(round57) — 지역 보스를 잡으면 그 지역의 정수가 하나 떨어진다.
    //
    //   골드(노드 트리)·샤드(영구 강화)·배양(시간)에 이어 네 번째 축이다. 앞의 셋과 달리
    //   "어느 지역을 몇 번 깼는가"가 그대로 쌓여서, 반복해서 돈 지역이 곧 특화 방향이 된다.
    //   환생해도 남는다.
    //
    //   비용이 레벨만큼 오르는(1,2,3…) 이유: 1지역만 반복해 고정 공격력을 쌓으면 2~3지역이
    //   통째로 날아간다. 깊게 파려면 그 지역을 제곱으로 돌아야 하니 그 길이 막힌다.
    public static class Essence
    {
        public const int Count = 8;
        public const int MaxLv = 20;

        // 레벨 lv -> lv+1 로 올리는 비용(정수 개수). L레벨까지 누적 L(L+1)/2 개.
        public static int CostAt(int lv) => lv + 1;

        // 지역별 강화 1레벨당 효과. 순서는 StageConfig.Stages 와 같다.
        public static readonly float[] PerLv = {
            100f,   // 1 실험실     — 고정 공격력
            1f,     // 2 정착지     — 공격 속도 %
            0.5f,   // 3 도심       — 치명타 확률 %
            2f,     // 4 수로망     — 골드 획득 %
            1.5f,   // 5 항만       — 커서 범위 %
            250f,   // 6 산림       — 타워 고정 공격력
            1.5f,   // 7 설원       — 보스 피해 %
            1f,     // 8 근원       — 모든 피해 %
        };

        public static float ValueAt(int region, int lv) => PerLv[region] * lv;
    }
}
