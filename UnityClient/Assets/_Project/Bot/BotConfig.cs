// §20.2 기본 봇 — 이동 5~20초 랜덤(확정), 시전 간격 추후 설정(SO)
// §20.4 AI 전략 봇(AiStrategyBrain)이 기본. TimerRandom 으로 바꾸면 예전 랜덤 봇.

using UnityEngine;

namespace SpellboundVR.Bot
{
    public enum BotBrainKind
    {
        /// <summary>하이브리드: 전장을 보고 고르는 AI 전략 두뇌(빠른 반사) + 서버 VLM · 학습의 작전(전략)</summary>
        AiStrategy,
        /// <summary>정해진 간격마다 랜덤 슬롯 · 랜덤 조준 (§20.2 기본 봇)</summary>
        TimerRandom,
    }

    [CreateAssetMenu(menuName = "Spellbound/Bot Config", fileName = "BotConfig")]
    public sealed class BotConfig : ScriptableObject
    {
        [Header("두뇌 (§20.4)")]
        public BotBrainKind Brain = BotBrainKind.AiStrategy;

        [Tooltip("AI 전략 두뇌: 시전 사이 최소 간격(초) — 사람이 룬을 그리고 조준하는 시간과 비슷하게")]
        public float AiCastGapSeconds = 2.5f;

        [Tooltip("서버 AI 전략(/api/ai/strategy — VLM 작전 · 판 결과로 배운 것)을 같이 쓴다. 서버에 연결된 경우만")]
        public bool UseServerStrategy = true;

        [Tooltip("서버 전략 요청 간격(초) — 서버(VLM)는 이때마다 작전(orders)과 한 수를 준다")]
        public float ServerAdviceInterval = 3f;

        [Tooltip("하이브리드: 두뇌 점수가 이 값 이상이면(위급) 서버의 한 수보다 두뇌 판단이 먼저 (빠른 반사)")]
        public float EmergencyScore = 6f;

        [Tooltip("봇의 판단 · 경기 결과를 서버 학습 데이터(/api/ai/experience)로 보낸다")]
        public bool ReportExperience = true;

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
