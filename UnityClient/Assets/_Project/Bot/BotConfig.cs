// §20.2 기본 봇 — 이동 5~20초 랜덤(확정), 시전 간격 추후 설정(SO)

using UnityEngine;

namespace SpellboundVR.Bot
{
    [CreateAssetMenu(menuName = "Spellbound/Bot Config", fileName = "BotConfig")]
    public sealed class BotConfig : ScriptableObject
    {
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
