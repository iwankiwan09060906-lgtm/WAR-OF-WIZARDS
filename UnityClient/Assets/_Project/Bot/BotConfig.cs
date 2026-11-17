// §20.2 봇 — 랜덤 두뇌(TimerRandomBrain) 하나만 쓴다: 랜덤 스킬 · 성공률 60% · 랜덤 시간 · 랜덤 칸 이동
// (VLM · 로컬 모델을 쓰던 AI 전략 두뇌 · 서버 작전 연결은 삭제)

using UnityEngine;

namespace SpellboundVR.Bot
{
    [CreateAssetMenu(menuName = "Spellbound/Bot Config", fileName = "BotConfig")]
    public sealed class BotConfig : ScriptableObject
    {

        [Tooltip("시전 전에 룬을 그리는 모습을 상대에게 보여 주는 시간(초) — 사람의 조준 시간과 비슷하게")]
        public float CastWindupSeconds = 4.5f;

        [Tooltip("룬 그리는 시간 흔들림 — 매 시전 CastWindupSeconds × (1 ~ 1 + 이 값) (사람처럼 빠를 때 · 느릴 때가 있다)")]
        [Range(0f, 1f)] public float CastWindupJitter = 0.5f;

        [Tooltip("룬을 그리다 실패할 확률 (0~1) — 실패하면 시전하지 않고 다음 기회를 기다린다 (0.4 = 성공률 60%)")]
        [Range(0f, 1f)] public float CastFailChance = 0.4f;

        [Header("시전 (§20.2 — 추후 설정)")]
        public float CastIntervalMin = 4f;
        public float CastIntervalMax = 8f;

        [Header("이동 (§3.3 — 확정 5~20초)")]
        public float MoveIntervalMin = 5f;
        public float MoveIntervalMax = 20f;

        [Header("덱")]
        public int MinHighPowerCards = 0;
        public int MaxHighPowerCards = 2;

        [Header("조준")]
        [Range(0f, 1f)] public float AreaDepthMin = 0.6f;
        [Range(0f, 1f)] public float AreaDepthMax = 1f;

        [Header("수동 조종 (§20.3)")]
        [Tooltip("로컬(에디터) 모드에서 봇 수동 키는 Alt를 누른 채 입력해야 동작 (사람 키보드 입력과 충돌 방지)")]
        public bool RequireAltInLocalMode = true;

        public static BotConfig CreateDefault()
        {
            var c = CreateInstance<BotConfig>();
            c.name = "BotConfig (Runtime Default)";
            return c;
        }
    }
}
