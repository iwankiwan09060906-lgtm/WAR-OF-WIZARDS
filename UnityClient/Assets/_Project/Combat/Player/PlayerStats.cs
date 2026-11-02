// 서버 권한 플레이어 상태 (§8 PC 서버: HP · 위치 · 슬롯 · 명중률)
// 사람 · 봇이 같은 구조를 쓴다 (§20.1 봇은 서버 안의 가상 플레이어).

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;

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
        /// <summary>S01 연막 — 시야 암전 종료 시각</summary>
        public float BlindUntil = -1f;
        /// <summary>S10 집중 — 다음 스킬에 적용할 배율 (1 = 없음)</summary>
        public float PendingFocus = 1f;
        /// <summary>이 플레이어가 다른 플레이어에게 입힌 피해 합 (개인전 결과 화면)</summary>
        public float DamageDealt;
        /// <summary>4인 개인전: 탈락했는지 (구조물 · 유닛이 사라지고 경기는 계속)</summary>
        public bool Eliminated;
        /// <summary>4인 개인전 순위 1 ~ 4 (0 = 아직 결정 안 됨)</summary>
        public int Place;
        /// <summary>상대에게 보일 시전 준비 (표시 전용)</summary>
        public CastPreview Preview;
        /// <summary>Preview 가 노리는 월드 레인 (조준 전이면 PlayerSnap.NoCastLane)</summary>
        public byte PreviewLane = PlayerSnap.NoCastLane;
        /// <summary>이 시각까지 새 보고가 없으면 Preview 를 지운다 (접속이 끊긴 사람)</summary>
        public float PreviewUntil;

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
            BlindUntil = -1f;
            PendingFocus = 1f;
            Eliminated = false;
            Place = 0;
            DamageDealt = 0f;
            ClearPreview();
        }

        public void ClearPreview()
        {
            Preview = default;
            PreviewLane = PlayerSnap.NoCastLane;
        }

        public bool HasFocus => PendingFocus > 1f;
    }
}
