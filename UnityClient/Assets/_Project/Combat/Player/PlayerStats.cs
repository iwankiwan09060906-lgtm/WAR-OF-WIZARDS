// 서버 권한 플레이어 상태 (§8 PC 서버: HP · 위치 · 슬롯 · 명중률)
// 사람 · 봇이 같은 구조를 쓴다 (§20.1 봇은 서버 안의 가상 플레이어).

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;

namespace SpellboundVR.Combat
{
    /// <summary>§13.3 슬롯 상태 런타임 값</summary>
    public struct SlotRuntime
    {
        public int SpellId;
        public SlotState State;
        public float CooldownEndTime;
        public float CooldownDuration;
        public int HighPowerUsesLeft;
        public bool IsHighPower;
    }

    public sealed class PlayerState
    {
        public readonly Team Team;
        public readonly int Id;
        public readonly SlotRuntime[] Slots = new SlotRuntime[DeckData.SlotCount];

        public bool Present;
        public bool IsBot;
        public bool Connected;
        public float Hp;
        public float MaxHp;
        public int WorldColumn = 1;
        public float Accuracy = 1f;
        public float FocusBonus = 1f;
        public float LastActivityTime;
        public float LastMoveTime = -999f;
        public bool DeathPending;

        public PlayerState(Team team)
        {
            Team = team;
            Id = TeamUtil.PlayerId(team);
        }

        public bool Alive => Hp > 0f;

        public bool IsHuman => Present && !IsBot;

        public void ResetForMatch(float maxHp, float baseAccuracy)
        {
            MaxHp = maxHp;
            Hp = maxHp;
            WorldColumn = 1;
            Accuracy = baseAccuracy;
            FocusBonus = 1f;
            LastActivityTime = 0f;
            LastMoveTime = -999f;
            DeathPending = false;
        }
    }
}
